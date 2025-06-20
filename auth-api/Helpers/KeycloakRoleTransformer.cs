using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

public class KeycloakRoleTransformer : IClaimsTransformation
{
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        var identity = (ClaimsIdentity)principal.Identity!;
        var realmRolesClaim = principal.FindFirst("realm_access");

        if (realmRolesClaim != null && !string.IsNullOrWhiteSpace(realmRolesClaim.Value))
        {
            var parsed = JsonDocument.Parse(realmRolesClaim.Value);
            if (parsed.RootElement.TryGetProperty("roles", out var rolesElement))
            {
                foreach (var role in rolesElement.EnumerateArray())
                {
                    var roleName = role.GetString();
                    if (!string.IsNullOrEmpty(roleName) && !identity.HasClaim(ClaimTypes.Role, roleName))
                    {
                        identity.AddClaim(new Claim(ClaimTypes.Role, roleName));
                    }
                }
            }
        }

        return Task.FromResult(principal);
    }
}
