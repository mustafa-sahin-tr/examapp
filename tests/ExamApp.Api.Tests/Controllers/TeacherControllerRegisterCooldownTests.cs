using System.Security.Claims;
using ExamApp.Api.Controllers;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
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
/// issue #277 (madde 2): <c>POST api/teacher/register</c> — servis bekleme süresine takılınca (<c>TooManyRequests</c>)
/// controller 429 + <c>Retry-After</c> (saniye) + <c>{ message, retryAfterSeconds, retryAfterUtc }</c> döner ve Keycloak'a
/// (rol atama / token yenileme) HİÇ gitmez.
/// </summary>
public class TeacherControllerRegisterCooldownTests
{
    private readonly ITeacherService _teacherService = Substitute.For<ITeacherService>();
    private readonly IKeycloakService _keycloak = Substitute.For<IKeycloakService>();
    private readonly IUserProfileProvider _profiles = Substitute.For<IUserProfileProvider>();

    private TeacherController NewController(bool withRefreshCookie = true)
    {
        _profiles.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new UserProfileDto { Id = 42, KeycloakId = "kc-teacher", Role = "Teacher" });

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddSingleton(_profiles);
        services.AddSingleton<IDistributedCache>(new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())));
        services.AddSingleton<UserProfileCacheService>();
        var provider = services.BuildServiceProvider();

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "kc-teacher")], "Test")),
            RequestServices = provider
        };
        if (withRefreshCookie)
            httpContext.Request.Headers.Cookie = "refresh_token=rt";

        return new TeacherController(_teacherService, provider.GetRequiredService<UserProfileCacheService>(), _keycloak,
            Substitute.For<ILogger<TeacherController>>())
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext }
        };
    }

    private static object? Prop(object? value, string name) => value?.GetType().GetProperty(name)?.GetValue(value);

    [Fact]
    public async Task Cooldown_maps_to_429_with_retry_after_header_and_body_and_skips_keycloak()
    {
        var retryAt = DateTime.UtcNow.AddHours(3);
        _teacherService.Save(42, Arg.Any<RegisterTeacherDto>(), Arg.Any<ExamApp.Api.Helpers.UserRoleChangeRequest?>()).Returns(new TeacherRegistrationResultDto
        {
            Success = false,
            TooManyRequests = true,
            RetryAfterUtc = retryAt,
            Message = "bekleyin",
            ApprovalStatus = TeacherApprovalStatus.Rejected
        });
        var controller = NewController();

        var result = await controller.RegisterTeacher(new RegisterTeacherDto { SchoolId = 5 });

        var obj = result.ShouldBeOfType<ObjectResult>();
        obj.StatusCode.ShouldBe(StatusCodes.Status429TooManyRequests);
        Prop(obj.Value, "message").ShouldBe("bekleyin");
        Prop(obj.Value, "retryAfterUtc").ShouldBe(retryAt);
        var seconds = (int)Prop(obj.Value, "retryAfterSeconds")!;
        seconds.ShouldBeInRange(3 * 3600 - 60, 3 * 3600);

        var header = controller.HttpContext.Response.Headers.RetryAfter.ToString();
        int.Parse(header).ShouldBe(seconds);

        await _keycloak.DidNotReceiveWithAnyArgs().SetRoleAsync(default!, default);
        await _keycloak.DidNotReceiveWithAnyArgs().RefreshTokenAsync(default!);
    }

    [Fact]
    public async Task Retry_after_is_at_least_one_second_even_if_the_moment_has_just_passed()
    {
        _teacherService.Save(42, Arg.Any<RegisterTeacherDto>(), Arg.Any<ExamApp.Api.Helpers.UserRoleChangeRequest?>()).Returns(new TeacherRegistrationResultDto
        {
            Success = false, TooManyRequests = true, RetryAfterUtc = DateTime.UtcNow.AddSeconds(-5), Message = "m"
        });
        var controller = NewController();

        var result = await controller.RegisterTeacher(new RegisterTeacherDto { SchoolId = 5 });

        result.ShouldBeOfType<ObjectResult>().StatusCode.ShouldBe(429);
        controller.HttpContext.Response.Headers.RetryAfter.ToString().ShouldBe("1");
    }

    [Fact]
    public async Task Conflict_still_maps_to_409()
    {
        _teacherService.Save(42, Arg.Any<RegisterTeacherDto>(), Arg.Any<ExamApp.Api.Helpers.UserRoleChangeRequest?>()).Returns(new TeacherRegistrationResultDto
        {
            Success = false, Conflict = true, Message = "c"
        });

        var result = await NewController().RegisterTeacher(new RegisterTeacherDto { SchoolId = 5 });

        result.ShouldBeOfType<ConflictObjectResult>();
    }
}
