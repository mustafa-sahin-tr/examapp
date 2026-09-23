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
/// Issue #156: reset-password uçları — sonuç → HTTP eşlemesi, çağıranın sub'ının servise geçmesi, Admin rolü,
/// no-store ve rate limit attribute'ları. Uçtan uca 403/401/429 davranışı: AdminPasswordResetEndpointsTests.
/// </summary>
public class AdminControllerPasswordResetTests
{
    private readonly IAdminPasswordResetService _reset = Substitute.For<IAdminPasswordResetService>();

    private AdminController NewController(string? sub = "kc-admin-sub") => new(
        Substitute.For<ITaxonomyService>(), Substitute.For<IClassifierCacheService>(), Substitute.For<ISchoolService>(),
        Substitute.For<IDashboardService>(), Substitute.For<ILocationService>(), Substitute.For<ITeacherApprovalService>(),
        Substitute.For<IAdminTeacherService>(), Substitute.For<IAdminStudentService>(), Substitute.For<IAdminDataAccessAuditService>(), _reset, Substitute.For<IAdminAccountStatusService>())
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

    private void Returns(AdminPasswordResetStatus status, string? issued = null) =>
        _reset.ResetAsync(Arg.Any<AdminUserTargetType>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AdminPasswordResetResult(status, issued));

    private static string? MessageOf(object? value) => value?.GetType().GetProperty("message")?.GetValue(value) as string;

    [Fact]
    public async Task Success_returns_200_with_only_temporaryPassword_and_passes_actor_sub()
    {
        Returns(AdminPasswordResetStatus.Success, "Tmp-Value-123456");

        var result = await NewController().ResetTeacherPassword(7, default);

        var dto = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AdminPasswordResetResponseDto>();
        dto.TemporaryPassword.ShouldBe("Tmp-Value-123456");
        dto.ToString().ShouldNotContain("Tmp-Value-123456");
        await _reset.Received(1).ResetAsync(AdminUserTargetType.Teacher, 7, "kc-admin-sub", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Student_endpoint_passes_student_target_type()
    {
        Returns(AdminPasswordResetStatus.Success, "x");

        await NewController().ResetStudentPassword(9, default);

        await _reset.Received(1).ResetAsync(AdminUserTargetType.Student, 9, "kc-admin-sub", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(AdminPasswordResetStatus.ForbiddenSelf, 403, "admin.passwordReset.self")]
    [InlineData(AdminPasswordResetStatus.ForbiddenProtectedRole, 403, "admin.passwordReset.protectedRole")]
    [InlineData(AdminPasswordResetStatus.AccountNotFound, 404, "admin.passwordReset.accountNotFound")]
    [InlineData(AdminPasswordResetStatus.TargetNotFound, 404, "admin.passwordReset.teacherNotFound")]
    [InlineData(AdminPasswordResetStatus.UpstreamFailure, 502, "admin.passwordReset.upstreamFailed")]
    [InlineData(AdminPasswordResetStatus.SessionRevokeFailed, 502, "admin.passwordReset.sessionRevokeFailed")]
    public async Task Failure_statuses_map_to_http_code_and_message(AdminPasswordResetStatus status, int code, string key)
    {
        Returns(status);

        var result = await NewController().ResetTeacherPassword(7, default);

        var objectResult = result.ShouldBeAssignableTo<ObjectResult>()!;
        objectResult.StatusCode.ShouldBe(code);
        MessageOf(objectResult.Value).ShouldBe(FallbackMessageLocalizerValue(key));
        objectResult.Value!.GetType().GetProperty("temporaryPassword").ShouldBeNull();
    }

    [Fact]
    public async Task Student_not_found_uses_student_message()
    {
        Returns(AdminPasswordResetStatus.TargetNotFound);

        var result = await NewController().ResetStudentPassword(7, default);

        MessageOf(result.ShouldBeOfType<NotFoundObjectResult>().Value).ShouldBe(FallbackMessageLocalizerValue("admin.passwordReset.studentNotFound"));
    }

    [Fact]
    public async Task Missing_sub_is_forbidden_and_service_not_called()
    {
        var result = await NewController(sub: null).ResetTeacherPassword(7, default);

        result.ShouldBeOfType<ForbidResult>();
        await _reset.DidNotReceiveWithAnyArgs().ResetAsync(default, default, default!, default);
    }

    [Theory]
    [InlineData(nameof(AdminController.ResetTeacherPassword), "teachers/{id:int}/reset-password")]
    [InlineData(nameof(AdminController.ResetStudentPassword), "students/{id:int}/reset-password")]
    public void Endpoints_are_admin_only_post_no_store_and_rate_limited(string methodName, string template)
    {
        typeof(AdminController).GetCustomAttributes<AuthorizeAttribute>(inherit: false).ShouldContain(a => a.Roles == "Admin");

        var method = typeof(AdminController).GetMethod(methodName)!;
        method.GetCustomAttribute<HttpPostAttribute>()!.Template.ShouldBe(template);
        method.GetCustomAttribute<AllowAnonymousAttribute>().ShouldBeNull();

        var cache = method.GetCustomAttribute<ResponseCacheAttribute>()!;
        cache.NoStore.ShouldBeTrue();
        cache.Location.ShouldBe(ResponseCacheLocation.None);

        method.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName.ShouldBe(AdminPasswordResetRateLimiting.Policy);
    }

    [Fact]
    public void Default_rate_limit_is_10_per_minute()
    {
        var options = new AdminPasswordResetRateLimitOptions();
        options.PermitLimit.ShouldBe(10);
        options.WindowSeconds.ShouldBe(60);
    }

    // Controller localizer'sız kurulduğunda FallbackMessageLocalizer kullanır; beklenen metni oradan al.
    private static string FallbackMessageLocalizerValue(string key)
        => ExamApp.Foundation.Localization.FallbackMessageLocalizer.Instance[key].Value;
}
