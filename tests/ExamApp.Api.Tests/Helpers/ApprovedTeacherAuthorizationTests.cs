using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Teachers;
using ExamApp.Api.Services.Teachers.Authorization;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Helpers;

/// <summary>
/// issue #287: <c>ApprovedTeacher</c> / <c>ApprovedTeacherOrStudent</c> policy'leri — minimal pipeline (header kimliği →
/// UseAuthorization) + gerçek guard (SQLite) + gerçek JSON sözlüğü. Onaysız öğretmen 403 + TeacherNotApproved gövdesi
/// alır; onaylı öğretmen geçer; admin/öğrenci karma uçlarda etkilenmez. Ayrıca controller attribute kapsamı.
/// </summary>
public class ApprovedTeacherAuthorizationTests : IDisposable
{
    private const int ApprovedTeacherId = 1;
    private const int PendingTeacherId = 2;
    private const int RejectedTeacherId = 3;
    private const int SwitchedToIndependentId = 4; // hesabı onaylı, tutor başvurusu Pending
    private const int NoTeacherRowId = 5;

    private readonly TestDb _db = TestDb.Create();

    public ApprovedTeacherAuthorizationTests()
    {
        using var ctx = _db.NewContext();
        ctx.Teachers.AddRange(
            new Teacher { UserId = ApprovedTeacherId, ApprovalStatus = TeacherApprovalStatus.Approved, AccountApprovedAt = DateTime.UtcNow },
            new Teacher { UserId = PendingTeacherId, ApprovalStatus = TeacherApprovalStatus.Pending },
            new Teacher { UserId = RejectedTeacherId, ApprovalStatus = TeacherApprovalStatus.Rejected, RejectionReason = "x" },
            new Teacher
            {
                UserId = SwitchedToIndependentId, IsIndependentTutor = true, ApprovalStatus = TeacherApprovalStatus.Pending,
                AccountApprovedAt = DateTime.UtcNow
            });
        ctx.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    // ---------------- Pipeline ----------------

    private sealed class HeaderAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Sub", out var sub))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, sub.ToString()) };
            foreach (var role in Request.Headers["X-Roles"].ToString().Split(',', StringSplitOptions.RemoveEmptyEntries))
                claims.Add(new Claim(ClaimTypes.Role, role.Trim()));
            var identity = new ClaimsIdentity(claims, "Test", ClaimTypes.Name, ClaimTypes.Role);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }

    /// <summary>sub "u{id}" → profil Id; bilinmeyen sub'lar fırlatmaz, Id=0 döner.</summary>
    private sealed class FakeProfiles : IUserProfileProvider
    {
        public Task<UserProfileDto> GetAsync(string keycloakId, CancellationToken ct = default) =>
            Task.FromResult(new UserProfileDto
            {
                Id = int.TryParse(keycloakId.TrimStart('u'), out var id) ? id : 0, KeycloakId = keycloakId
            });
    }

