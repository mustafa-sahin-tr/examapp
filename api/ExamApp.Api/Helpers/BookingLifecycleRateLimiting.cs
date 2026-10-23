using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Helpers;

/// <summary>
/// <c>RateLimiting:BookingLifecycle</c> ayarları (issue #315). Varsayılan: kullanıcı başına dakikada 20 iptal/erteleme/no-show isteği.
/// </summary>
public sealed class BookingLifecycleRateLimitOptions
{
    public const string SectionName = "RateLimiting:BookingLifecycle";

    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 20;

    [Range(1, 86_400)]
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
/// Randevu iptal / erteleme / no-show uçları için KULLANICI (sub) başına sabit pencere rate limit (issue #315 security review
/// O2): her istek karşı tarafa bildirim üretir; öner → geri çek döngüsüyle bildirim seli üretilmesin.
/// </summary>
public static class BookingLifecycleRateLimiting
{
    public const string Policy = "booking-lifecycle";

    /// <summary><see cref="AdminUserListRateLimiting.AddAdminUserListRateLimiting"/>'ten SONRA çağrılır (global ayarlar orada).</summary>
    public static IServiceCollection AddBookingLifecycleRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<BookingLifecycleRateLimitOptions>()
            .BindConfiguration(BookingLifecycleRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<RateLimiterOptions>(options =>
            options.AddPolicy<string, BookingLifecycleRateLimitPolicy>(Policy));

        return services;
    }
}

/// <summary>Sub başına sabit pencere + policy'ye özgü 429 metni (<c>booking.lifecycle.rateLimited</c>).</summary>
public sealed class BookingLifecycleRateLimitPolicy : IRateLimiterPolicy<string>
{
    private readonly IOptionsMonitor<BookingLifecycleRateLimitOptions> _options;
    private readonly ILogger<BookingLifecycleRateLimitPolicy> _logger;

    public BookingLifecycleRateLimitPolicy(
        IOptionsMonitor<BookingLifecycleRateLimitOptions> options, ILogger<BookingLifecycleRateLimitPolicy> logger)
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
                QueueLimit = 0,
                AutoReplenishment = true
            });
    }

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => RejectAsync;

    private async ValueTask RejectAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        _logger.LogWarning("[BookingLifecycle] Rate limit aşıldı: sub={Sub} path={Path}",
            AdminUserListRateLimiting.PartitionKey(context.HttpContext), context.HttpContext.Request.Path.Value);

        await AdminUserListRateLimiting.WriteRejectionAsync(
            context, "booking.lifecycle.rateLimited", _options.CurrentValue.WindowSeconds, errorCode: null, cancellationToken);
    }
}
