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
    /// <summary>Bir segment grubu: <see cref="Segments"/> (tam tek segment, buyuk/kucuk harf duyarsiz) -> <see cref="Roles"/>.</summary>
    public sealed record SegmentRoles(string[] Segments, string[] Roles);

    /// <summary>
    /// PathPrefix altinda: kalan yol TAM TEK segment olarak <see cref="SegmentGroups"/> icinde ilk eslesen grubun
    /// rollerine tabidir; hicbir gruba uymayan HER SEY (docs, bilinmeyen, cok segmentli yol) -> FallbackRoles.
    /// </summary>
    public sealed record RoleRestrictedPrefix(string PathPrefix, string[] FallbackRoles, SegmentRoles[] SegmentGroups);

    // question-detector (allowlist):
    // - /predict, /headerlist, /read-qr (UI'nin kullandigi salt-okunur cikarim) -> Teacher+Admin.
    // - /send-to-fix (diske egitim verisi yazar) -> Admin + QuestionTrainer (soru girerken model egitim verisi
    //   gonderebilen yetkili ogretmen; ayri realm rolu, cunku Teacher rolu kayitta onaydan once verilir ve askiya
    //   almada kalir). QuestionTrainer okuma uclari icin eklenmez: yetkili ogretmen zaten Teacher rolunu de tasir.
    // - /labeled-pages (egitilmis sayfa id'leri; isaretleme modu tekrar getirmesin) -> send-to-fix ile ayni roller.
    // - bilinmeyen ya da kodlanmis yol ekleri, send-to-fix-* benzeri tureyenler, docs ve openapi.json dahil
    //   GERI KALAN HER SEY yalniz Admin.
    // exam API isaretleme kaynaklari (/api/exam/labeling/*, Claude on-etiketleme dahil) -> send-to-fix ile ayni roller
    // (derinlemesine savunma; backend ayrica rol + onayli ogretmen kontrolu yapar).
    public static readonly RoleRestrictedPrefix[] Default =
    [
        new("/question-detector-dev", ["Admin"],
        [
            new(["predict", "headerlist", "read-qr"], ["Teacher", "Admin"]),
            new(["send-to-fix", "labeled-pages"], ["Admin", "QuestionTrainer"]),
        ]),
        new("/api/exam/labeling", ["Admin", "QuestionTrainer"], []),
    ];

    /// <summary>
    /// Issue #580: cok segmentli tek bir uca rol kurali. <see cref="Segments"/> yolun TUM segmentleri (sayisi esit, buyuk/kucuk harf
    /// duyarsiz); <c>*</c> herhangi bir bos olmayan tek segment. Onek kurallarindan sonra denenir.
    /// </summary>
    public sealed record RoleRestrictedPattern(string[] Segments, string[] Roles)
    {
        public bool Matches(string normalizedPath)
        {
            var parts = normalizedPath.Trim('/').Split('/');
            if (parts.Length != Segments.Length)
                return false;
            for (var i = 0; i < parts.Length; i++)
            {
                if (parts[i].Length == 0)
                    return false;
                if (Segments[i] != "*" && !string.Equals(Segments[i], parts[i], StringComparison.OrdinalIgnoreCase))
                    return false;
            }
            return true;
        }
    }

    // Issue #580: soru cozumu onayi (AI / cevap anahtari) -> yalniz Admin + QuestionTrainer (backend ayrica rol + sahiplik denetler).
    public static readonly RoleRestrictedPattern[] DefaultPatterns =
    [
        new(["api", "exam", "questions", "*", "solution", "approve"], ["Admin", "QuestionTrainer"]),
    ];

    public static IApplicationBuilder UseRouteRoleAuth(
        this IApplicationBuilder app,
        IEnumerable<RoleRestrictedPrefix>? rules = null,
        string scheme = "Bearer",
        IEnumerable<RoleRestrictedPattern>? patterns = null)
    {
        var list = (rules ?? Default).ToArray();
        var patternList = (patterns ?? DefaultPatterns).ToArray();
        return app.Use(async (context, next) =>
        {
            // "//" birlestirilir: "/question-detector-dev//send-to-fix" genel kurala dusup Admin kuralini atlamasin.
            var normalized = new PathString(CollapseSlashes(context.Request.Path.Value ?? ""));
            var rule = list.FirstOrDefault(r => normalized.StartsWithSegments(r.PathPrefix));
            var pattern = rule is null ? patternList.FirstOrDefault(p => p.Matches(normalized.Value ?? "")) : null;
            if ((rule is null && pattern is null) || HttpMethods.IsOptions(context.Request.Method))
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

            string[] allowed;
            if (rule is not null)
            {
                normalized.StartsWithSegments(rule.PathPrefix, out var remainder);
                var segment = remainder.Value?.Trim('/') ?? "";
                allowed = rule.SegmentGroups
                    .FirstOrDefault(g => g.Segments.Contains(segment, StringComparer.OrdinalIgnoreCase))?.Roles
                    ?? rule.FallbackRoles;
            }
            else
            {
                allowed = pattern!.Roles;
            }
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
