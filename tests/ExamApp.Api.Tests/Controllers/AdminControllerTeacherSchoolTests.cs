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
/// issue #313: <c>PUT api/admin/teachers/{id}/school</c> — sonuç → HTTP eşlemesi (öğrenci ucuyla paralel), çağıranın sub'ının
/// servise geçmesi, Admin rolü, PUT şablonu, no-store ve öğrenci okul ucuyla ortak rate limit kovası.
/// </summary>
public class AdminControllerTeacherSchoolTests
{
    private readonly IAdminTeacherSchoolService _service = Substitute.For<IAdminTeacherSchoolService>();

    private AdminController NewController(string? sub = "kc-admin") => new(
        Substitute.For<ITaxonomyService>(), Substitute.For<IClassifierCacheService>(), Substitute.For<ISchoolService>(),
        Substitute.For<IDashboardService>(), Substitute.For<ILocationService>(), Substitute.For<ITeacherApprovalService>(),
        Substitute.For<IAdminTeacherService>(), Substitute.For<IAdminStudentService>(), Substitute.For<IAdminDataAccessAuditService>(),
        Substitute.For<IAdminPasswordResetService>(), Substitute.For<IAdminAccountStatusService>(), teacherSchool: _service)
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

    private void Returns(AdminTeacherSchoolChangeResult result) =>
        _service.ChangeSchoolAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(result);

    private static string? MessageOf(object? value) => value?.GetType().GetProperty("message")?.GetValue(value) as string;

    private static string Localized(string key) => ExamApp.Foundation.Localization.FallbackMessageLocalizer.Instance[key].Value;

