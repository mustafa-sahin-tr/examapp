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
/// Issue #153: GET api/admin/students — schoolId/unassigned çakışması 400, parametrelerin servise aynen geçmesi.
/// 401/403 davranışı entegrasyon testinde (AdminStudentEndpointsTests) doğrulanır.
/// </summary>
public class AdminControllerStudentsTests
{
    private readonly IAdminStudentService _adminStudents = Substitute.For<IAdminStudentService>();

    private readonly IAdminDataAccessAuditService _audit = Substitute.For<IAdminDataAccessAuditService>();

    private AdminController NewController() => new(
        Substitute.For<ITaxonomyService>(), Substitute.For<IClassifierCacheService>(), Substitute.For<ISchoolService>(),
        Substitute.For<IDashboardService>(), Substitute.For<ILocationService>(), Substitute.For<ITeacherApprovalService>(),
        Substitute.For<IAdminTeacherService>(), _adminStudents, _audit)
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
    public async Task SchoolId_and_unassigned_together_is_400_and_service_not_called()
    {
        var result = await NewController().GetStudents(schoolId: 3, unassigned: true, page: 1, pageSize: 20, ct: default);

        result.Result.ShouldBeOfType<BadRequestObjectResult>();
        await _adminStudents.DidNotReceive().ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Passes_paging_and_filter_to_service_and_returns_200()
    {
        var paged = new Paged<AdminStudentListItemDto> { PageNumber = 2, PageSize = 20, TotalCount = 25, Items = [] };
        _adminStudents.ListAsync(2, 20, 7, false, Arg.Any<CancellationToken>()).Returns(paged);

        var result = await NewController().GetStudents(schoolId: 7, unassigned: false, page: 2, pageSize: 20, ct: default);

        result.Result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeSameAs(paged);
    }

    [Fact]
    public async Task Unassigned_only_is_passed_through()
    {
        _adminStudents.ListAsync(1, 20, null, true, Arg.Any<CancellationToken>())
            .Returns(new Paged<AdminStudentListItemDto> { PageNumber = 1, PageSize = 20, TotalCount = 0, Items = [] });

        var result = await NewController().GetStudents(schoolId: null, unassigned: true, page: 1, pageSize: 20, ct: default);

        result.Result.ShouldBeOfType<OkObjectResult>();
        await _adminStudents.Received(1).ListAsync(1, 20, null, true, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Default_page_size_is_20()
    {
        var parameter = typeof(AdminController).GetMethod(nameof(AdminController.GetStudents))!
            .GetParameters().Single(p => p.Name == "pageSize");

        parameter.DefaultValue.ShouldBe(20);
        AdminListPaging.MaxPageSize.ShouldBe(100);
    }

    // ---- issue #246: audit + rate limit ----

    [Fact]
    public async Task Issue246_successful_call_is_audited_with_sub_filter_normalized_page_and_row_count()
    {
        // Servis pageSize'ı normalize eder (1000 → 100); audit istemcinin ham değerini değil yanıttakini yazar.
        _adminStudents.ListAsync(3, 1000, 7, false, Arg.Any<CancellationToken>())
            .Returns(new Paged<AdminStudentListItemDto> { PageNumber = 3, PageSize = 100, TotalCount = 250, Items = [new(), new()] });

        await NewController().GetStudents(schoolId: 7, unassigned: false, page: 3, pageSize: 1000, ct: default);

        await _audit.Received(1).RecordListAccessAsync(
            new AdminListAccessRecord("kc-admin-sub", AdminDataAccessResource.StudentList, 7, false, 3, 100, 2, 250),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Issue246_rejected_filter_is_not_audited()
    {
        await NewController().GetStudents(schoolId: 3, unassigned: true, page: 1, pageSize: 20, ct: default);

        await _audit.DidNotReceiveWithAnyArgs().RecordListAccessAsync(default!, default);
    }

    [Fact]
    public async Task Issue246_audit_failure_fails_the_request_instead_of_returning_data()
    {
        _adminStudents.ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new Paged<AdminStudentListItemDto> { PageNumber = 1, PageSize = 20, TotalCount = 1, Items = [new()] });
        _audit.RecordListAccessAsync(Arg.Any<AdminListAccessRecord>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("db down")));

        await Should.ThrowAsync<InvalidOperationException>(() =>
            NewController().GetStudents(schoolId: null, unassigned: false, page: 1, pageSize: 20, ct: default));
    }

    [Fact]
    public void Issue246_endpoint_carries_the_per_user_rate_limit_policy()
    {
        var attribute = typeof(AdminController).GetMethod(nameof(AdminController.GetStudents))!
            .GetCustomAttributes(typeof(EnableRateLimitingAttribute), inherit: false)
            .Cast<EnableRateLimitingAttribute>()
            .Single();

        attribute.PolicyName.ShouldBe(AdminUserListRateLimiting.Policy);
    }
}
