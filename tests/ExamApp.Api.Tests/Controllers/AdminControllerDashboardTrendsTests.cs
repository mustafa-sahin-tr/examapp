using ExamApp.Api.Controllers;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.Classifier;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.Schools;
using ExamApp.Api.Services.Taxonomy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamApp.Api.Tests.Controllers;

/// <summary>
/// Issue #87: GET /api/admin/dashboard/trends — days-parameter validation and pass-through to
/// <see cref="IDashboardService"/>. The "Admin" role requirement is enforced by the
/// class-level [Authorize] on <see cref="AdminController"/> (see
/// <see cref="AdminController_ClassLevelAuthorizeAttribute_RequiresAdminRole"/>) rather than a
/// per-action attribute, so no separate per-action authorization test is needed here.
/// </summary>
public class AdminControllerDashboardTrendsTests
{
    private readonly ITaxonomyService _taxonomy = Substitute.For<ITaxonomyService>();
    private readonly IClassifierCacheService _classifierCache = Substitute.For<IClassifierCacheService>();
    private readonly ISchoolService _schools = Substitute.For<ISchoolService>();
    private readonly IDashboardService _dashboard = Substitute.For<IDashboardService>();

    private AdminController NewController() => new(_taxonomy, _classifierCache, _schools, _dashboard);

    [Fact]
    public void AdminController_ClassLevelAuthorizeAttribute_RequiresAdminRole()
    {
        // inherit: false — AdminController declares its own [Authorize(Roles="Admin")]; BaseController
        // also has a bare [Authorize], but that's not what we're asserting here.
        var attribute = typeof(AdminController).GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
            .Cast<AuthorizeAttribute>()
            .SingleOrDefault();

        attribute.ShouldNotBeNull();
        attribute!.Roles.ShouldBe("Admin");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(366)]
    [InlineData(1000)]
    public async Task GetDashboardTrends_DaysOutOfRange_ReturnsBadRequestAndDoesNotCallService(int days)
    {
        var controller = NewController();

        var result = await controller.GetDashboardTrends(days, default);

        result.Result.ShouldBeOfType<BadRequestObjectResult>();
        await _dashboard.DidNotReceive().GetTrendsAsync(Arg.Any<int>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(30)]
    [InlineData(365)]
    public async Task GetDashboardTrends_DaysWithinRange_ReturnsServiceResult(int days)
    {
        var dto = new DashboardTrendsDto
        {
            QuestionCreated = new List<DailyPointDto> { new() { Date = new DateOnly(2026, 1, 1), Count = 3 } },
            QuestionSolved = new List<DailyPointDto> { new() { Date = new DateOnly(2026, 1, 1), Count = 5 } },
        };
        _dashboard.GetTrendsAsync(days, Arg.Any<CancellationToken>()).Returns(dto);

        var result = await NewController().GetDashboardTrends(days, default);

        result.Result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(dto);
        await _dashboard.Received(1).GetTrendsAsync(days, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetDashboardTrends_NoDaysProvided_DefaultsToThirty()
    {
        var dto = new DashboardTrendsDto();
        _dashboard.GetTrendsAsync(30, Arg.Any<CancellationToken>()).Returns(dto);

        // Mirrors the controller's default parameter value (`days = 30`).
        var result = await NewController().GetDashboardTrends(ct: default);

        result.Result.ShouldBeOfType<OkObjectResult>();
        await _dashboard.Received(1).GetTrendsAsync(30, Arg.Any<CancellationToken>());
    }
}
