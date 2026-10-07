using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;

/// <summary>
/// Issue #368: giriş uçlarına IP bazlı rate limit (anti-spray / flood). Kullanıcı bazlı brute-force
/// kontrolü birincil olarak Keycloak realm kilidindedir; bu limit yüksek tutulur (varsayılan 100/dk/IP,
/// okul NAT'ı arkasında çok kullanıcı tek IP görünebilir — <c>RateLimiting:LoginEntry:PermitLimit</c> ile ayarlanır).
///
/// Neden Ocelot RateLimitOptions değil: istemciyi <c>ClientId</c> başlığıyla tanımlar (IP değil, sahtelenebilir),
/// gövdeye (grant_type=refresh_token) bakıp muaf tutamaz ve wildcard route'ları path son ekine göre ayıramaz.
///
/// Issue #419 review: veli davet kodu denemesi (POST /api/exam/parent-links/redeem) da aynı IP kovasından düşer — kod
/// tahmini çok hesapla dağıtılsa bile tek IP'den gelen deneme hızı sınırlı kalır (hesap başına limitler exam API'de).
///
/// Eşleme TERS çevrilmiştir: /realms/** ve /auth/realms/** altındaki HER POST ile /api/auth/login ve /token sayılır;
/// yalnız açık allowlist muaftır: token uçlarında grant_type=refresh_token ve login-actions/restart.
/// Path önce kanonikleştirilir (segment başına ';...' matrix parametreleri atılır, %2F/%5C ayraç sayılır,
/// '.'/'..' çözülür, küçük harfe çevrilir).
/// </summary>
public static class LoginRateLimitExtensions
{
    public const int DefaultPermitLimit = 100;

    /// <summary>Issue #419: veli davet kodu redeem ucu (gateway yolu, kanonik biçim).</summary>
    public const string ParentInviteRedeemPath = "/api/exam/parent-links/redeem";
    public const int DefaultWindowSeconds = 60;
    private const int MaxRefreshProbeBytes = 16 * 1024;