    [Fact]
    public async Task Success_returns_200_with_new_and_previous_school_and_passes_actor()
    {
        Returns(new AdminTeacherSchoolChangeResult(AdminTeacherSchoolChangeStatus.Success, 3, 4, Changed: true));

        var result = await NewController().ChangeTeacherSchool(9, new AdminTeacherSchoolRequestDto { SchoolId = 4 }, default);

        var dto = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AdminTeacherSchoolResponseDto>();
        dto.TeacherId.ShouldBe(9);
        dto.SchoolId.ShouldBe(4);
        dto.PreviousSchoolId.ShouldBe(3);
        dto.Changed.ShouldBeTrue();
        dto.ProfileCacheStale.ShouldBeFalse();
        await _service.Received(1).ChangeSchoolAsync(9, 4, "kc-admin", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Stale_profile_cache_is_reported_in_the_200_response()
    {
        Returns(new AdminTeacherSchoolChangeResult(AdminTeacherSchoolChangeStatus.Success, 3, 4, Changed: true, ProfileCacheStale: true));

        var result = await NewController().ChangeTeacherSchool(9, new AdminTeacherSchoolRequestDto { SchoolId = 4 }, default);

        var dto = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AdminTeacherSchoolResponseDto>();
        dto.Changed.ShouldBeTrue();
        dto.ProfileCacheStale.ShouldBeTrue();
    }

    [Fact]
    public async Task Same_school_returns_200_with_changed_false()
    {
        Returns(new AdminTeacherSchoolChangeResult(AdminTeacherSchoolChangeStatus.Success, 4, 4, Changed: false));

        var result = await NewController().ChangeTeacherSchool(9, new AdminTeacherSchoolRequestDto { SchoolId = 4 }, default);

        var dto = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AdminTeacherSchoolResponseDto>();
        dto.Changed.ShouldBeFalse();
        dto.PreviousSchoolId.ShouldBe(4);
        dto.SchoolId.ShouldBe(4);
    }

    [Theory]
    [InlineData(AdminTeacherSchoolChangeStatus.TargetNotFound, 404, "admin.teacherSchool.teacherNotFound")]
    [InlineData(AdminTeacherSchoolChangeStatus.SchoolNotFound, 400, "admin.teacherSchool.schoolNotFound")]
    [InlineData(AdminTeacherSchoolChangeStatus.AccountNotFound, 404, "admin.teacherSchool.accountNotFound")]
    [InlineData(AdminTeacherSchoolChangeStatus.ForbiddenSelf, 403, "admin.teacherSchool.self")]
    [InlineData(AdminTeacherSchoolChangeStatus.ForbiddenProtectedRole, 403, "admin.teacherSchool.protectedRole")]
    [InlineData(AdminTeacherSchoolChangeStatus.Conflict, 409, "admin.teacherSchool.concurrentChange")]
    [InlineData(AdminTeacherSchoolChangeStatus.UpstreamFailure, 502, "admin.teacherSchool.upstreamFailed")]
    [InlineData(AdminTeacherSchoolChangeStatus.IndependentActive, 409, "admin.teacherSchool.independentActive")]
    [InlineData(AdminTeacherSchoolChangeStatus.AccountNotApproved, 409, "admin.teacherSchool.accountNotApproved")]
    [InlineData(AdminTeacherSchoolChangeStatus.AccountSuspended, 409, "admin.teacherSchool.accountSuspended")]
    public async Task Failure_statuses_map_to_http_code_and_localized_message(AdminTeacherSchoolChangeStatus status, int code, string key)
    {
        Returns(new AdminTeacherSchoolChangeResult(status));

        var result = await NewController().ChangeTeacherSchool(9, new AdminTeacherSchoolRequestDto { SchoolId = 4 }, default);

        var objectResult = result.ShouldBeAssignableTo<ObjectResult>()!;
        objectResult.StatusCode.ShouldBe(code);
        var message = MessageOf(objectResult.Value);
        message.ShouldBe(Localized(key));
        message.ShouldNotBe(key);
    }

    [Fact]
    public async Task Missing_schoolId_is_400_and_service_not_called()
    {
        var result = await NewController().ChangeTeacherSchool(9, new AdminTeacherSchoolRequestDto(), default);

        MessageOf(result.ShouldBeOfType<BadRequestObjectResult>().Value).ShouldBe(Localized("admin.teacherSchool.schoolIdRequired"));
        await _service.DidNotReceiveWithAnyArgs().ChangeSchoolAsync(default, default, default!, default);
    }

    [Fact]
    public async Task Missing_sub_is_forbidden_and_service_not_called()
    {
        var result = await NewController(sub: null).ChangeTeacherSchool(9, new AdminTeacherSchoolRequestDto { SchoolId = 4 }, default);

        result.ShouldBeOfType<ForbidResult>();
        await _service.DidNotReceiveWithAnyArgs().ChangeSchoolAsync(default, default, default!, default);
    }

    [Fact]
    public void Endpoint_is_admin_only_put_no_store_and_shares_the_school_change_rate_limit_bucket()
    {
        typeof(AdminController).GetCustomAttributes<AuthorizeAttribute>(inherit: false).ShouldContain(a => a.Roles == "Admin");

        var method = typeof(AdminController).GetMethod(nameof(AdminController.ChangeTeacherSchool))!;
        method.GetCustomAttribute<HttpPutAttribute>()!.Template.ShouldBe("teachers/{id:int}/school");
        method.GetCustomAttribute<AllowAnonymousAttribute>().ShouldBeNull();
        method.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore.ShouldBeTrue();
        method.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName.ShouldBe(AdminStudentSchoolRateLimiting.Policy);
    }

    [Fact]
    public void Role_gate_is_Admin_only_with_no_method_level_widening()
    {
        // Review D3: etkin yetki = sınıf düzeyi [Authorize(Roles = "Admin")]; metot düzeyinde rolü genişleten/AllowAnonymous yok.
        var method = typeof(AdminController).GetMethod(nameof(AdminController.ChangeTeacherSchool))!;
        var classGates = typeof(AdminController).GetCustomAttributes<AuthorizeAttribute>(inherit: true).ToList();
        // Birden çok [Authorize] AND ile birleşir: biri Admin ister, hiçbiri başka rol tanımlamaz.
        classGates.ShouldContain(a => a.Roles == "Admin");
        classGates.ShouldAllBe(a => a.Roles == null || a.Roles == "Admin");
        method.GetCustomAttributes<AuthorizeAttribute>(inherit: true).ShouldAllBe(a => a.Roles == null || a.Roles == "Admin");
        method.GetCustomAttributes(inherit: true).OfType<IAllowAnonymous>().ShouldBeEmpty();
        typeof(AdminController).GetCustomAttributes(inherit: true).OfType<IAllowAnonymous>().ShouldBeEmpty();
    }
}
