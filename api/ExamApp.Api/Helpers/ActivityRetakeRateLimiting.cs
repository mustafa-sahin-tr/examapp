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
/// <c>RateLimiting:ActivityRetake</c> ayarları (issue #609). Varsayılan: kullanıcı başına dakikada 5 "Yeniden çöz" isteği.
/// </summary>
public sealed class ActivityRetakeRateLimitOptions
{
    public const string SectionName = "RateLimiting:ActivityRetake";

    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 5;

    [Range(1, 86_400)]
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
/// issue #609: "Yeniden çöz" (<c>POST api/test-instances/{instanceId}/retake</c>) için KULLANICI (sub) başına sabit pencere rate
/// limit — her başarılı istek oturumun tüm madde satırlarını tek transaction'da sıfırlar. <see cref="ActivityCheckRateLimiting"/>
/// ile aynı desen.
/// </summary>
public static class ActivityRetakeRateLimiting
{
    public const string Policy = "activity-retake";

    /// <summary><see cref="AdminUserListRateLimiting.AddAdminUserListRateLimiting"/>'ten SONRA çağrılır (global ayarlar orada).</summary>
    public static IServiceCollection AddActivityRetakeRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<ActivityRetakeRateLimitOptions>()
            .BindConfiguration(ActivityRetakeRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<RateLimiterOptions>(options =>
            options.AddPolicy<string, ActivityRetakeRateLimitPolicy>(Policy));

        return services;
    }
}

/// <summary>Sub başına sabit pencere + 429 <c>{ message, errorCode: "RateLimited" }</c> (<c>worksheets.session.activityRetakeRateLimited</c>).</summary>
public sealed class ActivityRetakeRateLimitPolicy : IRateLimiterPolicy<string>
{
    private readonly IOptionsMonitor<ActivityRetakeRateLimitOptions> _options;
    private readonly ILogger<ActivityRetakeRateLimitPolicy> _logger;

    public ActivityRetakeRateLimitPolicy(
        IOptionsMonitor<ActivityRetakeRateLimitOptions> options, ILogger<ActivityRetakeRateLimitPolicy> logger)
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
        _logger.LogWarning("[ActivityRetake] Rate limit aşıldı: sub={Sub} path={Path}",
            AdminUserListRateLimiting.PartitionKey(context.HttpContext), context.HttpContext.Request.Path.Value);

        await AdminUserListRateLimiting.WriteRejectionAsync(
            context, "worksheets.session.activityRetakeRateLimited", _options.CurrentValue.WindowSeconds, errorCode: "RateLimited", cancellationToken);
    }
}
