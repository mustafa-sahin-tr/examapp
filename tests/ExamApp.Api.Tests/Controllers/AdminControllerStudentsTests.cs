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
/// Issue #153: GET api/admin/students — schoolId/unassigned çakışması 400, parametrelerin servise aynen geçmesi.
/// 401/403 davranışı entegrasyon testinde (AdminStudentEndpointsTests) doğrulanır.
/// </summary>
public class AdminControllerStudentsTests
{
    private readonly IAdminStudentService _adminStudents = Substitute.For<IAdminStudentService>();

    private AdminController NewController() => new(
        Substitute.For<ITaxonomyService>(), Substitute.For<IClassifierCacheService>(), Substitute.For<ISchoolService>(),
        Substitute.For<IDashboardService>(), Substitute.For<ILocationService>(), Substitute.For<ITeacherApprovalService>(),
        Substitute.For<IAdminTeacherService>(), _adminStudents);

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
}
