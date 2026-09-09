using System.Linq;
using System.Security.Claims;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.LoginEvents;
using ExamApp.Api.Services.StudentReset;
using ExamApp.Api.Tests.Support;
using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Controllers;

/// <summary>
/// Issue #125: GET api/student/me/last-login — öğrencinin mevcut oturumu hariç bir önceki
/// başarılı girişini döner.
/// </summary>
public class StudentControllerLastLoginTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();
    private readonly IStudentService _studentService = Substitute.For<IStudentService>();
    private readonly IKeycloakService _keycloakService = Substitute.For<IKeycloakService>();
    private readonly IBackgroundJobClient _backgroundJobs = Substitute.For<IBackgroundJobClient>();
    private readonly IBadgeResetApiClient _badgeResetApiClient = Substitute.For<IBadgeResetApiClient>();
    private readonly ILoginEventService _loginEventService = Substitute.For<ILoginEventService>();

    private StudentController NewController(string? keycloakUserId)
    {
        var userProfileCache = new UserProfileCacheService(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())),
            Substitute.For<ILogger<UserProfileCacheService>>());

        var studentResetJob = new StudentResetJob(_db.NewContext(), _badgeResetApiClient);

        var controller = new StudentController(
            _minio,
            _studentService,
            userProfileCache,
            Options.Create(new KeycloakSettings()),
            _keycloakService,
            _backgroundJobs,
            studentResetJob,
            _loginEventService);

        var identity = keycloakUserId is null
            ? new ClaimsIdentity()
            : new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, keycloakUserId) }, authenticationType: "TestAuth");

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(identity) }
        };

        return controller;
    }

    [Fact]
    public void GetMyLastLogin_HasAuthorizeAttributeRestrictingToStudentRole()
    {
        var method = typeof(StudentController).GetMethod(nameof(StudentController.GetMyLastLogin));

        var attribute = method!.GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
            .Cast<AuthorizeAttribute>()
            .SingleOrDefault();

        attribute.ShouldNotBeNull();
        attribute!.Roles.ShouldBe("Student");
    }

    [Fact]
    public async Task GetMyLastLogin_NoKeycloakIdClaim_ReturnsUnauthorized()
    {
        var controller = NewController(keycloakUserId: null);

        var result = await controller.GetMyLastLogin(default);

        result.ShouldBeOfType<UnauthorizedResult>();
        await _loginEventService.DidNotReceive().GetPreviousSuccessfulLoginAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetMyLastLogin_PreviousLoginExists_ReturnsOkWithItsOccurredAtUtc()
    {
        var previous = new LoginEvent
        {
            KeycloakUserId = "kc-1",
            Role = "Student",
            Success = true,
            OccurredAtUtc = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc)
        };
        _loginEventService.GetPreviousSuccessfulLoginAsync("kc-1", Arg.Any<CancellationToken>()).Returns(previous);

        var controller = NewController("kc-1");

        var result = await controller.GetMyLastLogin(default);

        var dto = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<LastLoginDto>();
        dto.LastLoginAtUtc.ShouldBe(previous.OccurredAtUtc);
    }

    [Fact]
    public async Task GetMyLastLogin_NoPreviousLogin_ReturnsOkWithNullInsteadOfError()
    {
        _loginEventService.GetPreviousSuccessfulLoginAsync("kc-1", Arg.Any<CancellationToken>()).Returns((LoginEvent?)null);

        var controller = NewController("kc-1");

        var result = await controller.GetMyLastLogin(default);

        var dto = result.ShouldBeOfType<OkObjectResult>().Value.ShouldBeOfType<LastLoginDto>();
        dto.LastLoginAtUtc.ShouldBeNull();
    }

    public void Dispose() => _db.Dispose();
}
