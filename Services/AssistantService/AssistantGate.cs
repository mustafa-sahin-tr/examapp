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
/// Exception (#507): <c>GET /api/assistant/status</c> passes for any signed-in user and answers <c>enabled: false</c> itself,
/// so the panel can ask on every page without a 403 / 503 in the browser console.
/// </summary>
public static class AssistantGate
{
    public const string PathPrefix = "/api/assistant";
    public const string StatusPath = PathPrefix + "/status";

    /// <summary>Error code of a closed gate (same codes the gate answers with); null when allowed.</summary>
    public static string? Code(AssistantGateResult result) => result switch
    {
        AssistantGateResult.Disabled => "AssistantDisabled",
        AssistantGateResult.RoleNotEnabled => "AssistantRoleNotEnabled",
        AssistantGateResult.Unauthenticated => "Unauthenticated",
        _ => null,
    };

    public static bool ApiKeyConfigured(IConfiguration configuration) =>
        !string.IsNullOrWhiteSpace(configuration["Anthropic:ApiKey"]);

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
        var result = Evaluate(context.User, options, ApiKeyConfigured(context.RequestServices.GetRequiredService<IConfiguration>()));
        if (result != AssistantGateResult.Unauthenticated && HttpMethods.IsGet(context.Request.Method)
            && context.Request.Path.Equals(StatusPath, StringComparison.OrdinalIgnoreCase))
        {
            await next();
            return;
        }
        switch (result)
        {
            case AssistantGateResult.Allowed:
                await next();
                return;
            case AssistantGateResult.Unauthenticated:
                await context.ChallengeAsync();
                return;
            case AssistantGateResult.Disabled:
                await Problem(context, StatusCodes.Status503ServiceUnavailable, Code(result)!);
                return;
            default:
                await Problem(context, StatusCodes.Status403Forbidden, Code(result)!);
                return;
        }
    });

    private static Task Problem(HttpContext context, int status, string code)
    {
        context.Response.StatusCode = status;
        return context.Response.WriteAsJsonAsync(new { code });
    }
}
