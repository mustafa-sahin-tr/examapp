using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.RateLimiting;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Helpers;

/// <summary>
/// <c>RateLimiting:AdminUserList</c> ayarları (issue #246). Varsayılan: dakikada 30 sayfa (kullanıcı başına).
/// </summary>
public sealed class AdminUserListRateLimitOptions
{
    public const string SectionName = "RateLimiting:AdminUserList";

    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 30;

    [Range(1, 86_400)]
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
/// Admin kişisel veri listelerine (<c>GET api/admin/students</c>, <c>GET api/admin/teachers</c>) KULLANICI başına
/// sabit pencere rate limit (issue #246). Amaç: çalınmış bir admin token'ı ile tüm okulların listesinin dakikalar içinde
/// çekilmesini yavaşlatmak ve sayfa başına tetiklenen auth-api/Keycloak yükünü sınırlamak.
///
/// auth-api'deki <c>AuthRateLimiting</c> (#231) deseniyle aynı: isimli policy + <c>[EnableRateLimiting]</c>,
/// 429 düz metin body + <c>Retry-After</c>. Fark: partition anahtarı IP değil Keycloak <c>sub</c> — kimliği doğrulanmış
/// uçlarda IP'yi değiştirmek kolay, token sahibini değiştirmek değil; ayrıca gateway arkasında tüm adminler tek IP'de
/// toplanabilir. İki uç TEK kovayı paylaşır (öğrenci + öğretmen sayfaları toplamda sayılır).
///
/// Pipeline'da UseAuthentication/UseAuthorization'dan SONRA çalışır: 401/403 alan istekler kovayı tüketmez ve
/// <c>User</c> doludur. Ayarlar istek anında <see cref="IOptionsMonitor{T}"/>'tan okunur (partition ilk oluştuğunda).
///
/// Reddetme metni policy'ye özgüdür (<see cref="AdminUserListRateLimitPolicy.OnRejected"/>); ileride eklenecek
/// başka policy'ler kendi OnRejected'ını vermezse global handler jenerik <c>common.tooManyRequests</c> metnini yazar.
/// </summary>
public static class AdminUserListRateLimiting
{
    public const string Policy = "admin-user-list";

    public static IServiceCollection AddAdminUserListRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<AdminUserListRateLimitOptions>()
            .BindConfiguration(AdminUserListRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

            // Jenerik yedek: kendi OnRejected'ı olmayan policy'ler için. Policy OnRejected'ı varsa bu ÇAĞRILMAZ.
            options.OnRejected = (context, cancellationToken) =>
                WriteRejectionAsync(context, "common.tooManyRequests", fallbackRetryAfterSeconds: null, cancellationToken);

            options.AddPolicy<string, AdminUserListRateLimitPolicy>(Policy);
        });

        return services;
    }

    /// <summary>429 + (biliniyorsa) Retry-After (saniye) + yerelleştirilmiş düz metin body.</summary>
    internal static async ValueTask WriteRejectionAsync(
        OnRejectedContext context, string messageKey, int? fallbackRetryAfterSeconds, CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;
        response.StatusCode = StatusCodes.Status429TooManyRequests;

        int? retryAfter = context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var metadata)
            ? Math.Max(1, (int)Math.Ceiling(metadata.TotalSeconds))
            : fallbackRetryAfterSeconds;
        if (retryAfter is { } seconds)
            response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);

        var localizer = context.HttpContext.RequestServices.GetService<IStringLocalizer<Messages>>()
                        ?? FallbackMessageLocalizer.Instance;
        await response.WriteAsync(localizer[messageKey].Value, cancellationToken);
    }

    /// <summary>
    /// Keycloak <c>sub</c>. [Authorize(Roles="Admin")] nedeniyle normalde hep doludur; olmazsa tüm "kimliksiz"
    /// istekler tek ortak kovaya düşer (limit gevşemez, sıkılaşır).
    /// </summary>
    internal static string PartitionKey(HttpContext httpContext) =>
        "sub:" + (httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "unknown");
}

/// <summary>
/// <see cref="AdminUserListRateLimiting.Policy"/>'nin kendisi: sub başına sabit pencere + policy'ye özgü 429 metni
/// (<c>admin.userList.rateLimited</c>). DI ile oluşturulur (singleton ömrü; ayarlar istek anında okunur).
/// </summary>
public sealed class AdminUserListRateLimitPolicy : IRateLimiterPolicy<string>
{
    private readonly IOptionsMonitor<AdminUserListRateLimitOptions> _options;
    private readonly ILogger<AdminUserListRateLimitPolicy> _logger;

    public AdminUserListRateLimitPolicy(
        IOptionsMonitor<AdminUserListRateLimitOptions> options, ILogger<AdminUserListRateLimitPolicy> logger)
    {
        _options = options;
        _logger = logger;
    }

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        var settings = _options.CurrentValue;
        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: AdminUserListRateLimiting.PartitionKey(httpContext),
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = settings.PermitLimit,
                Window = TimeSpan.FromSeconds(settings.WindowSeconds),
                QueueLimit = 0, // fazlası beklemez, anında 429
                AutoReplenishment = true
            });
    }

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => RejectAsync;

    private async ValueTask RejectAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        // Olası token kötüye kullanımının izi: yalnızca sub (PII değil) + path.
        _logger.LogWarning("[AdminUserList] Rate limit aşıldı: sub={Sub} path={Path}",
            AdminUserListRateLimiting.PartitionKey(context.HttpContext), context.HttpContext.Request.Path.Value);

        await AdminUserListRateLimiting.WriteRejectionAsync(
            context, "admin.userList.rateLimited", _options.CurrentValue.WindowSeconds, cancellationToken);
    }
}
