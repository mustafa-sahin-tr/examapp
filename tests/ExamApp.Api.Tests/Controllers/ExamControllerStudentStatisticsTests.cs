using System.Reflection;
using System.Security.Claims;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Services.Teachers.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Controllers;

/// <summary>
/// issue #373: <c>GET worksheet/student/statistics</c> öğrenci kaydı olmayan kullanıcıda (öğretmen/admin)
/// null dereference ile 500 veriyordu. Artık uç yalnız Student rolüne açık (Teacher/Admin 403) ve öğrenci kaydı
/// yoksa savunma derinliği olarak 404 döner.
/// </summary>
public class ExamControllerStudentStatisticsTests
{
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();
    private readonly IExamService _examService = Substitute.For<IExamService>();
    private readonly IStudentService _studentService = Substitute.For<IStudentService>();
    private readonly IWorksheetAssignmentService _assignmentService = Substitute.For<IWorksheetAssignmentService>();
    private readonly ITestSessionService _testSession = Substitute.For<ITestSessionService>();
    private readonly IWorksheetAuthoringService _authoring = Substitute.For<IWorksheetAuthoringService>();
    private readonly IWorksheetDetailService _worksheetDetail = Substitute.For<IWorksheetDetailService>();
    private readonly IWorksheetReminderService _reminderService = Substitute.For<IWorksheetReminderService>();
    private readonly IWorksheetCalendarService _calendarService = Substitute.For<IWorksheetCalendarService>();
    private readonly IWorksheetAccessRequestService _accessRequestService = Substitute.For<IWorksheetAccessRequestService>();
    private readonly IAuthApiClient _authApiClient = Substitute.For<IAuthApiClient>();

    private ExamController NewController(UserProfileDto authenticatedUser)
    {
        _authApiClient.GetUserProfileAsync(Arg.Any<CancellationToken>()).Returns(authenticatedUser);

        var schoolContextResolver = Substitute.For<ISchoolContextResolver>();
        schoolContextResolver.ResolveSchoolIdAsync(Arg.Any<UserProfileDto>(), Arg.Any<CancellationToken>())
            .Returns((int?)null);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(_authApiClient);
        services.AddSingleton<IDistributedCache>(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
        services.AddSingleton<UserProfileCacheService>();
        services.AddSingleton(schoolContextResolver);
        services.AddSingleton<IUserProfileProvider, UserProfileProvider>();
        var provider = services.BuildServiceProvider();

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "kc-1"),
            new Claim("preferred_username", "user1"),
        }, authenticationType: "TestAuth");

        return new ExamController(_minio, _examService, _studentService, _assignmentService,
            _testSession, _authoring, _worksheetDetail, _reminderService, _calendarService, _accessRequestService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity), RequestServices = provider }
            }
        };
    }

    [Fact]
    public void GetGroupedStudentStatistics_RequiresOnlyStudentRole_AndKeepsApprovedTeacherPolicy()
    {
        // Teacher/Admin attribute seviyesinde 403 alır (rol kapısı yalnız Student).
        var method = typeof(ExamController).GetMethod(nameof(ExamController.GetGroupedStudentStatistics))!;
        var authorize = method.GetCustomAttributes<AuthorizeAttribute>().ToList();

        authorize.Where(a => a.Roles is not null).Select(a => a.Roles!).ToArray().ShouldBe(["Student"]);
        authorize.Select(a => a.Policy).ShouldContain(ApprovedTeacherPolicies.TeacherOrStudentCapability);
        method.GetCustomAttributes<AllowAnonymousAttribute>().ShouldBeEmpty();
    }

    /// <summary>Savunma derinliği: rol kapısı aşılsa bile (ör. öğrenci kaydı olmayan çağıran) 500 değil 404.</summary>
    [Theory]
    [InlineData("Student")]
    [InlineData("Teacher")]
    [InlineData("Admin")]
    public async Task GetGroupedStudentStatistics_UserWithoutStudentRecord_Returns404_DefensiveNullGuard(string role)
    {
        var user = new UserProfileDto { Id = 42, KeycloakId = "kc-1", Role = role };
        _studentService.GetStudentProfile(42).Returns((StudentProfileDto)null!);

        var controller = NewController(user);
        var result = await controller.GetGroupedStudentStatistics();

        var notFound = result.ShouldBeOfType<NotFoundObjectResult>();
        notFound.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        await _examService.DidNotReceiveWithAnyArgs().GetGroupedStudentStatistics(default);
    }

    [Fact]
    public async Task GetGroupedStudentStatistics_Student_ReturnsOkWithServiceResult()
    {
        var user = new UserProfileDto { Id = 42, KeycloakId = "kc-1", Role = "Student" };
        _studentService.GetStudentProfile(42).Returns(new StudentProfileDto { Id = 7, GradeId = 1 });
        var expected = new ExamAllStatisticsDto();
        _examService.GetGroupedStudentStatistics(7).Returns(expected);

        var controller = NewController(user);
        var result = await controller.GetGroupedStudentStatistics();

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(expected);
    }

    /// <summary>Issue #418: <c>GET worksheet/student-worksheets</c> öğrenci kaydı yoksa 500 (null dereference) değil 404.</summary>
    [Fact]
    public async Task GetWorksheetAndInstances_UserWithoutStudentRecord_Returns404_WithoutCallingService()
    {
        var user = new UserProfileDto { Id = 42, KeycloakId = "kc-1", Role = "Student" };
        _studentService.GetStudentProfile(42).Returns((StudentProfileDto)null!);

        var controller = NewController(user);
        var result = await controller.GetWorksheetAndInstancessAsync(gradeId: 5);

        var notFound = result.ShouldBeOfType<NotFoundObjectResult>();
        notFound.StatusCode.ShouldBe(StatusCodes.Status404NotFound);
        await _examService.DidNotReceiveWithAnyArgs().GetWorksheetAndInstancesAsync(default!, default);
    }

    [Fact]
    public async Task GetWorksheetAndInstances_Student_ReturnsOkWithServiceResult()
    {
        var user = new UserProfileDto { Id = 42, KeycloakId = "kc-1", Role = "Student" };
        var student = new StudentProfileDto { Id = 7, GradeId = 5 };
        _studentService.GetStudentProfile(42).Returns(student);
        var expected = new List<WorksheetWithInstanceDto>();
        _examService.GetWorksheetAndInstancesAsync(student, 5).Returns(expected);

        var controller = NewController(user);
        var result = await controller.GetWorksheetAndInstancessAsync(gradeId: 5);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(expected);
    }
}
