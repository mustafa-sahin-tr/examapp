using System.Security.Claims;
using System.Text.Json;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #277 (madde 4): register akışları rol gerçekten değiştiğinde <see cref="UserRoleChangedEvent"/> outbox satırını
/// kayıtla AYNI transaction/SaveChanges'te yazar (exam DB'de yerel Users tablosu yok); rol aynıysa yazmaz; retry-safe.
/// </summary>
public class UserRoleChangedOutboxTests : IDisposable
{
    private const string Sub = "kc-sub-1";

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    public void Dispose() => _db.Dispose();

    private static string RoleEventType => OutboxEventRegistry.NameFor<UserRoleChangedEvent>();

    private async Task<List<UserRoleChangedEvent>> RoleEventsAsync()
    {
        await using var ctx = _db.NewContext();
        return (await ctx.OutboxMessages.AsNoTracking().Where(m => m.Type == RoleEventType).ToListAsync())
            .Select(m => JsonSerializer.Deserialize<UserRoleChangedEvent>(m.Content)!)
            .ToList();
    }

    private async Task<int> SeedGradeAsync()
    {
        await using var ctx = _db.NewContext();
        var g = new Grade { Name = "7" };
        ctx.Grades.Add(g);
        await ctx.SaveChangesAsync();
        return g.Id;
    }

