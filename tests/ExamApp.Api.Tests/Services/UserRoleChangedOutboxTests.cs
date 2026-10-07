using System.Security.Claims;
using System.Text.Json;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Services.UserRoles;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #277 (madde 4) + review: <see cref="UserRoleChangedEvent"/> yalnızca Keycloak rolü BAŞARIYLA atandıktan sonra ve rol
/// gerçekten değiştiyse yazılır (<see cref="UserRoleChangeRecorder"/>; veli akışında <see cref="ParentService"/> satırla tek
/// SaveChanges'te). Tek SaveChanges retry-safe.
/// </summary>
public class UserRoleChangedOutboxTests : IDisposable
{
    private const string Sub = "kc-sub-1";

    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private static string RoleEventType => OutboxEventRegistry.NameFor<UserRoleChangedEvent>();

    private async Task<List<UserRoleChangedEvent>> RoleEventsAsync()
    {
        await using var ctx = _db.NewContext();
        return (await ctx.OutboxMessages.AsNoTracking().Where(m => m.Type == RoleEventType).ToListAsync())
            .Select(m => JsonSerializer.Deserialize<UserRoleChangedEvent>(m.Content)!)
            .ToList();
    }

    // ---- IsChange ----

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

    // ---- Recorder (Teacher/Student akışları) ----

