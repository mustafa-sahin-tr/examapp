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
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ExamApp.Api.Tests.Controllers;

/// <summary>
/// Issue #262: GET api/admin/teacher-applications (maskeli liste) ve GET api/admin/teacher-applications/{id} (tam e-posta)
/// — her başarılı çağrı veri dönmeden önce audit'lenir (fail-closed), 404 audit'lenmez; iki uç da admin liste
/// rate limit kovasında, no-store ve 429 audit'i için kaynak metadata'sı taşır.
/// </summary>
public class AdminControllerTeacherApplicationsTests
{
    private readonly ITeacherApprovalService _approvals = Substitute.For<ITeacherApprovalService>();
    private readonly IAdminDataAccessAuditService _audit = Substitute.For<IAdminDataAccessAuditService>();

    private AdminController NewController() => new(
        Substitute.For<ITaxonomyService>(), Substitute.For<IClassifierCacheService>(), Substitute.For<ISchoolService>(),
        Substitute.For<IDashboardService>(), Substitute.For<ILocationService>(), _approvals,
        Substitute.For<IAdminTeacherService>(), Substitute.For<IAdminStudentService>(), _audit,
        Substitute.For<IAdminPasswordResetService>(), Substitute.For<IAdminAccountStatusService>())
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "kc-admin-sub")], "Test"))
            }
        }
    };

    [Fact]
    public async Task List_is_audited_with_count_and_returned()
    {
        var items = new List<PendingTeacherApplicationDto> { new() { TeacherId = 1 }, new() { TeacherId = 2 } };
        _approvals.GetPendingApplicationsAsync(Arg.Any<CancellationToken>()).Returns(items);

        var result = await NewController().GetTeacherApplications(default);

        result.Result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeSameAs(items);
        await _audit.Received(1).RecordListAccessAsync(
            new AdminListAccessRecord("kc-admin-sub", AdminDataAccessResource.TeacherApplicationList, null, false, 1, 2, 2, 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Detail_is_audited_with_the_target_id()
    {
        var detail = new TeacherApplicationDetailDto { TeacherId = 42, Email = "ali@x.com" };
        _approvals.GetPendingApplicationAsync(42, Arg.Any<CancellationToken>()).Returns(detail);

        var result = await NewController().GetTeacherApplication(42, default);

        result.Result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeSameAs(detail);
        await _audit.Received(1).RecordDetailAccessAsync(
            new AdminDetailAccessRecord("kc-admin-sub", AdminDataAccessResource.TeacherApplicationDetail, 42),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Missing_application_is_404_and_not_audited()
    {
        _approvals.GetPendingApplicationAsync(7, Arg.Any<CancellationToken>()).Returns((TeacherApplicationDetailDto?)null);

        var result = await NewController().GetTeacherApplication(7, default);

        result.Result.ShouldBeOfType<NotFoundResult>();
        await _audit.DidNotReceiveWithAnyArgs().RecordDetailAccessAsync(default!, default);
    }

    [Fact]
    public async Task Detail_is_not_returned_when_audit_fails()
    {
        _approvals.GetPendingApplicationAsync(42, Arg.Any<CancellationToken>())
            .Returns(new TeacherApplicationDetailDto { TeacherId = 42 });
        _audit.RecordDetailAccessAsync(Arg.Any<AdminDetailAccessRecord>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("db down")));

        await Should.ThrowAsync<InvalidOperationException>(() => NewController().GetTeacherApplication(42, default));
    }

    [Theory]
    [InlineData(nameof(AdminController.GetTeacherApplications), AdminDataAccessResource.TeacherApplicationList)]
    [InlineData(nameof(AdminController.GetTeacherApplication), AdminDataAccessResource.TeacherApplicationDetail)]
    [InlineData(nameof(AdminController.GetStudents), AdminDataAccessResource.StudentList)]
    [InlineData(nameof(AdminController.GetTeachers), AdminDataAccessResource.TeacherList)]
    public void Endpoint_is_rate_limited_not_cached_and_tagged_for_429_audit(string action, AdminDataAccessResource resource)
    {
        var method = typeof(AdminController).GetMethod(action)!;

        method.GetCustomAttribute<EnableRateLimitingAttribute>()!.PolicyName.ShouldBe(AdminUserListRateLimiting.Policy);
        method.GetCustomAttribute<ResponseCacheAttribute>()!.NoStore.ShouldBeTrue();
        method.GetCustomAttribute<AdminDataAccessAttribute>()!.Resource.ShouldBe(resource);
    }
}
