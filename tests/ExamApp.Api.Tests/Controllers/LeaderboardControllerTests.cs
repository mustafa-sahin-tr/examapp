using System.Reflection;
using System.Security.Claims;
using ExamApp.Api.Controllers;
using ExamApp.Api.Models.Constants;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Leaderboards;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.Tests.Controllers;

/// <summary>
/// issue #193: GET /api/leaderboard — controller ince: scope parse, sayfalama doğrulama (tek yer), okul kimliğini
/// SUNUCU tarafında çözüp servise geçme, Success=false → 400. Client'tan gelen schoolId yok sayılır.
/// </summary>
public class LeaderboardControllerTests
{
    private readonly ILeaderboardService _service = Substitute.For<ILeaderboardService>();
    private readonly IUserProfileProvider _profiles = Substitute.For<IUserProfileProvider>();

    /// <summary>Profil stub'ı + HttpContext (RequestServices: IConfiguration, ILogger, IUserProfileProvider).</summary>
    private LeaderboardController NewController(
        UserProfileDto profile,
        string role = "Student",
        string? queryString = null,
        params Claim[] extraClaims)
    {
        _profiles.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(profile);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { { "Keycloak:ServiceClients:0", "exam-admin" } })
            .Build());
        services.AddSingleton(_profiles);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, profile.KeycloakId),
            new(ClaimTypes.Role, role)
        };
        claims.AddRange(extraClaims);
        var identity = new ClaimsIdentity(claims, authenticationType: "TestAuth");

        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(identity),
            RequestServices = services.BuildServiceProvider()
        };
        if (queryString != null)
        {
            http.Request.QueryString = new QueryString(queryString);
        }

        return new LeaderboardController(_service)
        {
            ControllerContext = new ControllerContext { HttpContext = http }
        };
    }

    private static UserProfileDto Student(int id, int? schoolId) =>
        new() { Id = id, KeycloakId = $"kc-{id}", Role = "Student", SchoolId = schoolId };

    private void ServiceReturnsOk() =>
        _service.GetLeaderboardAsync(Arg.Any<LeaderboardRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LeaderboardDto { Success = true });

    private LeaderboardRequest? CapturedRequest()
    {
        var call = _service.ReceivedCalls().SingleOrDefault(c => c.GetMethodInfo().Name == nameof(ILeaderboardService.GetLeaderboardAsync));
        return call?.GetArguments()[0] as LeaderboardRequest;
    }

    private static string MessageOf(IActionResult result) =>
        (string)result.ShouldBeOfType<BadRequestObjectResult>().Value!.GetType().GetProperty("message")!.GetValue(
            ((BadRequestObjectResult)result).Value)!;

    [Fact]
    public void Endpoint_RequiresAuthorization_AndHasNoClientSchoolIdParameter()
    {
        var method = typeof(LeaderboardController).GetMethod(nameof(LeaderboardController.GetLeaderboard))!;

        method.GetCustomAttribute<AuthorizeAttribute>(inherit: false).ShouldNotBeNull();
        typeof(LeaderboardController).GetCustomAttribute<RouteAttribute>()!.Template.ShouldBe("api/[controller]");

        // Sözleşme: okul kimliği client'tan bağlanmaz.
        method.GetParameters().Select(p => p.Name).ShouldNotContain("schoolId");
        method.GetParameters().Select(p => p.Name).ShouldBe(new[] { "scope", "skip", "take", "ct" });
    }

    [Fact]
    public void EntryDto_ExposesNoPii()
    {
        // Global listede başka okulların öğrencileri görünür → kimlik/tenant alanı yok.
        var props = typeof(LeaderboardEntryDto).GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();

        props.ShouldBe(new[] { "AvatarUrl", "FullName", "IsMe", "Level", "Rank", "Xp" });
        typeof(LeaderboardDto).GetProperty("MyStudentId").ShouldBeNull();
    }

    [Fact]
    public async Task NoScope_DefaultsToGlobal_WithServerResolvedSchoolId()
    {
        ServiceReturnsOk();
        var controller = NewController(Student(201, schoolId: 7));

        var result = await controller.GetLeaderboard(scope: null);

        result.ShouldBeOfType<OkObjectResult>();
        var req = CapturedRequest()!;
        req.Scope.ShouldBe(LeaderboardScope.Global);
        req.RequesterUserId.ShouldBe(201);
        req.CurrentSchoolId.ShouldBe(7);
        req.Skip.ShouldBe(0);
        req.Take.ShouldBe(LeaderboardService.DefaultTake);
    }

    [Theory]
    [InlineData("school")]
    [InlineData("School")]
    [InlineData(" SCHOOL ")]
    public async Task ScopeSchool_IsParsedCaseInsensitively(string scope)
    {
        ServiceReturnsOk();
        var controller = NewController(Student(201, schoolId: 7));

        var result = await controller.GetLeaderboard(scope);

        result.ShouldBeOfType<OkObjectResult>();
        CapturedRequest()!.Scope.ShouldBe(LeaderboardScope.School);
    }

    [Fact]
    public async Task ScopeGlobal_Explicit_IsSameAsDefault()
    {
        ServiceReturnsOk();
        var controller = NewController(Student(201, schoolId: 7));

        await controller.GetLeaderboard("global");

        CapturedRequest()!.Scope.ShouldBe(LeaderboardScope.Global);
    }

    [Fact]
    public async Task InvalidScope_Returns400_WithoutCallingService()
    {
        var controller = NewController(Student(201, schoolId: 7));

        var result = await controller.GetLeaderboard("class");

        result.ShouldBeOfType<BadRequestObjectResult>();
        await _service.DidNotReceive().GetLeaderboardAsync(Arg.Any<LeaderboardRequest>(), Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(-1, 20)]
    [InlineData(0, 0)]
    [InlineData(0, LeaderboardService.MaxTake + 1)]
    public async Task InvalidPaging_Returns400_WithMaxTakeInMessage(int skip, int take)
    {
        var controller = NewController(Student(201, schoolId: 7));

        var result = await controller.GetLeaderboard("global", skip, take);

        MessageOf(result).ShouldContain(LeaderboardService.MaxTake.ToString());
        await _service.DidNotReceive().GetLeaderboardAsync(Arg.Any<LeaderboardRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ValidPagingBoundaries_ArePassedThroughUnchanged()
    {
        ServiceReturnsOk();
        var controller = NewController(Student(201, schoolId: 7));

        await controller.GetLeaderboard("global", skip: 500, take: LeaderboardService.MaxTake);

        var req = CapturedRequest()!;
        req.Skip.ShouldBe(500);
        req.Take.ShouldBe(LeaderboardService.MaxTake);
    }

    [Fact]
    public async Task ClientSchoolIdQueryParameter_IsIgnored_ServerValueWins()
    {
        ServiceReturnsOk();
        // Client başka bir okulu (999) dayatmaya çalışıyor; DB'deki okul 7.
        var controller = NewController(Student(201, schoolId: 7), queryString: "?scope=school&schoolId=999");

        await controller.GetLeaderboard("school");

        CapturedRequest()!.CurrentSchoolId.ShouldBe(7);
    }

    [Fact]
    public async Task SchoolIdClaimDiffersFromDb_DbWins()
    {
        ServiceReturnsOk();
        var controller = NewController(Student(201, schoolId: 7), extraClaims: new Claim(ExamClaimTypes.SchoolId, "999"));

        await controller.GetLeaderboard("school");

        CapturedRequest()!.CurrentSchoolId.ShouldBe(7);
    }

    [Fact]
    public async Task SchoollessStudent_ScopeSchool_Returns400WithServiceMessage()
    {
        _service.GetLeaderboardAsync(Arg.Any<LeaderboardRequest>(), Arg.Any<CancellationToken>())
            .Returns(new LeaderboardDto { Success = false, Message = "uygulanamaz", SchoolScopeAvailable = false });
        var controller = NewController(Student(301, schoolId: null));

        var result = await controller.GetLeaderboard("school");

        MessageOf(result).ShouldBe("uygulanamaz");
        CapturedRequest()!.CurrentSchoolId.ShouldBeNull();
    }

    [Fact]
    public async Task SchoolBoundTeacher_ScopeSchool_PassesTeachersSchool()
    {
        ServiceReturnsOk();
        var controller = NewController(
            new UserProfileDto { Id = 55, KeycloakId = "kc-55", Role = "Teacher", SchoolId = 7 }, role: "Teacher");

        var result = await controller.GetLeaderboard("school");

        result.ShouldBeOfType<OkObjectResult>();
        var req = CapturedRequest()!;
        req.Scope.ShouldBe(LeaderboardScope.School);
        req.RequesterUserId.ShouldBe(55);
        req.CurrentSchoolId.ShouldBe(7);
    }

    [Fact]
    public async Task Admin_ScopeGlobal_SchoolIdIsNull_EvenIfProfileHasSchool()
    {
        // Admin muafiyeti: GetSchoolScopeAsync Unrestricted → SchoolId null.
        ServiceReturnsOk();
        var controller = NewController(new UserProfileDto { Id = 9, KeycloakId = "kc-9", Role = "Admin", SchoolId = 7 }, role: "Admin");

        await controller.GetLeaderboard("global");

        var req = CapturedRequest()!;
        req.CurrentSchoolId.ShouldBeNull();
        req.RequesterUserId.ShouldBe(9);
    }

    [Fact]
    public async Task Admin_ScopeSchool_Returns400_BecauseAdminHasNoSchool()
    {
        // Gerçek servisle uçtan uca: admin → SchoolId null → school kapsamı uygulanamaz.
        using var db = ExamApp.Api.Tests.Support.TestDb.Create();
        await using var ctx = db.NewContext();
        var realService = new LeaderboardService(ctx, Substitute.For<IAuthApiClient>(),
            Substitute.For<Microsoft.Extensions.Logging.ILogger<LeaderboardService>>());
        var profile = new UserProfileDto { Id = 9, KeycloakId = "kc-9", Role = "Admin", SchoolId = 7 };
        _profiles.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(profile);
        var stubbed = NewController(profile, role: "Admin");
        var controller = new LeaderboardController(realService) { ControllerContext = stubbed.ControllerContext };

        var result = await controller.GetLeaderboard("school");

        MessageOf(result).ShouldNotBe("student.leaderboard.schoolScopeUnavailable");
        MessageOf(result).ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task ProfileCannotBeResolved_Throws_FailClosed()
    {
        var controller = NewController(Student(201, schoolId: 7));
        // NewController'ın kurduğu stub'ı ez: profil çözülemiyor.
        _profiles.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns<UserProfileDto>(_ => throw new InvalidOperationException("redis down"));

        await Should.ThrowAsync<InvalidOperationException>(() => controller.GetLeaderboard("school"));
        await _service.DidNotReceive().GetLeaderboardAsync(Arg.Any<LeaderboardRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Success_ReturnsOkWithDtoAsIs()
    {
        var dto = new LeaderboardDto { Success = true, Scope = "school", SchoolId = 7, SchoolScopeAvailable = true, MyRank = 2 };
        _service.GetLeaderboardAsync(Arg.Any<LeaderboardRequest>(), Arg.Any<CancellationToken>()).Returns(dto);
        var controller = NewController(Student(201, schoolId: 7));

        var result = await controller.GetLeaderboard("school");

        result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeSameAs(dto);
    }
}