    // ---- helper ----

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("Student", true)]
    [InlineData("Teacher", false)]
    public void IsChange_compares_the_current_role_with_the_new_one(string? current, bool expected)
        => UserRoleChangeOutbox.IsChange(new UserRoleChangeRequest(Sub, 1, current), UserRole.Teacher).ShouldBe(expected);

    [Fact]
    public void IsChange_is_false_without_request_or_keycloak_id()
    {
        UserRoleChangeOutbox.IsChange(null, UserRole.Teacher).ShouldBeFalse();
        UserRoleChangeOutbox.IsChange(new UserRoleChangeRequest(" ", 1, null), UserRole.Teacher).ShouldBeFalse();
    }

    // ---- Teacher ----

    [Fact]
    public async Task Teacher_registration_with_a_role_change_writes_exactly_one_event()
    {
        await using (var ctx = _db.NewContext())
            (await new TeacherService(ctx, _authApi).Save(10, new RegisterTeacherDto(), new UserRoleChangeRequest(Sub, 10, null)))
                .Success.ShouldBeTrue();

        var e = (await RoleEventsAsync()).ShouldHaveSingleItem();
        e.KeycloakId.ShouldBe(Sub);
        e.UserId.ShouldBe(10);
        e.NewRole.ShouldBe("Teacher");
        e.EventId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Teacher_registration_without_a_role_change_writes_no_event()
    {
        await using (var ctx = _db.NewContext())
            (await new TeacherService(ctx, _authApi).Save(11, new RegisterTeacherDto(), new UserRoleChangeRequest(Sub, 11, "Teacher")))
                .Success.ShouldBeTrue();
        await using (var ctx = _db.NewContext())
            (await new TeacherService(ctx, _authApi).Save(11, new RegisterTeacherDto())).Success.ShouldBeTrue();

        (await RoleEventsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Rejected_teacher_registration_writes_no_event()
    {
        var gradeId = await SeedGradeAsync();
        await using (var ctx = _db.NewContext())
        {
            ctx.Students.Add(new Student { UserId = 12, StudentNumber = "n", GradeId = gradeId });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
            (await new TeacherService(ctx, _authApi).Save(12, new RegisterTeacherDto(), new UserRoleChangeRequest(Sub, 12, "Student")))
                .Conflict.ShouldBeTrue();

        (await RoleEventsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Teacher_role_event_survives_a_transient_commit_failure_with_a_stable_event_id()
    {
        var interceptor = new FailFirstCommitInterceptor();
        await using (var ctx = _db.NewContextWithTransientRetry(interceptor))
            (await new TeacherService(ctx, _authApi).Save(13, new RegisterTeacherDto { IsIndependentTutor = true },
                new UserRoleChangeRequest(Sub, 13, "Student"))).Success.ShouldBeTrue();

        interceptor.Failures.ShouldBe(1);
        (await RoleEventsAsync()).ShouldHaveSingleItem().NewRole.ShouldBe("Teacher");
    }

    [Fact]
    public async Task Teacher_registration_runs_under_the_retrying_strategy_guard()
    {
        await using (var ctx = _db.NewContextWithRetryingExecutionStrategy())
            (await new TeacherService(ctx, _authApi).Save(14, new RegisterTeacherDto(), new UserRoleChangeRequest(Sub, 14, null)))
                .Success.ShouldBeTrue();

        (await RoleEventsAsync()).Count.ShouldBe(1);
    }

    // ---- Student ----

    private StudentService NewStudentService(AppDbContext ctx) => new(ctx, _authApi, new SchoolAccessPolicy(ctx));

    [Fact]
    public async Task New_student_with_a_role_change_writes_exactly_one_event()
    {
        var gradeId = await SeedGradeAsync();
        await using (var ctx = _db.NewContext())
            (await NewStudentService(ctx).Save(20, new RegisterStudentDto { StudentNumber = "n", GradeId = gradeId },
                new UserRoleChangeRequest(Sub, 20, null))).Success.ShouldBeTrue();

        var e = (await RoleEventsAsync()).ShouldHaveSingleItem();
        e.KeycloakId.ShouldBe(Sub);
        e.NewRole.ShouldBe("Student");
    }

    [Fact]
    public async Task Existing_student_update_writes_an_event_only_when_the_role_changes()
    {
        var gradeId = await SeedGradeAsync();
        await using (var ctx = _db.NewContext())
            (await NewStudentService(ctx).Save(21, new RegisterStudentDto { StudentNumber = "n", GradeId = gradeId })).Success.ShouldBeTrue();

        await using (var ctx = _db.NewContext())
            (await NewStudentService(ctx).Save(21, new RegisterStudentDto { StudentNumber = "n2", GradeId = gradeId },
                new UserRoleChangeRequest(Sub, 21, "Student"))).Success.ShouldBeTrue();
        (await RoleEventsAsync()).ShouldBeEmpty();

        await using (var ctx = _db.NewContext())
            (await NewStudentService(ctx).Save(21, new RegisterStudentDto { StudentNumber = "n3", GradeId = gradeId },
                new UserRoleChangeRequest(Sub, 21, ""))).Success.ShouldBeTrue();
        (await RoleEventsAsync()).ShouldHaveSingleItem().NewRole.ShouldBe("Student");
    }

    [Fact]
    public async Task New_student_role_event_survives_a_transient_commit_failure()
    {
        var gradeId = await SeedGradeAsync();
        var interceptor = new FailFirstCommitInterceptor();
        await using (var ctx = _db.NewContextWithTransientRetry(interceptor))
            (await NewStudentService(ctx).Save(22, new RegisterStudentDto { StudentNumber = "n", GradeId = gradeId },
                new UserRoleChangeRequest(Sub, 22, null))).Success.ShouldBeTrue();

        interceptor.Failures.ShouldBe(1);
        (await RoleEventsAsync()).Count.ShouldBe(1);
        await using var check = _db.NewContext();
        (await check.Students.CountAsync(s => s.UserId == 22)).ShouldBe(1);
    }

    // ---- Parent (controller: satır + event tek SaveChanges) ----

    private ParentController NewParentController(AppDbContext ctx, string? currentRole, IKeycloakService keycloak)
    {
        var profiles = Substitute.For<IUserProfileProvider>();
        profiles.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new UserProfileDto { Id = 30, KeycloakId = Sub, Role = currentRole! });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(profiles);
        services.AddSingleton<IDistributedCache>(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
        services.AddSingleton<UserProfileCacheService>();

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, Sub)], "Test")),
            RequestServices = services.BuildServiceProvider()
        };
        http.Request.Headers.Cookie = "refresh_token=rt";
        return new ParentController(ctx, keycloak) { ControllerContext = new ControllerContext { HttpContext = http } };
    }

    [Theory]
    [InlineData(null, 1)]
    [InlineData("Parent", 0)]
    public async Task Parent_registration_writes_the_parent_row_and_an_event_only_on_role_change(string? currentRole, int expectedEvents)
    {
        var keycloak = Substitute.For<IKeycloakService>();
        keycloak.RefreshTokenAsync(Arg.Any<string>()).Returns(new TokenResponseDto { AccessToken = "at", RefreshToken = "" });

        await using (var ctx = _db.NewContext())
            (await NewParentController(ctx, currentRole, keycloak).RegisterParent()).ShouldBeOfType<OkObjectResult>();

        await using var check = _db.NewContext();
        (await check.Parents.CountAsync(p => p.UserId == 30)).ShouldBe(1);
        var events = await RoleEventsAsync();
        events.Count.ShouldBe(expectedEvents);
        if (expectedEvents == 1)
        {
            events[0].KeycloakId.ShouldBe(Sub);
            events[0].NewRole.ShouldBe("Parent");
        }
    }

    [Fact]
    public async Task Parent_keycloak_failure_writes_nothing()
    {
        var keycloak = Substitute.For<IKeycloakService>();
        keycloak.SetRoleAsync(Arg.Any<string>(), Arg.Any<UserRole>()).Returns(_ => throw new HttpRequestException("kc down"));

        await using (var ctx = _db.NewContext())
            await Should.ThrowAsync<HttpRequestException>(() => NewParentController(ctx, null, keycloak).RegisterParent());

        await using var check = _db.NewContext();
        (await check.Parents.CountAsync()).ShouldBe(0);
        (await RoleEventsAsync()).ShouldBeEmpty();
    }
}
