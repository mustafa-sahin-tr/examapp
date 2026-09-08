using System.Net;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;

namespace ExamApp.Api.Helpers;

/// <summary>
/// Brute-force / credential-stuffing koruması için kimlik doğrulanmamış auth uçlarına
/// (<c>POST /api/auth/login</c>, <c>POST /api/auth/exchange</c>) uygulanan IP bazlı rate limit.
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
    /// Güvenilen proxy listesi <c>ForwardedHeaders:KnownProxies</c> (IP dizisi) ile pinlenir.
    /// Liste boşsa framework'ün "yalnızca loopback" varsayılanı KALDIRILIR ve başlık her kaynaktan
    /// kabul edilir — çünkü bu topolojide auth-api'nin tek ingress'i gateway'dir ve loopback
    /// varsayılanı container ağında başlığı yok sayıp herkesi gateway IP'sinde tek kovaya düşürür.
    /// auth-api doğrudan dışarıya açılacaksa KnownProxies mutlaka doldurulmalıdır.
    /// </summary>
    public static IServiceCollection AddAuthForwardedHeaders(this IServiceCollection services, IConfiguration configuration)
    {
        var knownProxies = configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];

        services.Configure<ForwardedHeadersOptions>(options =>
        {
            // Yalnızca XForwardedFor: Proto/Host'u devralmak Request.Scheme'i değiştirip
            // HTTPS yönlendirme/cookie davranışını etkiler; bu değişikliğin kapsamı dışında.
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor;
            options.ForwardLimit = 1;

            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();

            foreach (var raw in knownProxies)
            {
                if (IPAddress.TryParse(raw, out var ip))
                    options.KnownProxies.Add(ip);
                else
                    throw new InvalidOperationException($"ForwardedHeaders:KnownProxies contains an invalid IP address: '{raw}'.");
            }
        });

        return services;
    }

    private static string ClientKey(HttpContext httpContext) =>
        httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
