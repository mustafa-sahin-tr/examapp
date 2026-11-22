using System.Reflection;
using System.Security.Claims;
using ExamApp.Api.Controllers;
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

namespace ExamApp.Api.Tests.Controllers;

/// <summary>issue #1276: <c>PUT api/admin/teachers/{id}/school-admin</c> durum eşlemesi ve yalnız Admin rolü.</summary>
public class AdminControllerSchoolAdminTests
{
    private readonly IAdminTeacherSchoolAdminService _service = Substitute.For<IAdminTeacherSchoolAdminService>();

    private static AdminController NewController(string? sub = "kc-admin-sub") => new(
        Substitute.For<ITaxonomyService>(), Substitute.For<IClassifierCacheService>(), Substitute.For<ISchoolService>(),
        Substitute.For<IDashboardService>(), Substitute.For<ILocationService>(), Substitute.For<ITeacherApprovalService>(),
        Substitute.For<IAdminTeacherService>(), Substitute.For<IAdminStudentService>(), Substitute.For<IAdminDataAccessAuditService>(),
        Substitute.For<IAdminPasswordResetService>(), Substitute.For<IAdminAccountStatusService>())
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

    private Task<IActionResult> PutAsync(bool? value, string? sub = "kc-admin-sub") =>
        NewController(sub).SetTeacherSchoolAdmin(5, new AdminTeacherSchoolAdminRequestDto { IsSchoolAdmin = value }, _service, default);

    [Fact]
    public async Task Success_returns_200_with_the_new_state()
    {
        _service.SetAsync(5, true, "kc-admin-sub", Arg.Any<CancellationToken>())
            .Returns(new AdminTeacherSchoolAdminResult(AdminTeacherSchoolAdminStatus.Success, true, 12, true));

        var dto = (await PutAsync(true)).ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<AdminTeacherSchoolAdminResponseDto>();

        dto.TeacherId.ShouldBe(5);
        dto.IsSchoolAdmin.ShouldBeTrue();
        dto.SchoolId.ShouldBe(12);
        dto.Changed.ShouldBeTrue();
    }

    [Fact]
    public async Task Missing_field_is_400_and_service_is_not_called()
    {
        (await PutAsync(null)).ShouldBeOfType<BadRequestObjectResult>();
        await _service.DidNotReceiveWithAnyArgs().SetAsync(default, default, default!, default);
    }

    [Fact]
    public async Task Missing_actor_is_forbidden()
    {
        (await PutAsync(true, sub: null)).ShouldBeOfType<ForbidResult>();
    }

    [Theory]
    [InlineData(AdminTeacherSchoolAdminStatus.NoSchool)]
    [InlineData(AdminTeacherSchoolAdminStatus.AccountNotApproved)]
    [InlineData(AdminTeacherSchoolAdminStatus.AccountSuspended)]
    [InlineData(AdminTeacherSchoolAdminStatus.Conflict)]
    public async Task Blocked_states_are_409(AdminTeacherSchoolAdminStatus status)
    {
        _service.SetAsync(5, true, Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(new AdminTeacherSchoolAdminResult(status));
        (await PutAsync(true)).ShouldBeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task Unknown_teacher_is_404()
    {
        _service.SetAsync(5, false, Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AdminTeacherSchoolAdminResult(AdminTeacherSchoolAdminStatus.TargetNotFound));
        (await PutAsync(false)).ShouldBeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public void Endpoint_is_admin_only_put_without_cache()
    {
        typeof(AdminController).GetCustomAttributes<AuthorizeAttribute>(inherit: false).ShouldContain(a => a.Roles == "Admin");
        var method = typeof(AdminController).GetMethod(nameof(AdminController.SetTeacherSchoolAdmin))!;
        method.GetCustomAttribute<HttpPutAttribute>()!.Template.ShouldBe("teachers/{id:int}/school-admin");
        method.GetCustomAttribute<AllowAnonymousAttribute>().ShouldBeNull();
        method.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore.ShouldBeTrue();
    }
}
