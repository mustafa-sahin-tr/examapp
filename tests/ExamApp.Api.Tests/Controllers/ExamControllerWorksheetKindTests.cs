using System.Security.Claims;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
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
/// issue #573: <c>ExamController</c> — <c>kind</c> sorgu parametresi servise geçer (liste ve aktif atamalar; verilmezse
/// null = hepsi), tür kilidi (<c>WorksheetKindLocked</c>) 409 döner, etkinlik bitirme kapısı (<c>ActivityCheckRequired</c>)
/// 409 döner ve başarılı yazma 200.
/// </summary>
public class ExamControllerWorksheetKindTests
{
    private const int UserId = 4501;

    private static ExamDto Dto(WorksheetKind? kind, int? id = null) => new()
    {
        Id = id, Name = "Sayfa", GradeId = 1, MaxDurationSeconds = 0, Kind = kind,
    };

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

    private ExamController NewController()
    {
        _authApiClient.GetUserProfileAsync(Arg.Any<CancellationToken>())
            .Returns(new UserProfileDto { Id = UserId, KeycloakId = "kc-4501", Role = "Student" });

        var schoolContextResolver = Substitute.For<ISchoolContextResolver>();
        schoolContextResolver.ResolveSchoolIdAsync(Arg.Any<UserProfileDto>(), Arg.Any<CancellationToken>()).Returns((int?)null);

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
            new Claim(ClaimTypes.NameIdentifier, "kc-4501"),
            new Claim("preferred_username", "student4501"),
            new Claim(ClaimTypes.Role, "Student"),
        }, authenticationType: "TestAuth");

        var httpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity), RequestServices = provider };

        return new ExamController(_minio, _examService, _studentService, _assignmentService,
            _testSession, _authoring, _worksheetDetail, _reminderService, _calendarService, _accessRequestService)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    // ---------------------------------------------------------------- tür kilidi → 409

    [Fact]
    public async Task CreateOrUpdate_KindLocked_Returns409WithErrorCode()
    {
        var response = new ExamSavedDto
        {
            Success = false, Conflict = true, ErrorCode = WorksheetAuthoringErrorCodes.WorksheetKindLocked, Message = "kilitli",
        };
        _authoring.CreateOrUpdateAsync(Arg.Any<ExamDto>(), UserId, Arg.Any<bool>()).Returns(response);

        var result = await NewController().CreateOrUpdateAsync(Dto(WorksheetKind.Activity, id: 9));

        var conflict = result.ShouldBeOfType<ConflictObjectResult>();
        conflict.Value.ShouldBe(response);
        ((ExamSavedDto)conflict.Value!).ErrorCode.ShouldBe("WorksheetKindLocked");
    }

    [Fact]
    public async Task CreateOrUpdate_ActivityAccepted_Returns200()
    {
        var response = new ExamSavedDto { Success = true, ExamId = 9, Message = "ok" };
        _authoring.CreateOrUpdateAsync(Arg.Any<ExamDto>(), UserId, Arg.Any<bool>()).Returns(response);

        var result = await NewController().CreateOrUpdateAsync(Dto(WorksheetKind.Activity));

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBe(response);
    }

    [Fact]
    public async Task CreateOrUpdate_PassesKindThroughToTheService()
    {
        _authoring.CreateOrUpdateAsync(Arg.Any<ExamDto>(), UserId, Arg.Any<bool>()).Returns(new ExamSavedDto { Success = true });

        await NewController().CreateOrUpdateAsync(Dto(WorksheetKind.Activity));

        await _authoring.Received(1).CreateOrUpdateAsync(
            Arg.Is<ExamDto>(d => d.Kind == WorksheetKind.Activity), UserId, Arg.Any<bool>());
    }

    // ---------------------------------------------------------------- bitirme kapısı → 409

    [Fact]
    public async Task EndTest_ActivityCheckRequired_Returns409()
    {
        _testSession.EndTest(5, UserId, Arg.Any<EndTestRequestDto?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TestSessionResultDto
            {
                Success = false, Conflict = true, ErrorCode = TestSessionErrorCodes.ActivityCheckRequired,
            });

        var result = await NewController().EndTest(5, default);

        result.ShouldBeOfType<ConflictObjectResult>();
    }

    [Fact]
    public async Task EndTest_ForwardsActiveSecondsBodyToService_Issue572()
    {
        _testSession.EndTest(6, UserId, Arg.Any<EndTestRequestDto?>(), Arg.Any<string?>(), Arg.Any<CancellationToken>())
            .Returns(new TestSessionResultDto { Success = true });

        var result = await NewController().EndTest(6, default, new EndTestRequestDto { ActiveSeconds = 321 });

        result.ShouldBeOfType<OkObjectResult>();
        await _testSession.Received(1).EndTest(
            6, UserId, Arg.Is<EndTestRequestDto?>(r => r != null && r.ActiveSeconds == 321), Arg.Any<string?>(), Arg.Any<CancellationToken>());
    }

    // ---------------------------------------------------------------- kind sorgu parametresi

    [Fact]
    public async Task GetActiveAssignments_KindActivity_PassesKindToService()
    {
        _assignmentService.GetActiveAssignmentsForStudentAsync(Arg.Any<StudentProfileDto>(), WorksheetKind.Activity)
            .Returns(new List<AssignedWorksheetDto>());

        await NewController().GetActiveAssignments(WorksheetKind.Activity);

        await _assignmentService.Received(1).GetActiveAssignmentsForStudentAsync(Arg.Any<StudentProfileDto>(), WorksheetKind.Activity);
    }

    [Fact]
    public async Task GetActiveAssignments_KindOmitted_PassesNullMeaningAll()
    {
        _assignmentService.GetActiveAssignmentsForStudentAsync(Arg.Any<StudentProfileDto>(), null)
            .Returns(new List<AssignedWorksheetDto>());

        await NewController().GetActiveAssignments();

        await _assignmentService.Received(1).GetActiveAssignmentsForStudentAsync(Arg.Any<StudentProfileDto>(), null);
    }

    [Fact]
    public async Task GetWorksheets_KindTest_IsForwardedInTheStudentFilter()
    {
        await NewController().GetWorksheetsAsync(kind: WorksheetKind.Test);

        await _examService.Received(1).GetWorksheetsForStudentsAsync(
            Arg.Is<ExamFilterDto>(f => f.kind == WorksheetKind.Test), Arg.Any<StudentProfileDto>());
    }

    [Fact]
    public async Task GetWorksheets_KindOmitted_IsNullInTheFilter()
    {
        await NewController().GetWorksheetsAsync();

        await _examService.Received(1).GetWorksheetsForStudentsAsync(
            Arg.Is<ExamFilterDto>(f => f.kind == null), Arg.Any<StudentProfileDto>());
    }
}