    private async Task<IHost> StartHostAsync()
    {
        return await new HostBuilder()
            .ConfigureWebHost(web =>
            {
                web.UseTestServer();
                web.ConfigureServices(services =>
                {
                    services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
                    services.AddRouting();
                    services.AddLogging();
                    services.AddJsonLocalization(o =>
                    {
                        o.ResourcesPath = "Resources";
                        o.FileProvider = new PhysicalFileProvider(AppContext.BaseDirectory);
                    });
                    services.Configure<RequestLocalizationOptions>(options =>
                    {
                        var cultures = SupportedLocales.AllCultureNames.Select(n => new CultureInfo(n)).ToList();
                        options.DefaultRequestCulture = new RequestCulture(SupportedLocales.DefaultCultureName);
                        options.SupportedCultures = cultures;
                        options.SupportedUICultures = cultures;
                        options.RequestCultureProviders.Clear();
                        options.RequestCultureProviders.Add(new NormalizedAcceptLanguageCultureProvider());
                    });
                    services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, HeaderAuthHandler>("Test", _ => { });
                    services.AddScoped(_ => _db.NewContext());
                    services.AddScoped<IApprovedTeacherGuard, ApprovedTeacherGuard>();
                    services.AddSingleton<IUserProfileProvider, FakeProfiles>();
                    services.AddApprovedTeacherAuthorization();
                });
                web.Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(e =>
                    {
                        e.MapGet("/teacher-only", () => Results.Ok()).RequireAuthorization(
                            new AuthorizeAttribute { Roles = "Teacher" },
                            new AuthorizeAttribute { Policy = ApprovedTeacherPolicies.TeacherCapability });
                        e.MapGet("/teacher-admin", () => Results.Ok()).RequireAuthorization(
                            new AuthorizeAttribute { Roles = "Teacher,Admin" },
                            new AuthorizeAttribute { Policy = ApprovedTeacherPolicies.TeacherCapability });
                        e.MapGet("/teacher-student", () => Results.Ok()).RequireAuthorization(
                            new AuthorizeAttribute { Roles = "Teacher,Student" },
                            new AuthorizeAttribute { Policy = ApprovedTeacherPolicies.TeacherOrStudentCapability });
                        e.MapGet("/any-user", () => Results.Ok()).RequireAuthorization(
                            new AuthorizeAttribute { Policy = ApprovedTeacherPolicies.TeacherCapability });
                    });
                });
            })
            .StartAsync();
    }

    private static Task<HttpResponseMessage> GetAsync(HttpClient client, string path, int userId, string roles, string? language = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-Sub", $"u{userId}");
        request.Headers.Add("X-Roles", roles);
        if (language != null)
            request.Headers.Add("Accept-Language", language);
        return client.SendAsync(request);
    }

    private sealed record NotApprovedBody(bool Success, string ErrorCode, string Message);

    // ---------------- Öğretmen-yalnız uç ----------------

    [Theory]
    [InlineData(PendingTeacherId)]
    [InlineData(RejectedTeacherId)]
    [InlineData(NoTeacherRowId)]
    public async Task Unapproved_teacher_gets_403_with_TeacherNotApproved_body(int userId)
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        var response = await GetAsync(client, "/teacher-only", userId, "Teacher");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadFromJsonAsync<NotApprovedBody>();
        body.ShouldNotBeNull();
        body.Success.ShouldBeFalse();
        body.ErrorCode.ShouldBe("TeacherNotApproved");
        body.Message.ShouldBe("Öğretmen özelliklerini kullanabilmek için öğretmen hesabınızın yönetici tarafından onaylanması gerekir.");
    }

    [Fact]
    public async Task TeacherNotApproved_message_follows_the_request_language()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        var response = await GetAsync(client, "/teacher-only", PendingTeacherId, "Teacher", language: "en-US");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadFromJsonAsync<NotApprovedBody>())!.Message
            .ShouldBe("Your teacher account must be approved by an administrator before you can use teacher features.");
    }

    [Theory]
    [InlineData(ApprovedTeacherId)]
    [InlineData(SwitchedToIndependentId)]
    public async Task Account_approved_teacher_passes(int userId)
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        (await GetAsync(client, "/teacher-only", userId, "Teacher")).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Missing_role_keeps_the_default_bodyless_403()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        var response = await GetAsync(client, "/teacher-only", 50, "Student");

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await response.Content.ReadAsStringAsync()).ShouldBeEmpty();
    }

    // ---------------- Karma uçlar: admin / öğrenci etkilenmez ----------------

    [Theory]
    [InlineData("Admin")]
    [InlineData("Admin,Teacher")] // Admin + (onaysız) Teacher: admin muafiyeti
    public async Task Admin_is_unaffected_on_teacher_admin_endpoints(string roles)
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        (await GetAsync(client, "/teacher-admin", PendingTeacherId, roles)).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(client, "/teacher-admin", NoTeacherRowId, roles)).StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Pending_teacher_is_denied_on_teacher_admin_endpoints()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        (await GetAsync(client, "/teacher-admin", PendingTeacherId, "Teacher")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Student_is_unaffected_and_pending_teacher_is_denied_on_teacher_student_endpoints()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        (await GetAsync(client, "/teacher-student", 60, "Student")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(client, "/teacher-student", ApprovedTeacherId, "Teacher")).StatusCode.ShouldBe(HttpStatusCode.OK);

        var denied = await GetAsync(client, "/teacher-student", PendingTeacherId, "Teacher");
        denied.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await denied.Content.ReadFromJsonAsync<NotApprovedBody>())!.ErrorCode.ShouldBe("TeacherNotApproved");
    }

    [Fact]
    public async Task Generic_endpoint_only_gates_the_teacher_role()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        (await GetAsync(client, "/any-user", 70, "Student")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(client, "/any-user", 71, "")).StatusCode.ShouldBe(HttpStatusCode.OK);
        (await GetAsync(client, "/any-user", PendingTeacherId, "Teacher")).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Anonymous_caller_is_still_challenged()
    {
        using var host = await StartHostAsync();
        using var client = host.GetTestClient();

        (await client.GetAsync("/teacher-only")).StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    // ---------------- Controller attribute kapsamı ----------------

    private static readonly string[] Policies =
        { ApprovedTeacherPolicies.TeacherCapability, ApprovedTeacherPolicies.TeacherOrStudentCapability };

    /// <summary>Onay beklerken de açık kalması GEREKEN öğretmen uçları (kendi başvurusu/profili).</summary>
    private static readonly (Type Controller, string Action)[] ExemptTeacherActions =
    {
        (typeof(TeacherController), nameof(TeacherController.GetTutorProfile)),
        (typeof(TeacherController), nameof(TeacherController.UpdateTutorProfile)),
    };

    private static IEnumerable<AuthorizeAttribute> AuthorizeAttributesOf(MethodInfo action) =>
        action.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Concat(action.DeclaringType!.GetCustomAttributes<AuthorizeAttribute>(inherit: true));

    private static bool IsGated(MethodInfo action) =>
        AuthorizeAttributesOf(action).Any(a => Policies.Contains(a.Policy));

    private static IEnumerable<MethodInfo> ControllerActions() =>
        typeof(BaseController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly))
            .Where(m => m.GetCustomAttributes<Microsoft.AspNetCore.Mvc.Routing.HttpMethodAttribute>().Any());

    [Fact]
    public void Every_teacher_role_endpoint_requires_the_approved_teacher_policy_except_the_own_application_endpoints()
    {
        var teacherRoleActions = ControllerActions()
            .Where(m => AuthorizeAttributesOf(m).Any(a =>
                (a.Roles ?? string.Empty).Split(',').Select(r => r.Trim()).Contains("Teacher")
                || a.Policy == "TeacherOrService"))
            .ToList();

        teacherRoleActions.Count.ShouldBeGreaterThan(40);
        var missing = teacherRoleActions
            .Where(m => !IsGated(m) && !ExemptTeacherActions.Contains((m.DeclaringType!, m.Name)))
            .Select(m => $"{m.DeclaringType!.Name}.{m.Name}")
            .ToList();
        missing.ShouldBeEmpty();
    }

    [Theory]
    [InlineData(typeof(TeacherController), nameof(TeacherController.RegisterTeacher))]
    [InlineData(typeof(TeacherController), nameof(TeacherController.CheckTeacher))]
    [InlineData(typeof(TeacherController), nameof(TeacherController.UpdateTheme))]
    [InlineData(typeof(TeacherController), nameof(TeacherController.GetTutorProfile))]
    [InlineData(typeof(TeacherController), nameof(TeacherController.UpdateTutorProfile))]
    [InlineData(typeof(AuthController), nameof(AuthController.RefreshProfileInformation))]
    [InlineData(typeof(AuthController), nameof(AuthController.Logout))]
    [InlineData(typeof(AuthController), nameof(AuthController.GetCurrentCulture))]
    [InlineData(typeof(ExamController), nameof(ExamController.GetGrades))]
    [InlineData(typeof(BookingController), nameof(BookingController.GetTeacherSlots))]
    public void Pending_teacher_keeps_access_to_own_profile_settings_and_public_data(Type controller, string action)
    {
        IsGated(controller.GetMethod(action)!).ShouldBeFalse();
    }

    [Theory]
    [InlineData(typeof(TeacherController), nameof(TeacherController.GetDashboardSummary), ApprovedTeacherPolicies.TeacherCapability)]
    [InlineData(typeof(ExamController), nameof(ExamController.CreateOrUpdateAsync), ApprovedTeacherPolicies.TeacherCapability)]
    [InlineData(typeof(ExamController), nameof(ExamController.AssignWorksheet), ApprovedTeacherPolicies.TeacherCapability)]
    [InlineData(typeof(ExamController), nameof(ExamController.DeleteWorksheet), ApprovedTeacherPolicies.TeacherCapability)]
    [InlineData(typeof(ExamController), nameof(ExamController.GetWorksheetsAsync), ApprovedTeacherPolicies.TeacherOrStudentCapability)]
    [InlineData(typeof(ExamController), nameof(ExamController.GetMyCalendar), ApprovedTeacherPolicies.TeacherOrStudentCapability)]
    [InlineData(typeof(BookingController), nameof(BookingController.CreateVideoSession), ApprovedTeacherPolicies.TeacherOrStudentCapability)]
    [InlineData(typeof(StudentController), nameof(StudentController.GetStudentLookup), ApprovedTeacherPolicies.TeacherCapability)]
    [InlineData(typeof(QuestionsController), nameof(QuestionsController.CreateOrUpdateQuestion), ApprovedTeacherPolicies.TeacherCapability)]
    [InlineData(typeof(QuestionTransferController), nameof(QuestionTransferController.HangfireLogin), ApprovedTeacherPolicies.TeacherCapability)]
    public void Representative_teacher_endpoints_are_gated(Type controller, string action, string policy)
    {
        AuthorizeAttributesOf(controller.GetMethod(action)!).ShouldContain(a => a.Policy == policy);
    }
}
