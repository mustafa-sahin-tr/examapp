using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

namespace ExamApp.Api.Helpers;

/// <summary>
/// Brute-force / credential-stuffing koruması için kimlik doğrulanmamış auth uçlarına
/// (<c>POST /api/auth/login</c>, <c>POST /api/auth/exchange</c>, <c>POST /api/auth/register</c>) uygulanan IP bazlı rate limit.
///
/// Neden gateway'de değil: <c>ocelot.json</c>'daki <c>/api/auth/{everything}</c> tek bir wildcard
/// route — login/exchange'i register/logout/user-profile'dan ayırmıyor. Buradaki isimli policy
/// yalnızca <c>[EnableRateLimiting(AuthAttemptsPolicy)]</c> taşıyan action'lara uygulanır.
///
/// Limit değerleri <c>appsettings.json</c> → <c>RateLimiting:AuthAttempts</c> bölümünden okunur;
/// yoksa <see cref="DefaultPermitLimit"/> / <see cref="DefaultWindowSeconds"/> geçerlidir.
/// </summary>
public static class AuthRateLimiting
{
    public const string AuthAttemptsPolicy = "auth-attempts";

    /// <summary>Pencere başına izin verilen deneme sayısı (varsayılan 10).</summary>
    public const int DefaultPermitLimit = 10;

    /// <summary>Sabit pencere uzunluğu, saniye (varsayılan 60).</summary>
    public const int DefaultWindowSeconds = 60;

    private const string RejectedBody = "Too many authentication attempts. Please try again later.";

    public static IServiceCollection AddAuthRateLimiting(this IServiceCollection services, IConfiguration configuration)
    {
        var permitLimit = configuration.GetValue("RateLimiting:AuthAttempts:PermitLimit", DefaultPermitLimit);
        var windowSeconds = configuration.GetValue("RateLimiting:AuthAttempts:WindowSeconds", DefaultWindowSeconds);

        if (permitLimit < 1)
            throw new InvalidOperationException("RateLimiting:AuthAttempts:PermitLimit must be >= 1.");
        if (windowSeconds < 1)
            throw new InvalidOperationException("RateLimiting:AuthAttempts:WindowSeconds must be >= 1.");

        var window = TimeSpan.FromSeconds(windowSeconds);

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Projenin genel hata biçimi düz metin body + status code (bkz. ExceptionHandlingMiddleware);
            // 429 da aynı biçimde döner. Retry-After saniye cinsinden, limiter'ın kendi metadata'sından.
            options.OnRejected = async (context, cancellationToken) =>
            {
                var response = context.HttpContext.Response;
                response.StatusCode = StatusCodes.Status429TooManyRequests;

                var retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var metadata)
                    ? Math.Max(1, (int)Math.Ceiling(metadata.TotalSeconds))
                    : windowSeconds;
                response.Headers.RetryAfter = retryAfter.ToString();

                await response.WriteAsync(RejectedBody, cancellationToken);
            };

