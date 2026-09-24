using System.Reflection;
using System.Security.Claims;
using ExamApp.Api.Controllers;
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
/// issue #277 (madde 8): <c>PUT api/admin/students/{id}/school</c> — sonuç → HTTP eşlemesi, çağıranın sub'ının servise
/// geçmesi, Admin rolü, PUT şablonu, no-store ve ayrı rate limit kovası.
/// </summary>
public class AdminControllerStudentSchoolTests
{
    private readonly IAdminStudentSchoolService _service = Substitute.For<IAdminStudentSchoolService>();

    private AdminController NewController(string? sub = "kc-admin-sub") => new(
        Substitute.For<ITaxonomyService>(), Substitute.For<IClassifierCacheService>(), Substitute.For<ISchoolService>(),
        Substitute.For<IDashboardService>(), Substitute.For<ILocationService>(), Substitute.For<ITeacherApprovalService>(),
        Substitute.For<IAdminTeacherService>(), Substitute.For<IAdminStudentService>(), Substitute.For<IAdminDataAccessAuditService>(),
        Substitute.For<IAdminPasswordResetService>(), Substitute.For<IAdminAccountStatusService>(), studentSchool: _service)
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

    private void Returns(AdminStudentSchoolChangeResult result) =>
        _service.ChangeSchoolAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(result);

    private static string? MessageOf(object? value) => value?.GetType().GetProperty("message")?.GetValue(value) as string;

    private static string Localized(string key) => ExamApp.Foundation.Localization.FallbackMessageLocalizer.Instance[key].Value;

    [Fact]
    public async Task Success_returns_200_with_new_and_previous_school_and_passes_actor()
    {
        Returns(new AdminStudentSchoolChangeResult(AdminStudentSchoolChangeStatus.Success, 3, 4, Changed: true));

        var result = await NewController().ChangeStudentSchool(9, new AdminStudentSchoolRequestDto { SchoolId = 4 }, default);

        var dto = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AdminStudentSchoolResponseDto>();
        dto.StudentId.ShouldBe(9);
        dto.SchoolId.ShouldBe(4);
        dto.PreviousSchoolId.ShouldBe(3);
        dto.Changed.ShouldBeTrue();
        await _service.Received(1).ChangeSchoolAsync(9, 4, "kc-admin-sub", Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(AdminStudentSchoolChangeStatus.TargetNotFound, 404, "admin.studentSchool.studentNotFound")]
    [InlineData(AdminStudentSchoolChangeStatus.SchoolNotFound, 400, "admin.studentSchool.schoolNotFound")]
    [InlineData(AdminStudentSchoolChangeStatus.AccountNotFound, 404, "admin.studentSchool.accountNotFound")]
    [InlineData(AdminStudentSchoolChangeStatus.ForbiddenSelf, 403, "admin.studentSchool.self")]
    [InlineData(AdminStudentSchoolChangeStatus.ForbiddenProtectedRole, 403, "admin.studentSchool.protectedRole")]
    [InlineData(AdminStudentSchoolChangeStatus.Conflict, 409, "admin.studentSchool.concurrentChange")]
    [InlineData(AdminStudentSchoolChangeStatus.UpstreamFailure, 502, "admin.studentSchool.upstreamFailed")]
    public async Task Failure_statuses_map_to_http_code_and_localized_message(AdminStudentSchoolChangeStatus status, int code, string key)
    {
        Returns(new AdminStudentSchoolChangeResult(status));

        var result = await NewController().ChangeStudentSchool(9, new AdminStudentSchoolRequestDto { SchoolId = 4 }, default);

        var objectResult = result.ShouldBeAssignableTo<ObjectResult>()!;
        objectResult.StatusCode.ShouldBe(code);
        var message = MessageOf(objectResult.Value);
        message.ShouldBe(Localized(key));
        message.ShouldNotBe(key);
    }

    [Fact]
    public async Task Missing_schoolId_is_400_and_service_not_called()
    {
        var result = await NewController().ChangeStudentSchool(9, new AdminStudentSchoolRequestDto(), default);

        MessageOf(result.ShouldBeOfType<BadRequestObjectResult>().Value).ShouldBe(Localized("admin.studentSchool.schoolIdRequired"));
        await _service.DidNotReceiveWithAnyArgs().ChangeSchoolAsync(default, default, default!, default);
    }

    [Fact]
    public async Task Missing_sub_is_forbidden_and_service_not_called()
    {
        var result = await NewController(sub: null).ChangeStudentSchool(9, new AdminStudentSchoolRequestDto { SchoolId = 4 }, default);

        result.ShouldBeOfType<ForbidResult>();
        await _service.DidNotReceiveWithAnyArgs().ChangeSchoolAsync(default, default, default!, default);
    }

    [Fact]
    public void Endpoint_is_admin_only_put_no_store_and_has_its_own_rate_limit_bucket()
    {
        typeof(AdminController).GetCustomAttributes<AuthorizeAttribute>(inherit: false).ShouldContain(a => a.Roles == "Admin");

        var method = typeof(AdminController).GetMethod(nameof(AdminController.ChangeStudentSchool))!;
        method.GetCustomAttribute<HttpPutAttribute>()!.Template.ShouldBe("students/{id:int}/school");
        method.GetCustomAttribute<AllowAnonymousAttribute>().ShouldBeNull();
        method.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore.ShouldBeTrue();
        method.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName.ShouldBe(AdminStudentSchoolRateLimiting.Policy);
        AdminStudentSchoolRateLimiting.Policy.ShouldNotBe(AdminAccountStatusRateLimiting.Policy);

        var options = new AdminStudentSchoolRateLimitOptions();
        options.PermitLimit.ShouldBe(20);
        options.WindowSeconds.ShouldBe(60);
    }
}
