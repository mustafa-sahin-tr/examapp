using System.Net;
using ExamApp.Api.Data;
using ExamApp.Api.Services.StudentReset;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace ExamApp.Api.Tests.Services;

public class StudentResetJobTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IBadgeResetApiClient _badgeReset = Substitute.For<IBadgeResetApiClient>();

    private StudentResetJob NewJob(AppDbContext ctx) => new(ctx, _badgeReset);

    private const int UserId = 7;
    private const int StudentId = 70;
    private const string KeycloakId = "kc-7";

    [Theory]
    [InlineData(0, StudentId, KeycloakId)]
    [InlineData(UserId, 0, KeycloakId)]
    [InlineData(UserId, StudentId, "  ")]
    public async Task Rejects_invalid_arguments(int userId, int studentId, string keycloakId)
    {
        await using var ctx = _db.NewContext();
        await Should.ThrowAsync<ArgumentException>(() => NewJob(ctx).RunAsync(userId, studentId, keycloakId));
    }

    [Fact]
    public async Task Runs_with_no_data_and_still_delegates_to_the_badge_service()
    {
        await using (var ctx = _db.NewContext())
            await NewJob(ctx).RunAsync(UserId, StudentId, KeycloakId);

        await _badgeReset.Received(1).ResetUserAsync(UserId, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Soft_deletes_the_students_progress_but_leaves_grade_scoped_assignments_intact()
    {
        int studentId, personalAssignmentId, gradeAssignmentId, programId;
        await using (var ctx = _db.NewContext())
        {
            var grade = new Grade { Name = "6" };
            var subject = new Subject { Name = "Mat" };
            ctx.AddRange(grade, subject);
            await ctx.SaveChangesAsync();

            var ws = new Worksheet { Name = "W", Description = "", GradeId = grade.Id };
            var student = new Student { UserId = UserId, StudentNumber = "70", SchoolName = "S", GradeId = grade.Id };
            ctx.AddRange(ws, student);
            await ctx.SaveChangesAsync();
            studentId = student.Id;

            ctx.StudentPoints.Add(new StudentPoint { StudentId = studentId, XP = 100, Level = 3 });
            ctx.StudentPointHistories.Add(new StudentPointHistory { StudentId = studentId, Points = 10, Reason = "Doğru Cevap" });
            ctx.Leaderboards.Add(new Leaderboard { StudentId = studentId, TotalPoints = 100, Rank = 1, TimePeriod = "Weekly" });

            var personal = new WorksheetAssignment { WorksheetId = ws.Id, StudentId = studentId, StartAt = DateTime.UtcNow };
            var gradeScoped = new WorksheetAssignment { WorksheetId = ws.Id, GradeId = grade.Id, StartAt = DateTime.UtcNow };
            ctx.WorksheetAssignments.AddRange(personal, gradeScoped);

            var program = new UserProgram
            {
                UserId = KeycloakId, ProgramName = "P", Description = "d",
                StudyType = "time", StudyDuration = "25-5", RestDays = "", DifficultSubjects = "",
            };
            ctx.UserPrograms.Add(program);
            await ctx.SaveChangesAsync();
            personalAssignmentId = personal.Id;
            gradeAssignmentId = gradeScoped.Id;
            programId = program.Id;

            ctx.UserProgramSchedules.Add(new UserProgramSchedule
            {
                UserProgramId = program.Id, ScheduleDate = DateTime.UtcNow, SubjectId = subject.Id, SubjectName = "Mat", Notes = "",
            });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
            await NewJob(ctx).RunAsync(UserId, studentId, KeycloakId);

        await using var check = _db.NewContext();
        (await check.StudentPoints.AnyAsync(x => x.StudentId == studentId)).ShouldBeFalse();
        (await check.StudentPointHistories.AnyAsync(x => x.StudentId == studentId)).ShouldBeFalse();
        (await check.Leaderboards.AnyAsync(x => x.StudentId == studentId)).ShouldBeFalse();
        (await check.UserPrograms.AnyAsync(x => x.Id == programId)).ShouldBeFalse();
        (await check.UserProgramSchedules.AnyAsync(x => x.UserProgramId == programId)).ShouldBeFalse();

        // personal assignment gone, grade-scoped one untouched
        (await check.WorksheetAssignments.AnyAsync(x => x.Id == personalAssignmentId)).ShouldBeFalse();
        (await check.WorksheetAssignments.AnyAsync(x => x.Id == gradeAssignmentId)).ShouldBeTrue();

        await _badgeReset.Received(1).ResetUserAsync(UserId, Arg.Any<CancellationToken>());
    }

    public void Dispose() => _db.Dispose();
}

public class BadgeResetApiClientTests
{
    private readonly IServiceTokenProvider _token = Substitute.For<IServiceTokenProvider>();

    private BadgeResetApiClient NewClient(StubHttp http, string? baseUrl = "http://badge:8006")
    {
        _token.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("tok");
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["BadgeApiBaseUrl"] = baseUrl })
            .Build();
        return new BadgeResetApiClient(http, config, _token);
    }

    [Fact]
    public async Task Throws_when_the_base_url_is_not_configured()
    {
        var client = NewClient(new StubHttp(HttpStatusCode.OK, ""), baseUrl: null);
        await Should.ThrowAsync<InvalidOperationException>(() => client.ResetUserAsync(1, default));
    }

    [Fact]
    public async Task Succeeds_on_the_first_candidate_url_that_returns_success()
    {
        var http = new StubHttp(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        await NewClient(http).ResetUserAsync(42, default);
        http.Requests.ShouldHaveSingleItem().RequestUri!.ToString().ShouldEndWith("/api/reset/users/42");
    }

    [Fact]
    public async Task Falls_through_404s_to_the_next_candidate()
    {
        var http = new StubHttp(req => new HttpResponseMessage(
            req.RequestUri!.AbsolutePath.Contains("/api/badge/reset") ? HttpStatusCode.OK : HttpStatusCode.NotFound));

        await NewClient(http).ResetUserAsync(9, default);
        http.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Fails_fast_on_a_non_404_error()
    {
        var http = new StubHttp(HttpStatusCode.Unauthorized, "nope");
        await Should.ThrowAsync<HttpRequestException>(() => NewClient(http).ResetUserAsync(9, default));
    }

    [Fact]
    public async Task Throws_when_every_candidate_returns_404()
    {
        var http = new StubHttp(HttpStatusCode.NotFound, "");
        var ex = await Should.ThrowAsync<HttpRequestException>(() => NewClient(http).ResetUserAsync(9, default));
        ex.Message.ShouldContain("not found");
    }
}

public class StudentResetServiceTokenProviderTests
{
    private static readonly Dictionary<string, string?> FullConfig = new()
    {
        ["Keycloak:Host"] = "http://kc:8080/",
        ["Keycloak:TokenUrl"] = "/realms/exam/protocol/openid-connect/token",
        ["Keycloak:AdminClientId"] = "admin-cli",
        ["Keycloak:AdminClientSecret"] = "sekret",
    };

    private static ServiceTokenProvider NewProvider(StubHttp http, Dictionary<string, string?>? config = null)
        => new(http, new ConfigurationBuilder().AddInMemoryCollection(config ?? FullConfig).Build());

    private static StubHttp Ok(string accessToken, int expiresIn = 300) => new(_ =>
        new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($$"""{"access_token":"{{accessToken}}","expires_in":{{expiresIn}}}"""),
        });

    [Fact]
    public async Task Throws_when_keycloak_host_or_token_url_is_missing()
    {
        var provider = NewProvider(Ok("t"), new Dictionary<string, string?> { ["Keycloak:Host"] = "http://kc" });
        await Should.ThrowAsync<InvalidOperationException>(() => provider.GetAccessTokenAsync(default));
    }

    [Fact]
    public async Task Throws_when_no_client_credentials_are_configured()
    {
        var provider = NewProvider(Ok("t"), new Dictionary<string, string?>
        {
            ["Keycloak:Host"] = "http://kc", ["Keycloak:TokenUrl"] = "/token",
        });
        await Should.ThrowAsync<InvalidOperationException>(() => provider.GetAccessTokenAsync(default));
    }

    [Fact]
    public async Task Falls_back_to_the_non_admin_client_credentials()
    {
        var http = Ok("tok-abc");
        var provider = NewProvider(http, new Dictionary<string, string?>
        {
            ["Keycloak:Host"] = "http://kc", ["Keycloak:TokenUrl"] = "/token",
            ["Keycloak:ClientId"] = "exam-api", ["Keycloak:ClientSecret"] = "s",
        });

        (await provider.GetAccessTokenAsync(default)).ShouldBe("tok-abc");
    }

    [Fact]
    public async Task Returns_the_token_and_caches_it_for_the_next_call()
    {
        var http = Ok("tok-1");
        var provider = NewProvider(http);

        (await provider.GetAccessTokenAsync(default)).ShouldBe("tok-1");
        (await provider.GetAccessTokenAsync(default)).ShouldBe("tok-1");
        http.Requests.Count.ShouldBe(1); // second call served from cache
    }

    [Fact]
    public async Task Does_not_cache_a_token_that_is_already_near_expiry()
    {
        var http = new StubHttp(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("""{"access_token":"short","expires_in":10}"""),
        });
        var provider = NewProvider(http);

        await provider.GetAccessTokenAsync(default);
        await provider.GetAccessTokenAsync(default);
        http.Requests.Count.ShouldBe(2); // 10s < 30s safety window -> refetched
    }

    [Fact]
    public async Task Surfaces_the_keycloak_error_description_on_failure()
    {
        var http = new StubHttp(HttpStatusCode.Unauthorized,
            """{"error":"invalid_client","error_description":"bad secret"}""");
        var ex = await Should.ThrowAsync<InvalidOperationException>(() => NewProvider(http).GetAccessTokenAsync(default));
        ex.Message.ShouldContain("bad secret");
    }

    [Fact]
    public async Task Throws_when_the_response_has_no_access_token()
    {
        var http = new StubHttp(HttpStatusCode.OK, """{"expires_in":300}""");
        await Should.ThrowAsync<InvalidOperationException>(() => NewProvider(http).GetAccessTokenAsync(default));
    }
}
