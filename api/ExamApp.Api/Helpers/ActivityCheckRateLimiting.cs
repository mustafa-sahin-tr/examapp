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
/// <c>RateLimiting:ActivityCheck</c> ayarları (issue #571). Varsayılan: kullanıcı başına dakikada 30 kontrol isteği.
/// </summary>
public sealed class ActivityCheckRateLimitOptions
{
    public const string SectionName = "RateLimiting:ActivityCheck";

    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 30;

    [Range(1, 86_400)]
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
/// issue #571: etkinlik kontrolü (<c>POST api/test-instances/{instanceId}/check</c>) için KULLANICI (sub) başına sabit pencere
/// rate limit — her istek 100 maddeye kadar doğrulama + puanlama + tek transaction'da yazma yapar; döngüyle DB yükü üretilmesin
/// ve kontrol ucu deneme-yanılma kâhini olarak hızla taranamasın. <see cref="QuestionBankLinkRateLimiting"/> ile aynı desen.
/// </summary>
public static class ActivityCheckRateLimiting
{
    public const string Policy = "activity-check";

    /// <summary><see cref="AdminUserListRateLimiting.AddAdminUserListRateLimiting"/>'ten SONRA çağrılır (global ayarlar orada).</summary>
    public static IServiceCollection AddActivityCheckRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<ActivityCheckRateLimitOptions>()
            .BindConfiguration(ActivityCheckRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<RateLimiterOptions>(options =>
            options.AddPolicy<string, ActivityCheckRateLimitPolicy>(Policy));

        return services;
    }
}

/// <summary>Sub başına sabit pencere + 429 <c>{ message, errorCode: "RateLimited" }</c> (<c>worksheets.session.activityCheckRateLimited</c>).</summary>
public sealed class ActivityCheckRateLimitPolicy : IRateLimiterPolicy<string>
{
    private readonly IOptionsMonitor<ActivityCheckRateLimitOptions> _options;
    private readonly ILogger<ActivityCheckRateLimitPolicy> _logger;

    public ActivityCheckRateLimitPolicy(
        IOptionsMonitor<ActivityCheckRateLimitOptions> options, ILogger<ActivityCheckRateLimitPolicy> logger)
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
        _logger.LogWarning("[ActivityCheck] Rate limit aşıldı: sub={Sub} path={Path}",
            AdminUserListRateLimiting.PartitionKey(context.HttpContext), context.HttpContext.Request.Path.Value);

        await AdminUserListRateLimiting.WriteRejectionAsync(
            context, "worksheets.session.activityCheckRateLimited", _options.CurrentValue.WindowSeconds, errorCode: "RateLimited", cancellationToken);
    }
}
