using System.Security.Claims;
using ExamApp.Api.Controllers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Interfaces;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #1087: plan kapısı — planda yok 403, sınır dolu 429, "yalnız planda var mı" ve geçiş.</summary>
public class RequiresEntitlementAttributeTests
{
    private const int UserId = 42;
    private readonly IEntitlementService _entitlements = Substitute.For<IEntitlementService>();

    private sealed class ProbeController : BaseController;

    private ActionExecutingContext Context(UserProfileDto? profile)
    {
        var profiles = Substitute.For<IUserProfileProvider>();
        profiles.GetAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(profile);
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "kc-1")], "TestAuth")),
            RequestServices = new ServiceCollection().AddLogging().AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
                .AddSingleton(profiles).AddSingleton(_entitlements).BuildServiceProvider(),
        };
        var controller = new ProbeController { ControllerContext = new ControllerContext { HttpContext = http } };
        var action = new ActionContext(http, new RouteData(), new ActionDescriptor());
        return new ActionExecutingContext(action, [], new Dictionary<string, object?>(), controller);
    }

    private static UserProfileDto Teacher => new() { Id = UserId, KeycloakId = "kc-1", Role = "Teacher", FullName = "T", Email = "t@t" };

    private void Returns(EntitlementCheck check) =>
        _entitlements.CheckAsync(Arg.Is<EntitlementSubject>(s => s.UserId == UserId), EntitlementFeatures.CanvasAi, Arg.Any<CancellationToken>())
            .Returns(check);

    private static async Task<bool> RunAsync(RequiresEntitlementAttribute gate, ActionExecutingContext context)
    {
        var ran = false;
        await gate.OnActionExecutionAsync(context, () =>
        {
            ran = true;
            return Task.FromResult(new ActionExecutedContext(context, [], context.Controller));
        });
        return ran;
    }

    [Fact]
    public async Task Feature_missing_from_the_plan_is_403_not_in_plan()
    {
        Returns(new EntitlementCheck(false, 0, 0, null, "teacher-basic"));
        var context = Context(Teacher);

        (await RunAsync(new RequiresEntitlementAttribute(EntitlementFeatures.CanvasAi) { InPlanOnly = true }, context)).ShouldBeFalse();

        var result = context.Result.ShouldBeOfType<ObjectResult>();
        result.StatusCode.ShouldBe(StatusCodes.Status403Forbidden);
        var body = result.Value.ShouldBeOfType<EntitlementLimitReachedDto>();
        (body.Code, body.Feature, body.Limit, body.PlanKey).ShouldBe(("entitlement.notInPlan", EntitlementFeatures.CanvasAi, (int?)0, "teacher-basic"));
    }

    [Fact]
    public async Task Used_up_limit_is_429_unless_only_the_plan_is_checked()
    {
        Returns(new EntitlementCheck(false, 300, 300, new DateTime(2026, 12, 1, 0, 0, 0, DateTimeKind.Utc), "teacher-essential"));

        var blocked = Context(Teacher);
        (await RunAsync(new RequiresEntitlementAttribute(EntitlementFeatures.CanvasAi), blocked)).ShouldBeFalse();
        var result = blocked.Result.ShouldBeOfType<ObjectResult>();
        result.StatusCode.ShouldBe(StatusCodes.Status429TooManyRequests);
        result.Value.ShouldBeOfType<EntitlementLimitReachedDto>().Code.ShouldBe("entitlement.limitReached");

        var retry = Context(Teacher);
        (await RunAsync(new RequiresEntitlementAttribute(EntitlementFeatures.CanvasAi) { InPlanOnly = true }, retry)).ShouldBeTrue();
        retry.Result.ShouldBeNull();
    }

    [Fact]
    public async Task Allowed_feature_runs_the_action_and_does_not_count()
    {
        Returns(new EntitlementCheck(true, null, 3, null, "teacher-pro"));
        var context = Context(Teacher);

        (await RunAsync(new RequiresEntitlementAttribute(EntitlementFeatures.CanvasAi), context)).ShouldBeTrue();

        await _entitlements.DidNotReceiveWithAnyArgs().RecordUsageAsync(default, default!, default!, default);
    }

    [Fact]
    public async Task Unresolved_user_is_left_to_the_action()
    {
        var context = Context(null);

        (await RunAsync(new RequiresEntitlementAttribute(EntitlementFeatures.CanvasAi), context)).ShouldBeTrue();

        await _entitlements.DidNotReceiveWithAnyArgs().CheckAsync(default!, default!, default);
    }
}