            options.AddPolicy(AuthAttemptsPolicy, httpContext =>
                RateLimitPartition.GetFixedWindowLimiter(
                    partitionKey: ClientKey(httpContext),
                    factory: _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = permitLimit,
                        Window = window,
                        QueueLimit = 0, // fazlası beklemez, anında 429
                        AutoReplenishment = true
                    }));
        });

        return services;
    }

    /// <summary>
    /// Gateway arkasında gerçek istemci IP'sinin <c>RemoteIpAddress</c>'e yansıması için
    /// <c>X-Forwarded-For</c> işleme. Ocelot gateway (<c>Services/Gateway/Program.cs</c>) bu başlığı
    /// kendi gördüğü uzak adresle <b>üzerine yazar</b> (append etmez); bu yüzden <c>ForwardLimit=1</c>
    /// yeterlidir ve gateway üzerinden gelen bir istemci başlığı sahteleyemez.
    ///
    /// Güvenilen kaynaklar iki anahtarla pinlenir (issue #100):
    /// <list type="bullet">
    /// <item><c>ForwardedHeaders:KnownNetworks</c> — CIDR dizisi (ör. compose/k8s pod ağı
    /// <c>172.28.0.0/16</c>). Gateway IP'si container/pod yeniden yaratıldıkça değiştiğinden
    /// production'da tercih edilen yol budur.</item>
    /// <item><c>ForwardedHeaders:KnownProxies</c> — tekil IP dizisi (sabit IP'li proxy için).</item>
    /// </list>
    /// İkisinden biri doluysa <c>X-Forwarded-For</c> YALNIZCA bu kaynaklardan gelen isteklerde
    /// uygulanır; başka bir peer'ın gönderdiği başlık yok sayılır (rate limit gerçek peer IP'sine düşer).
    ///
    /// İkisi de boşsa framework'ün "yalnızca loopback" varsayılanı KALDIRILIR ve başlık her kaynaktan
    /// kabul edilir — çünkü bu topolojide auth-api'nin tek ingress'i gateway'dir ve loopback
    /// varsayılanı container ağında başlığı yok sayıp herkesi gateway IP'sinde tek kovaya düşürür.
    /// Bu varsayılan yalnızca auth-api'ye gateway dışından erişilemediğinde güvenlidir: Aspire'da
    /// <c>Kestrel:BindLoopbackOnly=true</c>, docker-compose'da host portu <c>127.0.0.1</c>'e bağlı
    /// (bkz. <see cref="KestrelBinding"/>). auth-api başka bir ağdan erişilebilir olacaksa
    /// KnownNetworks/KnownProxies mutlaka doldurulmalıdır.
    ///
    /// Açılış kontrolleri (issue #100 review): hatalı IP/CIDR ve <c>/0</c> prefix'li ağ (tüm adres uzayı —
    /// pinlememe ile eşdeğer) reddedilir. <paramref name="environment"/> Production ise ve iki liste de
    /// boşsa açılış durdurulur; diğer ortamlarda Program.cs uyarı loglar
    /// (bkz. <see cref="IsForwardedHeadersTrustOpen"/>).
    /// </summary>
    public static IServiceCollection AddAuthForwardedHeaders(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment? environment = null)
    {
        // Değerler lambda içinde değil burada parse edilir: hatalı değer ilk istekte değil açılışta patlasın
        // (sessizce "herkese güven" moduna düşmesin).
        var (knownProxies, knownNetworks) = ParseForwardedHeadersTrust(configuration);

        if (environment?.IsProduction() == true && knownProxies.Count == 0 && knownNetworks.Count == 0)
        {
            throw new InvalidOperationException(
                $"{KnownNetworksKey} or {KnownProxiesKey} must be configured in Production: with both empty, " +
                "X-Forwarded-For is trusted from any peer and the auth IP rate limit can be bypassed.");
        }

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            // Yalnızca XForwardedFor: Proto/Host'u devralmak Request.Scheme'i değiştirip
            // HTTPS yönlendirme/cookie davranışını etkiler; bu değişikliğin kapsamı dışında.
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            options.ForwardLimit = 1;

            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();

            foreach (var ip in knownProxies)
                options.KnownProxies.Add(ip);

            foreach (var network in knownNetworks)
                options.KnownIPNetworks.Add(network);
        });

        return services;
    }

    public const string KnownProxiesKey = "ForwardedHeaders:KnownProxies";
    public const string KnownNetworksKey = "ForwardedHeaders:KnownNetworks";

    /// <summary>
    /// KnownProxies ve KnownNetworks ikisi de boşsa <c>true</c>: X-Forwarded-For her kaynaktan kabul edilir.
    /// Production dışı ortamlarda Program.cs açılışta bu durumu uyarı olarak loglar.
    /// </summary>
    public static bool IsForwardedHeadersTrustOpen(IConfiguration configuration)
    {
        var (knownProxies, knownNetworks) = ParseForwardedHeadersTrust(configuration);
        return knownProxies.Count == 0 && knownNetworks.Count == 0;
    }

    private static (List<IPAddress> KnownProxies, List<System.Net.IPNetwork> KnownNetworks) ParseForwardedHeadersTrust(
        IConfiguration configuration)
    {
        var rawProxies = configuration.GetSection(KnownProxiesKey).Get<string[]>() ?? [];
        var rawNetworks = configuration.GetSection(KnownNetworksKey).Get<string[]>() ?? [];

        var proxies = new List<IPAddress>(rawProxies.Length);
        foreach (var raw in rawProxies)
        {
            if (!IPAddress.TryParse(raw?.Trim(), out var ip))
                throw new InvalidOperationException($"{KnownProxiesKey} contains an invalid IP address: '{raw}'.");
            proxies.Add(ip);
        }

        var networks = new List<System.Net.IPNetwork>(rawNetworks.Length);
        foreach (var raw in rawNetworks)
        {
            if (!System.Net.IPNetwork.TryParse(raw?.Trim(), out var network))
                throw new InvalidOperationException($"{KnownNetworksKey} contains an invalid CIDR: '{raw}'.");
            if (network.PrefixLength == 0)
                throw new InvalidOperationException(
                    $"{KnownNetworksKey} must not contain a /0 network ('{raw}'): it trusts every address, which is the same as no pinning.");
            networks.Add(network);
        }

        return (proxies, networks);
    }

    private static string ClientKey(HttpContext httpContext) =>
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
