using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;

namespace ExamApp.Foundation.Security;

/// <summary>
/// Shared decision point for "is this caller a trusted service" (vs. an end user),
/// used by every API that has service-to-service endpoints.
///
/// Checks, in order (issue #372):
///  1. realm role <c>exam-service</c> — held only by the <c>exam-service</c> client's service
///     account (no Keycloak admin roles).
///  2. <c>azp</c> / <c>client_id</c> claim in the explicit <paramref name="allowedServiceClients"/>
///     (<c>Keycloak:ServiceClients</c>); there is no implicit default list.
/// The legacy <c>preferred_username == exam-admin</c> check and the implicit
/// <c>exam-admin</c> azp default were removed: <c>exam-admin</c> is now a Keycloak admin-REST
/// client (auth-api) and must not be accepted as a service caller.
/// </summary>
public static class ServicePrincipal
{
    public const string ServiceRole = "exam-service";

    public static bool IsService(ClaimsPrincipal? user, IEnumerable<string>? allowedServiceClients = null)
    {
        if (user?.Identity?.IsAuthenticated != true)
            return false;

        if (user.IsInRole(ServiceRole))
            return true;

        var allowed = allowedServiceClients?.ToArray();
        if (allowed is null || allowed.Length == 0)
            return false;

        var azp = user.FindFirst("azp")?.Value ?? user.FindFirst("client_id")?.Value;
        return !string.IsNullOrEmpty(azp) &&
               allowed.Any(c => c.Equals(azp, StringComparison.OrdinalIgnoreCase));
    }
}
