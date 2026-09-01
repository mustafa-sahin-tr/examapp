using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.Extensions.Configuration;

namespace ExamApp.Api.Helpers;

/// <summary>
/// Single place that decides whether a caller is a trusted service (as opposed to
/// an end user). Replaces the scattered <c>preferred_username == "exam-admin"</c>
/// string checks.
///
/// Checks, in order:
///  1. realm role <c>exam-service</c> — the target state; assign it to the
///     service account instead of relying on a client-id match.
///  2. <c>azp</c> / <c>client_id</c> claim in the configured allow-list
///     (<c>Keycloak:ServiceClients</c>, default <c>exam-admin</c>). This is the
///     correct claim for a client-credentials token and works today.
///  3. legacy <c>preferred_username == "exam-admin"</c> — kept only so nothing
///     breaks mid-migration; remove once (1) is in place.
/// </summary>
public static class ServicePrincipal
{
    public const string ServiceRole = "exam-service";
    private const string LegacyServiceUsername = "exam-admin";

    public static bool IsService(ClaimsPrincipal? user, IConfiguration configuration)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return false;

        if (user.IsInRole(ServiceRole))
            return true;

        var allowedClients = configuration
            .GetSection("Keycloak:ServiceClients")
            .Get<string[]>() ?? new[] { LegacyServiceUsername };

        var azp = user.FindFirstValue("azp") ?? user.FindFirstValue("client_id");
        if (!string.IsNullOrEmpty(azp) &&
            allowedClients.Any(c => c.Equals(azp, StringComparison.OrdinalIgnoreCase)))
            return true;

        var preferredUsername = user.FindFirstValue("preferred_username");
        return preferredUsername?.Equals(LegacyServiceUsername, StringComparison.OrdinalIgnoreCase) == true
            || preferredUsername?.Equals($"service-account-{LegacyServiceUsername}", StringComparison.OrdinalIgnoreCase) == true;
    }
}
