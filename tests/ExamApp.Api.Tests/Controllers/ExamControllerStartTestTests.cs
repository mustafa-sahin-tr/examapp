using System.Security.Claims;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Worksheets;
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
/// issue #14: StartTest action'ının UnauthorizedAccessException'ı 403'e çevirdiğini doğrular.
/// </summary>
public class ExamControllerStartTestTests
{
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();
    private readonly IExamService _examService = Substitute.For<IExamService>();
    private readonly IStudentService _studentService = Substitute.For<IStudentService>();
    private readonly IWorksheetAssignmentService _assignmentService = Substitute.For<IWorksheetAssignmentService>();
    private readonly ITestSessionService _testSession = Substitute.For<ITestSessionService>();
    private readonly IWorksheetAuthoringService _authoring = Substitute.For<IWorksheetAuthoringService>();
    private readonly IWorksheetDetailService _worksheetDetail = Substitute.For<IWorksheetDetailService>();
    private readonly IWorksheetReminderService _reminderService = Substitute.For<IWorksheetReminderService>();
    private readonly IAuthApiClient _authApiClient = Substitute.For<IAuthApiClient>();

    private ExamController NewController(UserProfileDto authenticatedUser)
    {
        _authApiClient.GetUserProfileAsync().Returns(authenticatedUser);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(_authApiClient);
        services.AddSingleton<IDistributedCache>(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
        services.AddSingleton<UserProfileCacheService>();
        var provider = services.BuildServiceProvider();

        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "kc-1"),
            new Claim("preferred_username", "student1"),
        }, authenticationType: "TestAuth");

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity),
            RequestServices = provider
        };

        var controller = new ExamController(_minio, _examService, _studentService, _assignmentService,
            _testSession, _authoring, _worksheetDetail, _reminderService)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };

        return controller;
    }

    [Fact]
    public async Task StartTest_UnauthorizedAccessException_Returns403WithMessage()
    {
        var user = new UserProfileDto { Id = 42, KeycloakId = "kc-1", Role = "Student" };
        _studentService.GetStudentProfile(42).Returns(new StudentProfileDto { Id = 7, GradeId = 1 });
        _testSession.StartTestAsync(5, Arg.Any<StudentProfileDto>())
            .Returns<Task<TestStartResultDto>>(_ => throw new UnauthorizedAccessException("Bu sınava erişim izniniz yok."));

        var controller = NewController(user);
        var result = await controller.StartTest(5);

        var objectResult = result.ShouldBeOfType<ObjectResult>();
        objectResult.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
    }

    [Fact]
    public async Task StartTest_worksheet_missing_returns_NotFound()
    {
        var user = new UserProfileDto { Id = 42, KeycloakId = "kc-1", Role = "Student" };
        _studentService.GetStudentProfile(42).Returns(new StudentProfileDto { Id = 7, GradeId = 1 });
        _testSession.StartTestAsync(5, Arg.Any<StudentProfileDto>()).Returns((TestStartResultDto)null!);

        var controller = NewController(user);
        var result = await controller.StartTest(5);

        result.ShouldBeOfType<NotFoundObjectResult>();
    }

    [Fact]
    public async Task StartTest_allowed_returns_Ok_with_the_service_result()
    {
        var user = new UserProfileDto { Id = 42, KeycloakId = "kc-1", Role = "Student" };
        _studentService.GetStudentProfile(42).Returns(new StudentProfileDto { Id = 7, GradeId = 1 });
        var expected = new TestStartResultDto { Success = true, InstanceId = 100 };
        _testSession.StartTestAsync(5, Arg.Any<StudentProfileDto>()).Returns(expected);

        var controller = NewController(user);
        var result = await controller.StartTest(5);

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(expected);
    }
}
