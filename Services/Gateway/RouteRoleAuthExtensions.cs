using Microsoft.Extensions.Logging;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

/// <summary>
/// Issue #366: Ocelot'un RouteClaimsRequirement'ı düz claim değeri eşler; Keycloak rolleri ise
/// <c>realm_access.roles</c> iç içe JSON claim'inde gelir. Bu yüzden rol kontrolü Ocelot'tan ÖNCE
/// çalışan bu middleware'de yapılır: ilgili yol öneki için Bearer doğrulanır (yoksa 401 + challenge)
/// ve token'daki realm rollerinden en az biri izinli değilse 403 döner.
/// Ocelot route'unda da <c>AuthenticationOptions</c> kalır (derinlemesine savunma).
/// </summary>
public static class RouteRoleAuthExtensions
{
    /// <summary>PathPrefix altinda: AllowlistedSegments (tam tek segment) -> AllowlistedRoles; GERI KALAN HER SEY -> FallbackRoles.</summary>
    public sealed record RoleRestrictedPrefix(
        string PathPrefix, string[] FallbackRoles, string[] AllowlistedSegments, string[] AllowlistedRoles);

    // question-detector (allowlist): UI yalniz /predict, /headerlist, /read-qr (salt-okunur cikarim) kullanir ->
    // Teacher+Admin. send-to-fix* (diske yazar), docs, bilinmeyen/kodlanmis ekler dahil GERI KALAN HER SEY yalniz Admin
    // (Teacher realm rolu kayitta onaydan once verilir ve askiya almada kalir).
    public static readonly RoleRestrictedPrefix[] Default =
    [
        new("/question-detector-dev", ["Admin"], ["predict", "headerlist", "read-qr"], ["Teacher", "Admin"]),
    ];

    public static IApplicationBuilder UseRouteRoleAuth(
        this IApplicationBuilder app,
        IEnumerable<RoleRestrictedPrefix>? rules = null,
        string scheme = "Bearer")
    {
        var list = (rules ?? Default).ToArray();
        return app.Use(async (context, next) =>
        {
            // "//" birlestirilir: "/question-detector-dev//send-to-fix" genel kurala dusup Admin kuralini atlamasin.
            var normalized = new PathString(CollapseSlashes(context.Request.Path.Value ?? ""));
            var rule = list.FirstOrDefault(r => normalized.StartsWithSegments(r.PathPrefix));
            if (rule is null || HttpMethods.IsOptions(context.Request.Method))
            {
                await next();
                return;
            }

            var result = await context.AuthenticateAsync(scheme);
            if (!result.Succeeded || result.Principal is null)
            {
                await context.ChallengeAsync(scheme);
                return;
            }

            context.User = result.Principal;
            var roles = ExtractRealmRoles(result.Principal,
                context.RequestServices.GetService<ILoggerFactory>()?.CreateLogger("Gateway.RouteRoleAuth"));
            // Kimligi dogrulanmis istekte supheli yol karakterleri (?, #, ;, bosluk, kontrol, %, ) -> 400.
            var decoded = context.Request.Path.Value ?? "";
            if (decoded.Any(c => c is '?' or '#' or ';' or '%' or '\\' || char.IsWhiteSpace(c) || char.IsControl(c)))
            {
                context.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            normalized.StartsWithSegments(rule.PathPrefix, out var remainder);
            var segment = remainder.Value?.Trim('/') ?? "";
            var allowed = rule.AllowlistedSegments.Contains(segment, StringComparer.OrdinalIgnoreCase)
                ? rule.AllowlistedRoles
                : rule.FallbackRoles;
            if (!allowed.Any(a => roles.Contains(a, StringComparer.OrdinalIgnoreCase)))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next();
        });
    }

    private static string CollapseSlashes(string p)
    {
        while (p.Contains("//")) p = p.Replace("//", "/");
        return p;
    }

    public static IReadOnlyList<string> ExtractRealmRoles(System.Security.Claims.ClaimsPrincipal principal, ILogger? logger = null)
    {
        var roles = new List<string>();
        var realmAccess = principal.FindFirst("realm_access")?.Value;
        if (!string.IsNullOrWhiteSpace(realmAccess))
        {
            try
            {
                using var doc = JsonDocument.Parse(realmAccess);
                if (doc.RootElement.TryGetProperty("roles", out var arr) && arr.ValueKind == JsonValueKind.Array)
                {
                    roles.AddRange(arr.EnumerateArray()
                        .Where(e => e.ValueKind == JsonValueKind.String)
                        .Select(e => e.GetString()!));
                }
            }
            catch (JsonException ex)
            {
                logger?.LogDebug(ex, "realm_access claim is not valid JSON; treating as no roles");
            }
        }
        return roles;
    }
}
