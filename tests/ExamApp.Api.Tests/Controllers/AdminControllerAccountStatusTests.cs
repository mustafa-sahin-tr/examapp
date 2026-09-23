using System.Reflection;
using System.Security.Claims;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
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

namespace ExamApp.Api.Tests.Controllers;

/// <summary>
/// Issue #155: account-status uçları — sonuç → HTTP eşlemesi, çağıranın sub'ının ve istenen durumun servise geçmesi,
/// Admin rolü, PATCH şablonu, no-store ve rate limit attribute'ları. Uçtan uca 403/401/429: AdminAccountStatusEndpointsTests.
/// </summary>
public class AdminControllerAccountStatusTests
{
    private readonly IAdminAccountStatusService _status = Substitute.For<IAdminAccountStatusService>();

    private AdminController NewController(string? sub = "kc-admin-sub") => new(
        Substitute.For<ITaxonomyService>(), Substitute.For<IClassifierCacheService>(), Substitute.For<ISchoolService>(),
        Substitute.For<IDashboardService>(), Substitute.For<ILocationService>(), Substitute.For<ITeacherApprovalService>(),
        Substitute.For<IAdminTeacherService>(), Substitute.For<IAdminStudentService>(), Substitute.For<IAdminDataAccessAuditService>(),
        Substitute.For<IAdminPasswordResetService>(), _status)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(
                    sub is null ? [] : [new Claim(ClaimTypes.NameIdentifier, sub)], "Test"))
            }
        }
    };

    private static AdminAccountStatusRequestDto Body(bool? enabled) => new() { Enabled = enabled };

    private void Returns(AdminAccountStatusChangeStatus status, bool? enabled = null) =>
        _status.SetEnabledAsync(Arg.Any<AdminUserTargetType>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AdminAccountStatusChangeResult(status, enabled));

    private static string? MessageOf(object? value) => value?.GetType().GetProperty("message")?.GetValue(value) as string;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Success_returns_200_with_enabled_and_passes_actor_and_desired_state(bool enabled)
    {
        Returns(AdminAccountStatusChangeStatus.Success, enabled);

        var result = await NewController().SetTeacherAccountStatus(7, Body(enabled), default);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AdminAccountStatusResponseDto>().Enabled.ShouldBe(enabled);
        await _status.Received(1).SetEnabledAsync(AdminUserTargetType.Teacher, 7, enabled, "kc-admin-sub", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Student_endpoint_passes_student_target_type()
    {
        Returns(AdminAccountStatusChangeStatus.Success, false);

        await NewController().SetStudentAccountStatus(9, Body(false), default);

        await _status.Received(1).SetEnabledAsync(AdminUserTargetType.Student, 9, false, "kc-admin-sub", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(AdminAccountStatusChangeStatus.ForbiddenSelf, 403, "admin.accountStatus.self")]
    [InlineData(AdminAccountStatusChangeStatus.ForbiddenProtectedRole, 403, "admin.accountStatus.protectedRole")]
    [InlineData(AdminAccountStatusChangeStatus.AccountNotFound, 404, "admin.accountStatus.accountNotFound")]
    [InlineData(AdminAccountStatusChangeStatus.TargetNotFound, 404, "admin.accountStatus.teacherNotFound")]
    [InlineData(AdminAccountStatusChangeStatus.UpstreamFailure, 502, "admin.accountStatus.upstreamFailed")]
    [InlineData(AdminAccountStatusChangeStatus.SessionRevokeFailed, 502, "admin.accountStatus.sessionRevokeFailed")]
    public async Task Failure_statuses_map_to_http_code_and_message(AdminAccountStatusChangeStatus status, int code, string key)
    {
        Returns(status);

        var result = await NewController().SetTeacherAccountStatus(7, Body(false), default);

        var objectResult = result.ShouldBeAssignableTo<ObjectResult>()!;
        objectResult.StatusCode.ShouldBe(code);
        var message = MessageOf(objectResult.Value);
        message.ShouldBe(FallbackMessageLocalizerValue(key));
        message.ShouldNotBe(key); // anahtar mesaj sözlüğünde tanımlı
    }

    [Fact]
    public async Task Student_not_found_uses_student_message()
    {
        Returns(AdminAccountStatusChangeStatus.TargetNotFound);

        var result = await NewController().SetStudentAccountStatus(7, Body(true), default);

        MessageOf(result.ShouldBeOfType<NotFoundObjectResult>().Value)
            .ShouldBe(FallbackMessageLocalizerValue("admin.accountStatus.studentNotFound"));
    }

    [Fact]
    public async Task Missing_enabled_is_400_and_service_not_called()
    {
        var result = await NewController().SetTeacherAccountStatus(7, Body(null), default);

        result.ShouldBeOfType<BadRequestObjectResult>();
        await _status.DidNotReceiveWithAnyArgs().SetEnabledAsync(default, default, default, default!, default);
    }

    [Fact]
    public async Task Missing_sub_is_forbidden_and_service_not_called()
    {
        var result = await NewController(sub: null).SetTeacherAccountStatus(7, Body(false), default);

        result.ShouldBeOfType<ForbidResult>();
        await _status.DidNotReceiveWithAnyArgs().SetEnabledAsync(default, default, default, default!, default);
    }

    [Theory]
    [InlineData(nameof(AdminController.SetTeacherAccountStatus), "teachers/{id:int}/account-status")]
    [InlineData(nameof(AdminController.SetStudentAccountStatus), "students/{id:int}/account-status")]
    public void Endpoints_are_admin_only_patch_no_store_and_rate_limited(string methodName, string template)
    {
        typeof(AdminController).GetCustomAttributes<AuthorizeAttribute>(inherit: false).ShouldContain(a => a.Roles == "Admin");

        var method = typeof(AdminController).GetMethod(methodName)!;
        method.GetCustomAttribute<HttpPatchAttribute>()!.Template.ShouldBe(template);
        method.GetCustomAttribute<AllowAnonymousAttribute>().ShouldBeNull();

        var cache = method.GetCustomAttribute<ResponseCacheAttribute>()!;
        cache.NoStore.ShouldBeTrue();

        method.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName.ShouldBe(AdminAccountStatusRateLimiting.Policy);
        AdminAccountStatusRateLimiting.Policy.ShouldNotBe(AdminPasswordResetRateLimiting.Policy);
    }

    [Fact]
    public void Default_rate_limit_is_20_per_minute()
    {
        var options = new AdminAccountStatusRateLimitOptions();
        options.PermitLimit.ShouldBe(20);
        options.WindowSeconds.ShouldBe(60);
    }

    private static string FallbackMessageLocalizerValue(string key)
        => ExamApp.Foundation.Localization.FallbackMessageLocalizer.Instance[key].Value;
}