    [Theory]
    [InlineData(UserRole.Teacher)]
    [InlineData(UserRole.Student)]
    public async Task Recorder_writes_exactly_one_event_on_a_role_change(UserRole role)
    {
        await using (var ctx = _db.NewContext())
            (await new UserRoleChangeRecorder(ctx).RecordIfChangedAsync(new UserRoleChangeRequest(Sub, 10, null), role)).ShouldBeTrue();

        var e = (await RoleEventsAsync()).ShouldHaveSingleItem();
        e.KeycloakId.ShouldBe(Sub);
        e.UserId.ShouldBe(10);
        e.NewRole.ShouldBe(role.ToString());
        e.EventId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Recorder_writes_nothing_when_the_role_is_unchanged()
    {
        await using (var ctx = _db.NewContext())
            (await new UserRoleChangeRecorder(ctx).RecordIfChangedAsync(new UserRoleChangeRequest(Sub, 11, "Teacher"), UserRole.Teacher))
                .ShouldBeFalse();

        (await RoleEventsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Recorder_is_retry_safe_on_a_transient_failure()
    {
        // Tek ifadelik SaveChanges'i EF açık transaction'sız çalıştırır → COMMIT yerine INSERT'in kendisi geçici hatayla düşer.
        var interceptor = new FailFirstCommandInterceptor("INSERT INTO \"OutboxMessages\"");
        await using (var ctx = _db.NewContextWithTransientRetry(interceptor))
            await new UserRoleChangeRecorder(ctx).RecordIfChangedAsync(new UserRoleChangeRequest(Sub, 12, "Student"), UserRole.Teacher);

        interceptor.Failures.ShouldBe(1);
        (await RoleEventsAsync()).ShouldHaveSingleItem().NewRole.ShouldBe("Teacher");
    }

    [Fact]
    public async Task Recorder_works_under_the_retrying_strategy_guard()
    {
        await using (var ctx = _db.NewContextWithRetryingExecutionStrategy())
            await new UserRoleChangeRecorder(ctx).RecordIfChangedAsync(new UserRoleChangeRequest(Sub, 13, null), UserRole.Student);

        (await RoleEventsAsync()).Count.ShouldBe(1);
    }

    // ---- ParentService ----

    [Theory]
    [InlineData(null, 1)]
    [InlineData("Parent", 0)]
    public async Task Parent_registration_writes_the_row_and_an_event_only_on_role_change(string? currentRole, int expectedEvents)
    {
        int parentId;
        await using (var ctx = _db.NewContext())
            parentId = await new ParentService(ctx).RegisterAsync(30, new UserRoleChangeRequest(Sub, 30, currentRole));

        await using var check = _db.NewContext();
        (await check.Parents.SingleAsync(p => p.UserId == 30)).Id.ShouldBe(parentId);
        var events = await RoleEventsAsync();
        events.Count.ShouldBe(expectedEvents);
        if (expectedEvents == 1)
            events[0].NewRole.ShouldBe("Parent");
    }

    [Fact]
    public async Task Parent_re_registration_is_idempotent_for_the_row()
    {
        await using (var ctx = _db.NewContext())
            await new ParentService(ctx).RegisterAsync(31, new UserRoleChangeRequest(Sub, 31, null));
        await using (var ctx = _db.NewContext())
        {
            await new ParentService(ctx).RegisterAsync(31, new UserRoleChangeRequest(Sub, 31, "Parent"));
            (await new ParentService(ctx).HasParentRecordAsync(31)).ShouldBeTrue();
            (await new ParentService(ctx).HasParentRecordAsync(999)).ShouldBeFalse();
        }

        await using var check = _db.NewContext();
        (await check.Parents.CountAsync(p => p.UserId == 31)).ShouldBe(1);
        (await RoleEventsAsync()).Count.ShouldBe(1);
    }

    [Theory]
    [InlineData(UserRole.Parent, 33, true)]   // öğrenci satırı → veli olamaz
    [InlineData(UserRole.Parent, 34, true)]   // öğretmen satırı → veli olamaz
    [InlineData(UserRole.Parent, 36, false)]
    [InlineData(UserRole.Student, 35, true)]  // veli satırı → öğrenci olamaz
    [InlineData(UserRole.Teacher, 35, true)]  // veli satırı → öğretmen olamaz
    [InlineData(UserRole.Student, 34, false)] // öğrenci ↔ öğretmen dışlaması burada değil (kayıt servisleri)
    public async Task Role_exclusivity_checks_rows_issue_419(UserRole target, int userId, bool expected)
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.Students.Add(new Student { UserId = 33, StudentNumber = "s33" });
            ctx.Teachers.Add(new Teacher { UserId = 34 });
            ctx.Parents.Add(new Parent { UserId = 35 });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        (await new UserRoleExclusivity(check).ConflictsAsync(userId, target, [])).ShouldBe(expected);
    }

    [Theory]
    [InlineData(UserRole.Parent, "Student", true)]
    [InlineData(UserRole.Parent, "teacher", true)]
    [InlineData(UserRole.Parent, "Parent", false)]
    [InlineData(UserRole.Student, "Parent", true)]
    [InlineData(UserRole.Teacher, "Parent", true)]
    [InlineData(UserRole.Teacher, "Student", false)]
    public async Task Role_exclusivity_checks_claimed_roles_issue_419(UserRole target, string claimed, bool expected)
    {
        await using var check = _db.NewContext();
        (await new UserRoleExclusivity(check).ConflictsAsync(99, target, [claimed])).ShouldBe(expected);
    }

    [Fact]
    public async Task Parent_row_and_event_are_retry_safe_on_a_transient_commit_failure()
    {
        var interceptor = new FailFirstCommitInterceptor();
        await using (var ctx = _db.NewContextWithTransientRetry(interceptor))
            await new ParentService(ctx).RegisterAsync(32, new UserRoleChangeRequest(Sub, 32, null));

        interceptor.Failures.ShouldBe(1);
        await using var check = _db.NewContext();
        (await check.Parents.CountAsync(p => p.UserId == 32)).ShouldBe(1);
        (await RoleEventsAsync()).Count.ShouldBe(1);
    }

    // ---- Controller sırası: event yalnızca SetRoleAsync BAŞARILI olduktan sonra ----

    private static DefaultHttpContext Http(IServiceProvider services, params string[] jwtRoles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, Sub) };
        claims.AddRange(jwtRoles.Select(r => new Claim(ClaimTypes.Role, r)));
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test")),
            RequestServices = services
        };
        http.Request.Headers.Cookie = "refresh_token=rt";
        return http;
    }

    private ServiceProvider Services(IUserRoleChangeRecorder recorder, string? profileRole)
    {
        var profiles = Substitute.For<IUserProfileProvider>();
        profiles.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => new UserProfileDto { Id = 40, KeycloakId = Sub, Role = profileRole! });
        var resolver = Substitute.For<ISchoolContextResolver>();
        return new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddSingleton(profiles)
            .AddSingleton(resolver)
            .AddSingleton(recorder)
            .AddSingleton<IDistributedCache>(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())))
            .AddSingleton<UserProfileCacheService>()
            .AddScoped(_ => _db.NewContext())
            .AddScoped<IUserRoleExclusivity, UserRoleExclusivity>() // issue #419: gerçek kural, SQLite üzerinde
            .BuildServiceProvider();
    }

    [Fact]
    public async Task Teacher_controller_records_the_role_change_after_a_successful_keycloak_role_assignment()
    {
        var recorder = Substitute.For<IUserRoleChangeRecorder>();
        var keycloak = Substitute.For<IKeycloakService>();
        keycloak.RefreshTokenAsync(Arg.Any<string>()).Returns(new TokenResponseDto { AccessToken = "at", RefreshToken = "" });
        var teacherService = Substitute.For<ITeacherService>();
        teacherService.Save(40, Arg.Any<RegisterTeacherDto>()).Returns(new TeacherRegistrationResultDto { Success = true });
        var services = Services(recorder, profileRole: "Student");
        var controller = new TeacherController(teacherService, services.GetRequiredService<UserProfileCacheService>(), keycloak,
            Substitute.For<ILogger<TeacherController>>())
        { ControllerContext = new ControllerContext { HttpContext = Http(services, "Student") } };

        (await controller.RegisterTeacher(new RegisterTeacherDto())).ShouldBeOfType<OkObjectResult>();

        Received.InOrder(() =>
        {
            keycloak.SetRoleAsync(Sub, UserRole.Teacher);
            recorder.RecordIfChangedAsync(
                Arg.Is<UserRoleChangeRequest>(r => r.KeycloakId == Sub && r.UserId == 40 && r.CurrentRole == "Student"),
                UserRole.Teacher, Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task Teacher_controller_records_nothing_when_keycloak_role_assignment_fails()
    {
        var recorder = Substitute.For<IUserRoleChangeRecorder>();
        var keycloak = Substitute.For<IKeycloakService>();
        keycloak.SetRoleAsync(Arg.Any<string>(), Arg.Any<UserRole>()).Returns(_ => throw new HttpRequestException("kc down"));
        var teacherService = Substitute.For<ITeacherService>();
        teacherService.Save(40, Arg.Any<RegisterTeacherDto>()).Returns(new TeacherRegistrationResultDto { Success = true });
        var services = Services(recorder, profileRole: null);
        var controller = new TeacherController(teacherService, services.GetRequiredService<UserProfileCacheService>(), keycloak,
            Substitute.For<ILogger<TeacherController>>())
        { ControllerContext = new ControllerContext { HttpContext = Http(services) } };

        await Should.ThrowAsync<HttpRequestException>(() => controller.RegisterTeacher(new RegisterTeacherDto()));

        await recorder.DidNotReceiveWithAnyArgs().RecordIfChangedAsync(default!, default, default);
    }

    [Fact]
    public async Task Parent_controller_writes_nothing_when_keycloak_role_assignment_fails()
    {
        var parentService = Substitute.For<IParentService>();
        var keycloak = Substitute.For<IKeycloakService>();
        keycloak.SetRoleAsync(Arg.Any<string>(), Arg.Any<UserRole>()).Returns(_ => throw new HttpRequestException("kc down"));
        var services = Services(Substitute.For<IUserRoleChangeRecorder>(), profileRole: null);
        var controller = new ParentController(parentService, keycloak)
        { ControllerContext = new ControllerContext { HttpContext = Http(services) } };

        await Should.ThrowAsync<HttpRequestException>(() => controller.RegisterParent());

        await parentService.DidNotReceiveWithAnyArgs().RegisterAsync(default, default!, default);
    }

    [Fact]
    public async Task Parent_controller_registers_via_the_service_after_keycloak_and_returns_the_parent_id()
    {
        var parentService = Substitute.For<IParentService>();
        parentService.RegisterAsync(40, Arg.Any<UserRoleChangeRequest>(), Arg.Any<CancellationToken>()).Returns(77);
        var keycloak = Substitute.For<IKeycloakService>();
        keycloak.RefreshTokenAsync(Arg.Any<string>()).Returns(new TokenResponseDto { AccessToken = "at", RefreshToken = "" });
        var services = Services(Substitute.For<IUserRoleChangeRecorder>(), profileRole: null);
        var controller = new ParentController(parentService, keycloak)
        { ControllerContext = new ControllerContext { HttpContext = Http(services) } };

        var ok = (await controller.RegisterParent()).ShouldBeOfType<OkObjectResult>();

        ok.Value!.GetType().GetProperty("profileId")!.GetValue(ok.Value).ShouldBe(77);
        Received.InOrder(() =>
        {
            keycloak.SetRoleAsync(Sub, UserRole.Parent);
            parentService.RegisterAsync(40, Arg.Is<UserRoleChangeRequest>(r => r.KeycloakId == Sub), Arg.Any<CancellationToken>());
        });
    }

    // issue #419 review (D4): öğrenci/öğretmen hesabı kendini veliye çeviremez — Keycloak rolü atanmadan 409.
    [Theory]
    [InlineData("Student", null, false)]
    [InlineData("Teacher", null, false)]
    [InlineData(null, "Student", false)] // yalnız auth-api profil rolü (JWT henüz güncellenmemiş)
    [InlineData(null, null, true)]
    public async Task Parent_register_rejects_existing_student_or_teacher_before_touching_keycloak(
        string? jwtRole, string? profileRole, bool hasRow)
    {
        if (hasRow)
            await SeedRowAsync(new Student { UserId = 40, StudentNumber = "s40" });
        var parentService = Substitute.For<IParentService>();
        var keycloak = Substitute.For<IKeycloakService>();
        var services = Services(Substitute.For<IUserRoleChangeRecorder>(), profileRole);
        var controller = new ParentController(parentService, keycloak)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = jwtRole == null ? Http(services) : Http(services, jwtRole)
            }
        };

        var conflict = (await controller.RegisterParent()).ShouldBeOfType<ConflictObjectResult>();

        conflict.Value!.GetType().GetProperty("message")!.GetValue(conflict.Value)
            .ShouldBe(FallbackMessageLocalizer.Instance["parent.otherRoleExists"].Value);
        await keycloak.DidNotReceiveWithAnyArgs().SetRoleAsync(default!, default);
        await parentService.DidNotReceiveWithAnyArgs().RegisterAsync(default, default!, default);
    }

    // issue #419 re-review: veli hesabı kendini öğrenciye/öğretmene çeviremez — Keycloak rolü atanmadan 409.
    [Theory]
    [InlineData("Parent", null, false)]
    [InlineData(null, "Parent", false)]
    [InlineData(null, null, true)]
    public async Task Teacher_register_rejects_parent_accounts_before_touching_keycloak(string? jwtRole, string? profileRole, bool hasRow)
    {
        if (hasRow)
            await SeedRowAsync(new Parent { UserId = 40 });
        var keycloak = Substitute.For<IKeycloakService>();
        var teacherService = Substitute.For<ITeacherService>();
        var services = Services(Substitute.For<IUserRoleChangeRecorder>(), profileRole);
        var controller = new TeacherController(teacherService, services.GetRequiredService<UserProfileCacheService>(), keycloak,
            Substitute.For<ILogger<TeacherController>>())
        { ControllerContext = new ControllerContext { HttpContext = jwtRole == null ? Http(services) : Http(services, jwtRole) } };

        var conflict = (await controller.RegisterTeacher(new RegisterTeacherDto())).ShouldBeOfType<ConflictObjectResult>();

        conflict.Value!.GetType().GetProperty("message")!.GetValue(conflict.Value)
            .ShouldBe(FallbackMessageLocalizer.Instance["teacher.parentRecordExists"].Value);
        await teacherService.DidNotReceiveWithAnyArgs().Save(default, default!);
        await keycloak.DidNotReceiveWithAnyArgs().SetRoleAsync(default!, default);
    }

    private async Task SeedRowAsync(object entity)
    {
        await using var ctx = _db.NewContext();
        ctx.Add(entity);
        await ctx.SaveChangesAsync();
    }
}
