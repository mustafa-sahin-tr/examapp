using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace ExamApp.Foundation.Security;

/// <summary>
/// Shared decision point for "is this caller a trusted service" (vs. an end user),
/// used by every API that has service-to-service endpoints. Replaces the scattered
/// <c>preferred_username == "exam-admin"</c> string checks.
///
/// Checks, in order:
///  1. realm role <c>exam-service</c> — the target state; assign it to the service
///     account rather than matching a client id by string.
///  2. <c>azp</c> / <c>client_id</c> claim in <paramref name="allowedServiceClients"/>
///     (falls back to <c>exam-admin</c> when none supplied). This is the correct
///     claim for a client-credentials token and works today.
///  3. legacy <c>preferred_username</c> == <c>exam-admin</c> / <c>service-account-exam-admin</c>
///     — kept only so nothing breaks mid-migration; remove once (1) is in place.
/// </summary>
public static class ServicePrincipal
{
    public const string ServiceRole = "exam-service";
    private const string LegacyServiceClient = "exam-admin";

    public static bool IsService(ClaimsPrincipal? user, IEnumerable<string>? allowedServiceClients = null)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return false;

        if (user.IsInRole(ServiceRole))
            return true;

        var allowed = allowedServiceClients?.ToArray();
        if (allowed is null || allowed.Length == 0)
            allowed = new[] { LegacyServiceClient };

        var azp = user.FindFirst("azp")?.Value ?? user.FindFirst("client_id")?.Value;
        if (!string.IsNullOrEmpty(azp) &&
            allowed.Any(c => c.Equals(azp, StringComparison.OrdinalIgnoreCase)))
            return true;

        var preferredUsername = user.FindFirst("preferred_username")?.Value;
        return preferredUsername is not null &&
               (preferredUsername.Equals(LegacyServiceClient, StringComparison.OrdinalIgnoreCase) ||
                preferredUsername.Equals($"service-account-{LegacyServiceClient}", StringComparison.OrdinalIgnoreCase));
    }
}
