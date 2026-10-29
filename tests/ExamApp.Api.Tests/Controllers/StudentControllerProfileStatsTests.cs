using System.Security.Claims;
using ExamApp.Api.Controllers;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Leaderboards;
using ExamApp.Api.Services.LoginEvents;
using ExamApp.Api.Services.StudentReset;
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
/// Issue #501: profildeki çözülen / doğru / yanlış / tamamlanan test sayıları istatistik kuralından
/// (<see cref="IExamService.GetGroupedStudentStatistics"/>), sıra genel liderlik tablosundan gelir — eskiden kimsenin yazmadığı
/// <c>StudentPointHistories</c> / <c>Leaderboards</c> tablolarından okunuyor ve hep 0 dönüyordu.
/// </summary>
public class StudentControllerProfileStatsTests
{
    private const int UserId = 42;
    private const string KeycloakId = "kc-42";
    private const int StudentId = 5;

    private readonly IStudentService _students = Substitute.For<IStudentService>();
    private readonly IExamService _exams = Substitute.For<IExamService>();
    private readonly ILeaderboardService _leaderboard = Substitute.For<ILeaderboardService>();

    public StudentControllerProfileStatsTests()
    {
        _students.GetStudentProfile(UserId).Returns(_ => new StudentProfileDto { Id = StudentId, XP = 120 });
        _exams.GetGroupedStudentStatistics(StudentId).Returns(new ExamAllStatisticsDto
        {
            Total = new ExamStatisticsDto { TotalSolvedTests = 4, CompletedTests = 3, TotalCorrectAnswers = 17, TotalWrongAnswers = 6 }
        });
        _leaderboard.GetRankForXpAsync(StudentId, 120, LeaderboardScope.Global, null, Arg.Any<CancellationToken>())
            .Returns(new LeaderboardRank(2, 30));
    }

    private StudentController NewController(bool withStats = true)
    {
        var profileProvider = Substitute.For<IUserProfileProvider>();
        profileProvider.GetAsync(KeycloakId, Arg.Any<CancellationToken>())
            .Returns(_ => new UserProfileDto { Id = UserId, KeycloakId = KeycloakId, FullName = "Ali Kaya", Email = "s@x", Role = "Student" });
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .AddSingleton(profileProvider)
            .BuildServiceProvider();

        var controller = new StudentController(
            Substitute.For<IMinIoService>(),
            _students,
            new UserProfileCacheService(
                new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
                Substitute.For<ILogger<UserProfileCacheService>>()),
            Options.Create(new KeycloakSettings()),
            Substitute.For<IKeycloakService>(),
            Substitute.For<IStudentResetScheduler>(),
            Substitute.For<ILoginEventService>(),
            Substitute.For<ILogger<StudentController>>(),
            examService: withStats ? _exams : null,
            leaderboardService: withStats ? _leaderboard : null);

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, KeycloakId) }, "TestAuth")),
                RequestServices = services
            }
        };
        return controller;
    }

    [Fact]
    public async Task Profile_counts_come_from_the_statistics_rule_and_rank_from_the_global_leaderboard()
    {
        var result = await NewController().GetStudentProfile();

        var dto = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<StudentProfileDto>();
        dto.ShouldSatisfyAllConditions(
            d => d.CorrectAnswers.ShouldBe(17),
            d => d.WrongAnswers.ShouldBe(6),
            d => d.TotalQuestionsSolved.ShouldBe(23),
            // Tamamlanan = Completed oturumlar (#396), başlatılmış/süresi dolmuş oturum sayılmaz.
            d => d.TestsCompleted.ShouldBe(3),
            d => d.LeaderboardRank.ShouldBe(2));
    }

    [Fact]
    public async Task Without_stat_services_the_profile_still_returns_with_zero_counts()
    {
        var result = await NewController(withStats: false).GetStudentProfile();

        var dto = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<StudentProfileDto>();
        dto.TotalQuestionsSolved.ShouldBe(0);
        dto.LeaderboardRank.ShouldBe(0);
    }

    [Fact]
    public async Task Missing_student_record_returns_not_found_without_computing_stats()
    {
        _students.GetStudentProfile(UserId).Returns((StudentProfileDto?)null!);

        var result = await NewController().GetStudentProfile();

        result.ShouldBeOfType<NotFoundObjectResult>();
        await _exams.DidNotReceive().GetGroupedStudentStatistics(Arg.Any<int>());
    }
}
