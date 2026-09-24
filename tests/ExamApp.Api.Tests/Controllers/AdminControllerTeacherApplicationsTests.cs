using System.Reflection;
using System.Security.Claims;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
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
/// — her çağrı veri dönmeden önce audit'lenir (fail-closed), 404 de Outcome=NotFound ile; iki uç da admin liste
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

    private static Paged<TeacherApplicationListItemDto> PageOf(int page, int pageSize, int total, params int[] teacherIds) => new()
    {
        PageNumber = page,
        PageSize = pageSize,
        TotalCount = total,
        Items = teacherIds.Select(id => new TeacherApplicationListItemDto { TeacherId = id }).ToList()
    };

    [Fact]
    public async Task List_defaults_to_pending_and_is_audited_with_normalized_paging_and_status_filter()
    {
        var page = PageOf(1, 20, 2, 1, 2);
        _approvals.ListApplicationsAsync(TeacherApplicationStatusFilter.Pending, 1, AdminListPaging.DefaultPageSize, Arg.Any<CancellationToken>())
            .Returns(page);

        var result = await NewController().GetTeacherApplications(ct: default);

        result.Result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeSameAs(page);
        await _audit.Received(1).RecordListAccessAsync(
            new AdminListAccessRecord("kc-admin-sub", AdminDataAccessResource.TeacherApplicationList, null, false, 1, 20, 2, 2,
                TeacherApplicationStatusFilter.Pending),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("all", TeacherApplicationStatusFilter.All)]
    [InlineData("ALL", TeacherApplicationStatusFilter.All)]
    [InlineData("pending", TeacherApplicationStatusFilter.Pending)]
    [InlineData(" Pending ", TeacherApplicationStatusFilter.Pending)]
    [InlineData("", TeacherApplicationStatusFilter.Pending)]
    public async Task List_passes_the_status_filter_and_paging_through_and_audits_it(string status, TeacherApplicationStatusFilter expected)
    {
        // Servis normalize edilmiş sayfayı döner (ör. pageSize 500 → 100); audit servisin değerlerini yazar.
        var page = PageOf(3, 100, 250, 7);
        _approvals.ListApplicationsAsync(expected, 3, 500, Arg.Any<CancellationToken>()).Returns(page);

        var result = await NewController().GetTeacherApplications(status, 3, 500, default);

        result.Result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeSameAs(page);
        await _audit.Received(1).RecordListAccessAsync(
            new AdminListAccessRecord("kc-admin-sub", AdminDataAccessResource.TeacherApplicationList, null, false, 3, 100, 1, 250, expected),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("approved")]
    [InlineData("rejected")]
    [InlineData("1")]
    [InlineData("2")]
    [InlineData("pending,all")]
    public async Task List_with_an_invalid_status_is_400_without_querying_or_auditing(string status)
    {
        var result = await NewController().GetTeacherApplications(status, 1, 20, default);

        result.Result.ShouldBeOfType<BadRequestObjectResult>();
        await _approvals.DidNotReceiveWithAnyArgs().ListApplicationsAsync(default, default, default, default);
        await _audit.DidNotReceiveWithAnyArgs().RecordListAccessAsync(default!, default);
    }

    [Fact]
    public async Task List_is_not_returned_when_audit_fails()
    {
        _approvals.ListApplicationsAsync(Arg.Any<TeacherApplicationStatusFilter>(), Arg.Any<int>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(PageOf(1, 20, 1, 1));
        _audit.RecordListAccessAsync(Arg.Any<AdminListAccessRecord>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("db down")));

        await Should.ThrowAsync<InvalidOperationException>(() => NewController().GetTeacherApplications("all", 1, 20, default));
    }

    [Fact]
    public async Task Detail_is_audited_with_the_target_id()
    {
        var detail = new TeacherApplicationDetailDto { TeacherId = 42, Email = "ali@x.com", Status = "Pending" };
        _approvals.GetApplicationAsync(42, Arg.Any<CancellationToken>()).Returns(detail);

        var result = await NewController().GetTeacherApplication(42, default);

        result.Result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeSameAs(detail);
        await _audit.Received(1).RecordDetailAccessAsync(
            new AdminDetailAccessRecord("kc-admin-sub", AdminDataAccessResource.TeacherApplicationDetail, 42, AdminDataAccessOutcome.Served, "Pending"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Detail_of_a_rejected_application_is_served_and_audited_with_its_status()
    {
        // issue #187: detay yalnızca bekleyen değil, her durumdaki başvuru için döner.
        var detail = new TeacherApplicationDetailDto { TeacherId = 43, Email = "r***@x.com", Status = "Rejected", RejectionReason = "Belge eksik" };
        _approvals.GetApplicationAsync(43, Arg.Any<CancellationToken>()).Returns(detail);

        var result = await NewController().GetTeacherApplication(43, default);

        result.Result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeSameAs(detail);
        await _audit.Received(1).RecordDetailAccessAsync(
            new AdminDetailAccessRecord("kc-admin-sub", AdminDataAccessResource.TeacherApplicationDetail, 43, AdminDataAccessOutcome.Served, "Rejected"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Missing_application_is_404_and_audited_as_not_found()
    {
        _approvals.GetApplicationAsync(7, Arg.Any<CancellationToken>()).Returns((TeacherApplicationDetailDto?)null);

        var result = await NewController().GetTeacherApplication(7, default);

        result.Result.ShouldBeOfType<NotFoundResult>();
        await _audit.Received(1).RecordDetailAccessAsync(
            new AdminDetailAccessRecord("kc-admin-sub", AdminDataAccessResource.TeacherApplicationDetail, 7, AdminDataAccessOutcome.NotFound, null),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Detail_is_not_returned_when_audit_fails()
    {
        _approvals.GetApplicationAsync(42, Arg.Any<CancellationToken>())
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
