using System.Reflection;
using System.Security.Claims;
using ExamApp.Api.Controllers;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Services.Classifier;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.Locations;
using ExamApp.Api.Services.Schools;
using ExamApp.Api.Services.Taxonomy;
using ExamApp.Api.Services.TeacherApprovals;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.Tests.Controllers;

/// <summary>
/// issue #289: <c>POST api/admin/teachers/{id}/suspend</c> ve <c>/unsuspend</c> — sonuç → HTTP + errorCode eşlemesi,
/// aktör sub'ının ve nedenin servise geçmesi, Admin rolü, POST şablonları, no-store ve rate limit kovası.
/// </summary>
public class AdminControllerTeacherSuspensionTests
{
    private readonly IAdminTeacherSuspensionService _service = Substitute.For<IAdminTeacherSuspensionService>();

    private AdminController NewController(string? sub = "kc-admin-sub") => new(
        Substitute.For<ITaxonomyService>(), Substitute.For<IClassifierCacheService>(), Substitute.For<ISchoolService>(),
        Substitute.For<IDashboardService>(), Substitute.For<ILocationService>(), Substitute.For<ITeacherApprovalService>(),
        Substitute.For<IAdminTeacherService>(), Substitute.For<IAdminStudentService>(), Substitute.For<IAdminDataAccessAuditService>(),
        Substitute.For<IAdminPasswordResetService>(), Substitute.For<IAdminAccountStatusService>(), teacherSuspension: _service)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    sub is null ? [] : [new Claim(ClaimTypes.NameIdentifier, sub)], "Test")),
                // issue #298: aksiyon admin'in exam user id'sini (otomatik reddedilen randevuların UpdateUserId'si) çözer.
                RequestServices = Services()
            }
        }
    };

    private const int AdminUserId = 7;

    private static IServiceProvider Services()
    {
        var profiles = Substitute.For<IUserProfileProvider>();
        profiles.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new UserProfileDto { Id = AdminUserId, KeycloakId = "kc-admin-sub", Role = "Admin" });
        return new Microsoft.Extensions.DependencyInjection.ServiceCollection()
            .AddSingleton<Microsoft.Extensions.Configuration.IConfiguration>(
                new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build())
            .AddSingleton(profiles)
            .BuildServiceProvider();
    }

    private static string? Prop(object? value, string name) => value?.GetType().GetProperty(name)?.GetValue(value) as string;

    private static string Localized(string key) => ExamApp.Foundation.Localization.FallbackMessageLocalizer.Instance[key].Value;

    [Fact]
    public async Task Suspend_success_returns_200_and_passes_reason_and_actor()
    {
        var at = DateTime.UtcNow;
        _service.SuspendAsync(9, "neden", "kc-admin-sub", AdminUserId, Arg.Any<CancellationToken>())
            .Returns(new AdminTeacherSuspensionResult(AdminTeacherSuspensionStatus.Success, null, at));

        var result = await NewController().SuspendTeacher(9, new AdminTeacherSuspendRequestDto { Reason = "neden" }, default);

        var dto = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AdminTeacherSuspensionResponseDto>();
        dto.TeacherId.ShouldBe(9);
        dto.AccountSuspended.ShouldBeTrue();
        dto.AccountApproved.ShouldBeFalse();
        dto.AccountSuspendedAt.ShouldBe(at);
        dto.AccountApprovedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Unsuspend_success_returns_200_with_new_approval_time()
    {
        var at = DateTime.UtcNow;
        _service.UnsuspendAsync(9, "kc-admin-sub", Arg.Any<CancellationToken>())
            .Returns(new AdminTeacherSuspensionResult(AdminTeacherSuspensionStatus.Success, at, null));

        var result = await NewController().UnsuspendTeacher(9, default);

        var dto = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AdminTeacherSuspensionResponseDto>();
        dto.AccountApproved.ShouldBeTrue();
        dto.AccountSuspended.ShouldBeFalse();
        dto.AccountApprovedAt.ShouldBe(at);
        dto.AccountSuspendedAt.ShouldBeNull();
    }

    [Theory]
    [InlineData(AdminTeacherSuspensionStatus.ReasonRequired, 400, "admin.teacherSuspension.reasonRequired", "SuspensionReasonRequired")]
    [InlineData(AdminTeacherSuspensionStatus.TargetNotFound, 404, "admin.teacherSuspension.teacherNotFound", "TeacherNotFound")]
    [InlineData(AdminTeacherSuspensionStatus.AccountNotApproved, 409, "admin.teacherSuspension.accountNotApproved", "TeacherAccountNotApproved")]
    [InlineData(AdminTeacherSuspensionStatus.AlreadySuspended, 409, "admin.teacherSuspension.alreadySuspended", "TeacherAlreadySuspended")]
    [InlineData(AdminTeacherSuspensionStatus.NotSuspended, 409, "admin.teacherSuspension.notSuspended", "TeacherNotSuspended")]
    [InlineData(AdminTeacherSuspensionStatus.Conflict, 409, "admin.teacherSuspension.concurrentChange", "ConcurrentChange")]
    public async Task Failure_statuses_map_to_http_code_localized_message_and_errorCode(
        AdminTeacherSuspensionStatus status, int code, string key, string errorCode)
    {
        _service.SuspendAsync(Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new AdminTeacherSuspensionResult(status));

        var result = await NewController().SuspendTeacher(9, new AdminTeacherSuspendRequestDto { Reason = "x" }, default);

        var objectResult = result.ShouldBeAssignableTo<ObjectResult>()!;
        objectResult.StatusCode.ShouldBe(code);
        var message = Prop(objectResult.Value, "message");
        message.ShouldBe(Localized(key));
        message.ShouldNotBe(key);
        Prop(objectResult.Value, "errorCode").ShouldBe(errorCode);
    }

    [Fact]
    public async Task Too_long_reason_is_400_with_limit_in_message()
    {
        _service.SuspendAsync(Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new AdminTeacherSuspensionResult(AdminTeacherSuspensionStatus.ReasonTooLong));

        var result = await NewController().SuspendTeacher(9, new AdminTeacherSuspendRequestDto { Reason = "x" }, default);

        var bad = result.ShouldBeOfType<BadRequestObjectResult>();
        Prop(bad.Value, "errorCode").ShouldBe("SuspensionReasonTooLong");
        Prop(bad.Value, "message")!.ShouldContain("500");
    }

    [Fact]
    public async Task Missing_body_passes_null_reason_to_service()
    {
        _service.SuspendAsync(9, null, "kc-admin-sub", Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new AdminTeacherSuspensionResult(AdminTeacherSuspensionStatus.ReasonRequired));

        var result = await NewController().SuspendTeacher(9, null, default);

        result.ShouldBeOfType<BadRequestObjectResult>();
    }

    [Fact]
    public async Task Missing_sub_is_forbidden_and_service_not_called()
    {
        (await NewController(sub: null).SuspendTeacher(9, new AdminTeacherSuspendRequestDto { Reason = "x" }, default))
            .ShouldBeOfType<ForbidResult>();
        (await NewController(sub: null).UnsuspendTeacher(9, default)).ShouldBeOfType<ForbidResult>();

        await _service.DidNotReceiveWithAnyArgs().SuspendAsync(default, default, default!, default, default);
        await _service.DidNotReceiveWithAnyArgs().UnsuspendAsync(default, default!, default);
    }

    [Theory]
    [InlineData(nameof(AdminController.SuspendTeacher), "teachers/{id:int}/suspend")]
    [InlineData(nameof(AdminController.UnsuspendTeacher), "teachers/{id:int}/unsuspend")]
    public void Endpoints_are_admin_only_post_no_store_and_rate_limited(string methodName, string template)
    {
        typeof(AdminController).GetCustomAttributes<AuthorizeAttribute>(inherit: false).ShouldContain(a => a.Roles == "Admin");

        var method = typeof(AdminController).GetMethod(methodName)!;
        method.GetCustomAttribute<HttpPostAttribute>()!.Template.ShouldBe(template);
        method.GetCustomAttribute<AllowAnonymousAttribute>().ShouldBeNull();
        method.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore.ShouldBeTrue();
        method.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName.ShouldBe(AdminAccountStatusRateLimiting.Policy);
    }
}
