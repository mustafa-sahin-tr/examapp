using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace BadgeService.Security;

/// <summary>
/// Intentionally duplicated from <c>api/ExamApp.Api/Helpers/KeycloakRoleTransformer.cs</c>.
///
/// This logic used to live in <c>ExamApp.Foundation</c> so both <c>ExamApp.Api</c> and
/// <c>BadgeService</c> could share it, but that required adding a
/// <c>FrameworkReference Include="Microsoft.AspNetCore.App"</c> to Foundation to make
/// <see cref="IClaimsTransformation"/> compile. Foundation is also referenced by non-web
/// projects (e.g. the OutboxPublisher worker service, Foundation's own test project), and that
/// FrameworkReference would have transitively pulled the entire ASP.NET Core shared framework
/// into them. BadgeService is an ASP.NET Core app (Sdk.Web — it hosts a SignalR hub and JWT
/// bearer auth) so it compiles this natively; keeping a small duplicate here avoids polluting
/// Foundation for the sake of one class.
///
/// Projects Keycloak's <c>realm_access.roles</c> array onto standard
/// <see cref="ClaimTypes.Role"/> claims so <c>[Authorize(Roles = "...")]</c> / <c>User.IsInRole(...)</c> works.
///
/// Runs on every authenticated request, so it must be total: a malformed or
/// unexpected token shape is ignored, never thrown.
/// </summary>
public class KeycloakRoleTransformer : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity identity)
            return Task.FromResult(principal);

        var realmAccess = principal.FindFirst("realm_access")?.Value;
        if (string.IsNullOrWhiteSpace(realmAccess))
            return Task.FromResult(principal);

        try
        {
            using var parsed = JsonDocument.Parse(realmAccess);
            if (!parsed.RootElement.TryGetProperty("roles", out var roles) ||
                roles.ValueKind != JsonValueKind.Array)
            {
                return Task.FromResult(principal);
            }

            foreach (var role in roles.EnumerateArray())
            {
                if (role.ValueKind != JsonValueKind.String)
                    continue;

                var name = role.GetString();
                if (!string.IsNullOrEmpty(name) && !identity.HasClaim(ClaimTypes.Role, name))
                    identity.AddClaim(new Claim(ClaimTypes.Role, name));
            }
        }
        catch (JsonException)
        {
            // Not our token to reason about — leave it untouched.
        }

        return Task.FromResult(principal);
    }
}
