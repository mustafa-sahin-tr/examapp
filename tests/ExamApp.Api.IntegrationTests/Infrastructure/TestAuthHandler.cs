using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.IntegrationTests.Infrastructure;

/// <summary>
/// Replaces Keycloak JWT validation in tests. The request carries identity via headers:
///   X-Test-Auth      keycloak id (subject) — presence = authenticated
///   X-Test-Username  preferred_username     (default: the subject)
///   X-Test-Azp       azp claim              (for service-to-service checks)
///   X-Test-Roles     comma-separated realm roles → emitted as a realm_access claim,
///                    which KeycloakRoleTransformer then maps to role claims.
/// </summary>
public class TestAuthHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string Scheme = "Test";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("X-Test-Auth", out var subject) || string.IsNullOrEmpty(subject))
            return Task.FromResult(AuthenticateResult.NoResult());

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, subject!),
            new("preferred_username", Request.Headers["X-Test-Username"].FirstOrDefault() ?? subject!),
        };

        var azp = Request.Headers["X-Test-Azp"].FirstOrDefault();
        if (!string.IsNullOrEmpty(azp))
            claims.Add(new Claim("azp", azp));

        var roles = Request.Headers["X-Test-Roles"].FirstOrDefault();
        if (!string.IsNullOrEmpty(roles))
        {
            var list = string.Join(",", roles.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(r => $"\"{r.Trim()}\""));
            claims.Add(new Claim("realm_access", $"{{\"roles\":[{list}]}}"));
        }

        var identity = new ClaimsIdentity(claims, Scheme, ClaimTypes.Name, ClaimTypes.Role);
        var properties = new AuthenticationProperties();

        // Üretimdeki JwtBearer (SaveToken + OnMessageReceived) taklidi: "Authorization: Bearer x" varsa o, yoksa YALNIZCA
        // whiteboard hub yolunda query string'deki token saklanır (SignalRQueryToken ile aynı kural).
        string? header = Request.Headers.Authorization;
        var savedToken = header is not null && header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)
            ? header["Bearer ".Length..].Trim()
            : ExamApp.Api.Helpers.SignalRQueryToken.Resolve(Request, ExamApp.Api.Hubs.WhiteboardHub.Path);
        if (!string.IsNullOrEmpty(savedToken))
            properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = savedToken }]);

        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), properties, Scheme);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}
