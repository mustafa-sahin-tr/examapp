using ExamApp.Api.Controllers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Services.Classifier;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.Locations;
using ExamApp.Api.Services.Schools;
using ExamApp.Api.Services.Taxonomy;
using ExamApp.Api.Services.TeacherApprovals;
using Microsoft.AspNetCore.Mvc;

namespace ExamApp.Api.Tests.Controllers;

/// <summary>
/// Issue #152: GET api/admin/teachers — schoolId/unassigned çakışması 400, parametrelerin servise aynen geçmesi.
/// Admin rol zorunluluğu sınıf seviyesindeki [Authorize(Roles="Admin")] ile (AdminControllerDashboardTrendsTests),
/// 401/403 davranışı entegrasyon testinde (AdminTeacherEndpointsTests) doğrulanır.
/// </summary>
public class AdminControllerTeachersTests
{
    private readonly IAdminTeacherService _adminTeachers = Substitute.For<IAdminTeacherService>();

    private AdminController NewController() => new(
        Substitute.For<ITaxonomyService>(), Substitute.For<IClassifierCacheService>(), Substitute.For<ISchoolService>(),
        Substitute.For<IDashboardService>(), Substitute.For<ILocationService>(), Substitute.For<ITeacherApprovalService>(),
        _adminTeachers, Substitute.For<IAdminStudentService>());

    [Fact]
    public async Task SchoolId_and_unassigned_together_is_400_and_service_not_called()
    {
        var result = await NewController().GetTeachers(schoolId: 3, unassigned: true, page: 1, pageSize: 20, ct: default);

        result.Result.ShouldBeOfType<BadRequestObjectResult>();
        await _adminTeachers.DidNotReceive().ListAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Passes_paging_and_filter_to_service_and_returns_200()
    {
        var paged = new Paged<AdminTeacherListItemDto> { PageNumber = 2, PageSize = 20, TotalCount = 25, Items = [] };
        _adminTeachers.ListAsync(2, 20, 7, false, Arg.Any<CancellationToken>()).Returns(paged);

        var result = await NewController().GetTeachers(schoolId: 7, unassigned: false, page: 2, pageSize: 20, ct: default);

        result.Result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeSameAs(paged);
    }

    [Fact]
    public void Default_page_size_is_20()
    {
        var parameter = typeof(AdminController).GetMethod(nameof(AdminController.GetTeachers))!
            .GetParameters().Single(p => p.Name == "pageSize");

        parameter.DefaultValue.ShouldBe(20);
        AdminListPaging.MaxPageSize.ShouldBe(100);
    }
}