    /// <summary>
    /// Güvenilir proxy (ör. prod'da Caddy) tanımlıysa gerçek istemci IP'sini X-Forwarded-For'dan çözer.
    /// <c>ForwardedHeaders:KnownProxies</c> (IP listesi) / <c>ForwardedHeaders:KnownNetworks</c> (CIDR listesi);
    /// ikisi de boşsa hiçbir şey yapılmaz (RemoteIpAddress olduğu gibi kalır, başlığa güvenilmez).
    /// </summary>
    public static IApplicationBuilder UseTrustedForwardedFor(this IApplicationBuilder app, IConfiguration configuration)
    {
        var proxies = configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
        var networks = configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [];
        if (proxies.Length == 0 && networks.Length == 0) return app;

        var options = new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor, ForwardLimit = 1 };
        options.KnownProxies.Clear();
        options.KnownIPNetworks.Clear();
        foreach (var p in proxies)
        {
            var ip = IPAddress.Parse(p.Trim());
            if (ip.Equals(IPAddress.Any) || ip.Equals(IPAddress.IPv6Any))
                throw new InvalidOperationException("ForwardedHeaders:KnownProxies must not contain an unspecified address (trusts nobody meaningfully / invalid).");
            options.KnownProxies.Add(ip);
        }
        foreach (var n in networks)
        {
            var net = System.Net.IPNetwork.Parse(n.Trim());
            if (net.PrefixLength == 0)
                throw new InvalidOperationException("ForwardedHeaders:KnownNetworks must not contain 0.0.0.0/0 or ::/0 (would trust every client's X-Forwarded-For).");
            options.KnownIPNetworks.Add(net);
        }
        return app.UseForwardedHeaders(options);
    }

    public static IApplicationBuilder UseLoginRateLimit(this IApplicationBuilder app, IConfiguration configuration)
    {
        var permitLimit = configuration.GetValue("RateLimiting:LoginEntry:PermitLimit", DefaultPermitLimit);
        var windowSeconds = configuration.GetValue("RateLimiting:LoginEntry:WindowSeconds", DefaultWindowSeconds);
        if (permitLimit < 1) throw new InvalidOperationException("RateLimiting:LoginEntry:PermitLimit must be >= 1.");
        if (windowSeconds < 1) throw new InvalidOperationException("RateLimiting:LoginEntry:WindowSeconds must be >= 1.");

        var limiter = PartitionedRateLimiter.Create<string, string>(ip =>
            RateLimitPartition.GetFixedWindowLimiter(ip, _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permitLimit,
                Window = TimeSpan.FromSeconds(windowSeconds),
                QueueLimit = 0,
                AutoReplenishment = true
            }));

        app.ApplicationServices.GetService<IHostApplicationLifetime>()?.ApplicationStopping.Register(limiter.Dispose);

        return app.Use(async (context, next) =>
        {
            if (!await IsLimitedLoginRequestAsync(context.Request, context.RequestAborted))
            {
                await next();
                return;
            }

            using var lease = limiter.AttemptAcquire(ClientKey(context.Connection.RemoteIpAddress));
            if (lease.IsAcquired)
            {
                await next();
                return;
            }

            var retryAfter = lease.TryGetMetadata(MetadataName.RetryAfter, out var ra)
                ? Math.Max(1, (int)Math.Ceiling(ra.TotalSeconds))
                : windowSeconds;
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            context.Response.Headers.RetryAfter = retryAfter.ToString();
            await context.Response.WriteAsync("Too many authentication attempts. Please try again later.");
        });
    }

    /// <summary>IPv4-mapped IPv6 → IPv4; IPv6 /64 önekine indirgenir (tek ev/okul tüm /64'ü kontrol eder).</summary>
    public static string ClientKey(IPAddress? ip)
    {
        if (ip is null) return "unknown";
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetworkV6) return ip.ToString();

        var bytes = ip.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes).ToString() + "/64";
    }

    /// <summary>Kanonik path: küçük harf, ';' matrix parametresiz, %2F/%5C ayraç, '.'/'..' çözülmüş, tekrarlı '/' yok.</summary>
    public static string Canonicalize(string? rawPath)
    {
        var path = (rawPath ?? string.Empty)
            .Replace("%2f", "/", StringComparison.OrdinalIgnoreCase)
            .Replace("%5c", "/", StringComparison.OrdinalIgnoreCase)
            .Replace('\\', '/');

        var stack = new List<string>();
        foreach (var raw in path.Split('/'))
        {
            var semi = raw.IndexOf(';');
            var seg = (semi >= 0 ? raw[..semi] : raw).ToLowerInvariant();
            if (seg.Length == 0 || seg == ".") continue;
            if (seg == "..") { if (stack.Count > 0) stack.RemoveAt(stack.Count - 1); continue; }
            stack.Add(seg);
        }
        return "/" + string.Join('/', stack);
    }

    /// <summary>
    /// Issue #419 re-review: redeem eşleşmesi kodlamaya dayanıklı — path, değişmeyene kadar (en fazla 5 tur) tekrar tekrar
    /// percent-decode edilir, sonra kanonikleştirilir; <c>.../parent-links/redeem</c> ile BİTEN her yol sayılır (çift kodlanmış
    /// <c>%252F</c>, <c>%2572edeem</c> ya da ön ekli varyantlar kovayı atlayamaz).
    /// </summary>
    public static bool IsParentInviteRedeem(string? rawPath)
    {
        var path = rawPath ?? string.Empty;
        for (var i = 0; i < 5; i++)
        {
            var decoded = Uri.UnescapeDataString(path);
            if (decoded == path) break;
            path = decoded;
        }
        var canonical = Canonicalize(path);
        return canonical.EndsWith("/parent-links/redeem", StringComparison.Ordinal);
    }

    public static bool ContainsTraversal(string? rawPath)
    {
        var path = (rawPath ?? string.Empty)
            .Replace("%2f", "/", StringComparison.OrdinalIgnoreCase)
            .Replace("%5c", "/", StringComparison.OrdinalIgnoreCase)
            .Replace('\\', '/');
        foreach (var raw in path.Split('/'))
        {
            var semi = raw.IndexOf(';');
            if ((semi >= 0 ? raw[..semi] : raw) == "..") return true;
        }
        return false;
    }

    private static async Task<bool> IsLimitedLoginRequestAsync(HttpRequest request, CancellationToken ct)
    {
        if (!HttpMethods.IsPost(request.Method)) return false;

        // '..' (';' temizlenip %2F/%5C ayrıldıktan sonra bile) meşru trafikte yoktur: muafiyetleri atla, say.
        if (ContainsTraversal(request.Path.Value)) return true;

        var path = Canonicalize(request.Path.Value);

        if (path == "/api/auth/login") return true;
        if (IsParentInviteRedeem(request.Path.Value)) return true;
        if (path == "/token") return !await IsRefreshGrantAsync(request, ct);

        if (!path.StartsWith("/realms/", StringComparison.Ordinal) && !path.StartsWith("/auth/realms/", StringComparison.Ordinal))
            return false;

        // Allowlist: akışı yeniden başlatan ve parola denemesi taşımayan uç.
        if (path.EndsWith("/login-actions/restart", StringComparison.Ordinal)) return false;
        if (path.EndsWith("/protocol/openid-connect/token", StringComparison.Ordinal))
            return !await IsRefreshGrantAsync(request, ct);

        return true;
    }

    // Refresh parola denemesi değildir. Gövde büyük/bilinmeyen uzunlukta/bozuksa fail-closed: SAYILIR.
    // Gövdeyi Ocelot downstream'e iletebilsin diye başa sarar.
    private static async Task<bool> IsRefreshGrantAsync(HttpRequest request, CancellationToken ct)
    {
        if (!request.HasFormContentType || request.ContentLength is null or > MaxRefreshProbeBytes) return false;
        request.EnableBuffering();
        try
        {
            var form = await request.ReadFormAsync(ct);
            return form["grant_type"] == "refresh_token";
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            return false;
        }
        finally
        {
            request.Body.Position = 0;
        }
    }
}
