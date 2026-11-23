using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using Microsoft.Extensions.Options;

namespace AssistantService;

public enum AssistantGateResult
{
    Allowed,
    Unauthenticated,
    Disabled,
    RoleNotEnabled,
}

/// <summary>
/// Issue #502: every <c>/api/assistant/*</c> request passes this gate after authentication. Order: no token → 401,
/// switched off or no API key → 503 <c>AssistantDisabled</c>, role not enabled → 403 <c>AssistantRoleNotEnabled</c>.
/// </summary>
public static class AssistantGate
{
    public const string PathPrefix = "/api/assistant";

    public static AssistantGateResult Evaluate(ClaimsPrincipal user, AssistantOptions options, bool apiKeyConfigured)
    {
        if (user.Identity?.IsAuthenticated != true)
            return AssistantGateResult.Unauthenticated;
        if (!options.Enabled || !apiKeyConfigured)
            return AssistantGateResult.Disabled;
        return options.Roles.Any(user.IsInRole) ? AssistantGateResult.Allowed : AssistantGateResult.RoleNotEnabled;
    }

    public static IApplicationBuilder UseAssistantGate(this IApplicationBuilder app) => app.Use(async (context, next) =>
    {
        if (!context.Request.Path.StartsWithSegments(PathPrefix))
        {
            await next();
            return;
        }

        var options = context.RequestServices.GetRequiredService<IOptionsMonitor<AssistantOptions>>().CurrentValue;
        var apiKey = context.RequestServices.GetRequiredService<IConfiguration>()["Anthropic:ApiKey"];
        var result = Evaluate(context.User, options, !string.IsNullOrWhiteSpace(apiKey));
        switch (result)
        {
            case AssistantGateResult.Allowed:
                await next();
                return;
            case AssistantGateResult.Unauthenticated:
                await context.ChallengeAsync();
                return;
            case AssistantGateResult.Disabled:
                await Problem(context, StatusCodes.Status503ServiceUnavailable, "AssistantDisabled");
                return;
            default:
                await Problem(context, StatusCodes.Status403Forbidden, "AssistantRoleNotEnabled");
                return;
        }
    });

    private static Task Problem(HttpContext context, int status, string code)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { code });
    }
}
