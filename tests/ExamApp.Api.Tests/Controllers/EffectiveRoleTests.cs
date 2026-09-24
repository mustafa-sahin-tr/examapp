using System.Security.Claims;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Teachers.Authorization;
using ExamApp.Api.Services.Worksheets;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Tests.Controllers;

/// <summary>
/// issue #277 review (security HIGH): öğretmen/öğrenci dal kararları profil rolüne (auth-api) değil, JWT ile doğrulanmış etkin
/// role (<see cref="EffectiveRole"/>) dayanır. JWT Student + profil Teacher → öğrenci dalı; öğretmen dalına yalnızca JWT'si
/// Teacher olan girer — ve o çağıran için #287 ApprovedTeacher kapısı her zaman uygulanır.
/// </summary>
public class EffectiveRoleTests
{
    private static ClaimsPrincipal Principal(params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "kc-1") };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    [Theory]
    [InlineData("Teacher", new[] { "Student" }, "Student")]            // uyuşmazlık: JWT kazanır
    [InlineData("Student", new[] { "Teacher" }, "Teacher")]
    [InlineData("Teacher", new[] { "Teacher" }, "Teacher")]            // doğrulanmış profil rolü
    [InlineData("Student", new[] { "Teacher", "Student" }, "Student")] // çok rollü: profil rolü JWT'de var
    [InlineData("Teacher", new string[0], "")]                        // JWT'de uygulama rolü yok → dal yok
    [InlineData("", new[] { "Parent" }, "Parent")]
    [InlineData(null, new[] { "Teacher" }, "Teacher")]
    [InlineData("Admin", new[] { "Admin" }, "Admin")]
    public void Resolve_uses_the_profile_role_only_when_the_jwt_confirms_it(string? profileRole, string[] jwtRoles, string expected)
        => EffectiveRole.Resolve(profileRole, Principal(jwtRoles)).ShouldBe(expected);

    [Theory]
    [InlineData("Teacher", new[] { "Student" })]
    [InlineData("Student", new[] { "Teacher" })]
    [InlineData(null, new[] { "Teacher" })]
    [InlineData("Teacher", new[] { "Teacher", "Student" })]
    [InlineData("Teacher", new string[0])]
    public void Teacher_branch_implies_the_approved_teacher_gate_applies(string? profileRole, string[] jwtRoles)
    {
        var principal = Principal(jwtRoles);
        var requirement = new ApprovedTeacherRequirement("Admin", "SuperAdmin");

        if (EffectiveRole.Resolve(profileRole, principal) == nameof(UserRole.Teacher))
            requirement.AppliesTo(principal).ShouldBeTrue();
    }

    // ---- ExamController uçtan uca (profil sağlayıcı → BaseController → dal) ----

    private readonly IExamService _examService = Substitute.For<IExamService>();
    private readonly IStudentService _studentService = Substitute.For<IStudentService>();
    private readonly IWorksheetDetailService _worksheetDetail = Substitute.For<IWorksheetDetailService>();

    private ExamController NewExamController(string profileRole, params string[] jwtRoles)
    {
        var profiles = Substitute.For<IUserProfileProvider>();
        profiles.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => new UserProfileDto { Id = 5, KeycloakId = "kc-1", Role = profileRole });
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddSingleton(profiles)
            .BuildServiceProvider();

        return new ExamController(Substitute.For<IMinIoService>(), _examService, _studentService,
            Substitute.For<IWorksheetAssignmentService>(), Substitute.For<ITestSessionService>(),
            Substitute.For<IWorksheetAuthoringService>(), _worksheetDetail, Substitute.For<IWorksheetReminderService>(),
            Substitute.For<IWorksheetCalendarService>(), Substitute.For<IWorksheetAccessRequestService>())
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = Principal(jwtRoles), RequestServices = services }
            }
        };
    }

    [Fact]
    public async Task Detail_with_jwt_student_and_profile_teacher_takes_the_student_branch()
    {
        _studentService.GetStudentProfile(5).Returns(new StudentProfileDto { Id = 50 });

        await NewExamController("Teacher", "Student").GetWorksheetDetail(9, default);

        await _worksheetDetail.Received(1).GetWorksheetDetailAsync(9, "Student", 50, 5, false, Arg.Any<CancellationToken>());
        await _worksheetDetail.DidNotReceive().GetWorksheetDetailAsync(Arg.Any<int>(), "Teacher", Arg.Any<int?>(), Arg.Any<int>(),
            Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Detail_with_jwt_teacher_takes_the_teacher_branch()
    {
        await NewExamController("Student", "Teacher").GetWorksheetDetail(9, default);

        await _worksheetDetail.Received(1).GetWorksheetDetailAsync(9, "Teacher", null, 5, false, Arg.Any<CancellationToken>());
        await _studentService.DidNotReceiveWithAnyArgs().GetStudentProfile(default);
    }

    [Fact]
    public async Task List_with_jwt_student_and_profile_teacher_uses_the_student_listing()
    {
        _studentService.GetStudentProfile(5).Returns(new StudentProfileDto { Id = 50 });

        await NewExamController("Teacher", "Student").GetWorksheetsAsync();

        await _examService.Received(1).GetWorksheetsForStudentsAsync(Arg.Any<ExamFilterDto>(), Arg.Any<StudentProfileDto>());
        await _examService.DidNotReceiveWithAnyArgs().GetWorksheetsForTeacherAsync(default!, default!, default);
    }

    [Fact]
    public async Task Latest_with_jwt_student_and_profile_teacher_does_not_apply_the_teacher_owner_filter()
    {
        await NewExamController("Teacher", "Student").GetLatestWorksheetsAsync();

        await _examService.Received(1).GetLatestWorksheetsAsync(Arg.Any<int>(), Arg.Any<int>(), null);
    }
}
