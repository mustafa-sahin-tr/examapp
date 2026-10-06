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

        await _badgeReset.Received(1).ResetUserAsync(UserId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
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

            ctx.StudentPoints.Add(new StudentPoint { StudentId = studentId, XP = 100 });
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

        await _badgeReset.Received(1).ResetUserAsync(UserId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    // ---- issue #396: BadgeService first; a failed BadgeService reset leaves exam data intact ----

    private async Task<(int StudentId, int InstanceId, int AssignmentId)> SeedProgressAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "6" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();
        var ws = new Worksheet { Name = "W", Description = "", GradeId = grade.Id };
        var student = new Student { UserId = UserId, StudentNumber = "70", SchoolName = "S", GradeId = grade.Id };
        ctx.AddRange(ws, student);
        await ctx.SaveChangesAsync();

        var instance = new WorksheetInstance
        {
            StudentId = student.Id, WorksheetId = ws.Id, Status = WorksheetInstanceStatus.Completed,
            StartTime = DateTime.UtcNow.AddHours(-1), EndTime = DateTime.UtcNow,
        };
        var assignment = new WorksheetAssignment { WorksheetId = ws.Id, StudentId = student.Id, StartAt = DateTime.UtcNow };
        ctx.TestInstances.Add(instance);
        ctx.WorksheetAssignments.Add(assignment);
        ctx.StudentPoints.Add(new StudentPoint { StudentId = student.Id, XP = 100 });
        await ctx.SaveChangesAsync();
        return (student.Id, instance.Id, assignment.Id);
    }

    [Fact]
    public async Task A_failed_badge_reset_throws_and_leaves_the_exam_data_untouched()
    {
        var seed = await SeedProgressAsync();
        _badgeReset.ResetUserAsync(UserId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new HttpRequestException("badge down")));

        await using (var ctx = _db.NewContext())
            await Should.ThrowAsync<HttpRequestException>(() => NewJob(ctx).RunAsync(UserId, seed.StudentId, KeycloakId));

        // Exam data intact → the student cannot re-solve old tests while BadgeService still holds the old total;
        // Hangfire retries the job (exception propagates).
        await using var check = _db.NewContext();
        (await check.TestInstances.AnyAsync(x => x.Id == seed.InstanceId)).ShouldBeTrue();
        (await check.WorksheetAssignments.AnyAsync(x => x.Id == seed.AssignmentId)).ShouldBeTrue();
        (await check.StudentPoints.AnyAsync(x => x.StudentId == seed.StudentId)).ShouldBeTrue();
    }

    [Fact]
    public async Task The_badge_reset_runs_before_the_exam_data_is_deleted()
    {
        var seed = await SeedProgressAsync();
        bool? instanceExistedDuringBadgeReset = null;
        _badgeReset.ResetUserAsync(UserId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            await using var probe = _db.NewContext();
            instanceExistedDuringBadgeReset = await probe.TestInstances.AnyAsync(x => x.Id == seed.InstanceId);
        });

        await using (var ctx = _db.NewContext())
            await NewJob(ctx).RunAsync(UserId, seed.StudentId, KeycloakId);

        instanceExistedDuringBadgeReset.ShouldBe(true);
        await using var check = _db.NewContext();
        (await check.TestInstances.AnyAsync(x => x.Id == seed.InstanceId)).ShouldBeFalse();
    }

    [Fact]
    public async Task A_retry_after_a_failed_badge_reset_completes_the_reset()
    {
        var seed = await SeedProgressAsync();
        var calls = 0;
        _badgeReset.ResetUserAsync(UserId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++calls == 1 ? Task.FromException(new HttpRequestException("badge down")) : Task.CompletedTask);

        await using (var ctx = _db.NewContext())
            await Should.ThrowAsync<HttpRequestException>(() => NewJob(ctx).RunAsync(UserId, seed.StudentId, KeycloakId));
        await using (var ctx = _db.NewContext())
            await NewJob(ctx).RunAsync(UserId, seed.StudentId, KeycloakId);

        await using var check = _db.NewContext();
        (await check.TestInstances.AnyAsync(x => x.Id == seed.InstanceId)).ShouldBeFalse();
        (await check.StudentPoints.AnyAsync(x => x.StudentId == seed.StudentId)).ShouldBeFalse();
    }

    [Fact]
    public async Task Open_sessions_are_closed_before_the_badge_reset_and_the_reset_line_comes_after_them()
    {
        var seed = await SeedProgressAsync();
        int openId;
        await using (var ctx = _db.NewContext())
        {
            var ws = new Worksheet { Name = "Open", Description = "", GradeId = (await ctx.Grades.FirstAsync()).Id };
            ctx.Worksheets.Add(ws);
            await ctx.SaveChangesAsync();
            var open = new WorksheetInstance
            {
                StudentId = seed.StudentId, WorksheetId = ws.Id, Status = WorksheetInstanceStatus.Started,
                StartTime = DateTime.UtcNow.AddMinutes(-2),
            };
            ctx.TestInstances.Add(open);
            await ctx.SaveChangesAsync();
            openId = open.Id;
        }

        WorksheetInstanceStatus? statusDuringBadgeReset = null;
        DateTime? closedAt = null;
        DateTime? sentResetAt = null;
        _badgeReset.ResetUserAsync(UserId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Returns(async call =>
        {
            sentResetAt = call.ArgAt<DateTime>(1);
            await using var probe = _db.NewContext();
            var row = await probe.TestInstances.FirstAsync(x => x.Id == openId);
            statusDuringBadgeReset = row.Status;
            closedAt = row.EndTime;
        });

        var before = DateTime.UtcNow;
        await using (var ctx = _db.NewContext())
            await NewJob(ctx).RunAsync(UserId, seed.StudentId, KeycloakId);

        statusDuringBadgeReset.ShouldBe(WorksheetInstanceStatus.Expired);
        sentResetAt.ShouldNotBeNull();
        sentResetAt!.Value.Kind.ShouldBe(DateTimeKind.Utc);
        sentResetAt.Value.ShouldBeGreaterThanOrEqualTo(before);
        sentResetAt.Value.ShouldBeGreaterThanOrEqualTo(closedAt!.Value);
    }

    [Fact]
    public async Task A_failed_badge_reset_still_leaves_the_open_session_closed_but_keeps_all_data()
    {
        var seed = await SeedProgressAsync();
        int openId;
        await using (var ctx = _db.NewContext())
        {
            var ws = new Worksheet { Name = "Open", Description = "", GradeId = (await ctx.Grades.FirstAsync()).Id };
            ctx.Worksheets.Add(ws);
            await ctx.SaveChangesAsync();
            var open = new WorksheetInstance
            {
                StudentId = seed.StudentId, WorksheetId = ws.Id, Status = WorksheetInstanceStatus.Started, StartTime = DateTime.UtcNow,
            };
            ctx.TestInstances.Add(open);
            await ctx.SaveChangesAsync();
            openId = open.Id;
        }
        _badgeReset.ResetUserAsync(UserId, Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new HttpRequestException("badge down")));

        await using (var ctx = _db.NewContext())
            await Should.ThrowAsync<HttpRequestException>(() => NewJob(ctx).RunAsync(UserId, seed.StudentId, KeycloakId));

        await using var check = _db.NewContext();
        var open2 = await check.TestInstances.FirstAsync(x => x.Id == openId);
        open2.Status.ShouldBe(WorksheetInstanceStatus.Expired);
        open2.UpdateUserId.ShouldBe(UserId);
        (await check.TestInstances.AnyAsync(x => x.Id == seed.InstanceId)).ShouldBeTrue();
    }

    [Fact]
    public void The_job_carries_the_final_failure_alert_filter()
    {
        typeof(StudentResetJob).GetCustomAttributes(typeof(StudentResetFailureAlertAttribute), inherit: false)
            .ShouldHaveSingleItem();
    }

    [Fact]
    public void The_failure_alert_names_the_user_and_job_but_not_the_keycloak_sub()
    {
        var text = StudentResetFailureAlertAttribute.Describe("123", new object?[] { UserId, StudentId, KeycloakId });

        text.ShouldStartWith(StudentResetFailureAlertAttribute.AlertMarker);
        text.ShouldContain("jobId=123");
        text.ShouldContain($"userId={UserId}");
        text.ShouldContain($"studentId={StudentId}");
        text.ShouldNotContain(KeycloakId);
        StudentResetFailureAlertAttribute.Describe(null, null).ShouldContain("userId=?");
    }

    // issue #396 review: the filter itself, with a Failed state, logs at Error through Hangfire's log provider; any other
    // state (e.g. the Scheduled retry) logs nothing.
    [Fact]
    public void The_filter_logs_an_error_only_when_the_job_lands_in_Failed()
    {
        var provider = new RecordingLogProvider();
        {
            var job = Hangfire.Common.Job.FromExpression<StudentResetJob>(j => j.RunAsync(UserId, StudentId, KeycloakId));
            var background = new Hangfire.BackgroundJob("job-1", job, DateTime.UtcNow);
            // Same category the production constructor uses (LogProvider.GetLogger(typeof(StudentResetJob))).
            var filter = new StudentResetFailureAlertAttribute(() => provider.GetLogger(typeof(StudentResetJob).FullName!));

            filter.OnStateApplied(Context(background, new Hangfire.States.ScheduledState(TimeSpan.FromMinutes(1))), Substitute.For<Hangfire.Storage.IWriteOnlyTransaction>());
            Mine(provider).ShouldBeEmpty();

            var boom = new HttpRequestException("badge down");
            filter.OnStateApplied(Context(background, new Hangfire.States.FailedState(boom)), Substitute.For<Hangfire.Storage.IWriteOnlyTransaction>());

            var entry = Mine(provider).ShouldHaveSingleItem();
            entry.Level.ShouldBe(Hangfire.Logging.LogLevel.Error);
            entry.Logger.ShouldBe(typeof(StudentResetJob).FullName);
            entry.Message.ShouldStartWith(StudentResetFailureAlertAttribute.AlertMarker);
            entry.Message.ShouldContain("jobId=job-1");
            entry.Message.ShouldContain($"userId={UserId}");
            entry.Exception.ShouldBeSameAs(boom);
        }
    }

    private static Hangfire.States.ApplyStateContext Context(Hangfire.BackgroundJob job, Hangfire.States.IState state) =>
        new(Substitute.For<Hangfire.JobStorage>(),
            Substitute.For<Hangfire.Storage.IStorageConnection>(),
            Substitute.For<Hangfire.Storage.IWriteOnlyTransaction>(),
            job, state, oldStateName: "Processing");

    private static List<LogEntry> Mine(RecordingLogProvider provider)
    {
        lock (provider.Entries)
            return provider.Entries.Where(x => x.Logger == typeof(StudentResetJob).FullName).ToList();
    }

    private sealed record LogEntry(string Logger, Hangfire.Logging.LogLevel Level, string Message, Exception? Exception);

    private sealed class RecordingLogProvider : Hangfire.Logging.ILogProvider
    {
        public List<LogEntry> Entries { get; } = new();

        public Hangfire.Logging.ILog GetLogger(string name) => new RecordingLog(name, Entries);

        private sealed class RecordingLog(string name, List<LogEntry> entries) : Hangfire.Logging.ILog
        {
            public bool Log(Hangfire.Logging.LogLevel logLevel, Func<string>? messageFunc, Exception? exception = null)
            {
                if (messageFunc == null)
                    return true; // "is enabled" probe
                lock (entries)
                    entries.Add(new LogEntry(name, logLevel, messageFunc(), exception));
                return true;
            }
        }
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
        await Should.ThrowAsync<InvalidOperationException>(() => client.ResetUserAsync(1, DateTime.UtcNow, default));
    }

    [Fact]
    public async Task Succeeds_on_the_first_candidate_url_that_returns_success()
    {
        var http = new StubHttp(_ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var resetAt = new DateTime(2026, 10, 6, 21, 15, 30, 123, DateTimeKind.Utc);
        await NewClient(http).ResetUserAsync(42, resetAt, default);
        var uri = http.Requests.ShouldHaveSingleItem().RequestUri!;
        uri.AbsolutePath.ShouldEndWith("/api/reset/users/42");
        // issue #396: the reset line travels as a round-trip ISO timestamp with offset (single exam-API clock).
        var sent = Uri.UnescapeDataString(uri.Query).Replace("?resetAtUtc=", "");
        DateTimeOffset.Parse(sent, System.Globalization.CultureInfo.InvariantCulture).UtcDateTime.ShouldBe(resetAt);
    }

    [Fact]
    public async Task Falls_through_404s_to_the_next_candidate()
    {
        var http = new StubHttp(req => new HttpResponseMessage(
            req.RequestUri!.AbsolutePath.Contains("/api/badge/reset") ? HttpStatusCode.OK : HttpStatusCode.NotFound));

        await NewClient(http).ResetUserAsync(9, DateTime.UtcNow, default);
        http.Requests.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Error_messages_cap_the_response_body()
    {
        var http = new StubHttp(HttpStatusCode.InternalServerError, new string('x', 10_000));
        var ex = await Should.ThrowAsync<HttpRequestException>(() => NewClient(http).ResetUserAsync(9, DateTime.UtcNow, default));
        ex.Message.Length.ShouldBeLessThan(BadgeResetApiClient.MaxErrorBodyLength + 400);
        ex.Message.ShouldContain("(truncated)");
    }

    [Fact]
    public async Task Fails_fast_on_a_non_404_error()
    {
        var http = new StubHttp(HttpStatusCode.Unauthorized, "nope");
        await Should.ThrowAsync<HttpRequestException>(() => NewClient(http).ResetUserAsync(9, DateTime.UtcNow, default));
    }

    [Fact]
    public async Task Throws_when_every_candidate_returns_404()
    {
        var http = new StubHttp(HttpStatusCode.NotFound, "");
        var ex = await Should.ThrowAsync<HttpRequestException>(() => NewClient(http).ResetUserAsync(9, DateTime.UtcNow, default));
        ex.Message.ShouldContain("not found");
    }
}

public class StudentResetServiceTokenProviderTests
{
    private static readonly Dictionary<string, string?> FullConfig = new()
    {
        ["Keycloak:Host"] = "http://kc:8080/",
        ["Keycloak:TokenUrl"] = "/realms/exam/protocol/openid-connect/token",
        ["Keycloak:ServiceClientSecret"] = "example-svc-value",
        // Issue #372: admin / exam-client credentials must never be used for service tokens.
        ["Keycloak:AdminClientId"] = "exam-admin",
        ["Keycloak:AdminClientSecret"] = "example-admin-value",
        ["Keycloak:ClientId"] = "exam-client",
        ["Keycloak:ClientSecret"] = "example-client-value",
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
    public async Task Does_not_fall_back_to_admin_or_exam_client_credentials()
    {
        var http = Ok("tok-abc");
        var provider = NewProvider(http, new Dictionary<string, string?>
        {
            ["Keycloak:Host"] = "http://kc", ["Keycloak:TokenUrl"] = "/token",
            ["Keycloak:AdminClientId"] = "exam-admin", ["Keycloak:AdminClientSecret"] = "example-admin-value",
            ["Keycloak:ClientId"] = "exam-client", ["Keycloak:ClientSecret"] = "example-client-value",
        });

        await Should.ThrowAsync<InvalidOperationException>(() => provider.GetAccessTokenAsync(default));
        http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Requests_the_token_with_the_exam_service_client()
    {
        string? body = null;
        var http = new StubHttp(req =>
        {
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"access_token":"t","expires_in":300}"""),
            };
        });

        await NewProvider(http).GetAccessTokenAsync(default);

        body.ShouldNotBeNull();
        body.ShouldContain("client_id=exam-service");
        body.ShouldContain("client_secret=example-svc-value");
        body.ShouldNotContain("exam-admin");
        body.ShouldNotContain("example-admin-value");
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
