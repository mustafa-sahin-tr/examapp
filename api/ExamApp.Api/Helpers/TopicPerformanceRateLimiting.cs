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

/// <summary><c>RateLimiting:TopicPerformance</c> ayarları (issue #497). Varsayılan: kullanıcı başına dakikada 30 istek.</summary>
public sealed class TopicPerformanceRateLimitOptions
{
    public const string SectionName = "RateLimiting:TopicPerformance";

    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 30;

    [Range(1, 86_400)]
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
/// Issue #497: konu performansı öğrencinin 180 günlük cevaplarını okur. Asistan aracı döngüye girse bile DB yükü sınırlı
/// kalsın diye kullanıcı (sub) başına sabit pencere. <see cref="ActivityCheckRateLimiting"/> ile aynı desen.
/// </summary>
public static class TopicPerformanceRateLimiting
{
    public const string Policy = "topic-performance";

    /// <summary><see cref="AdminUserListRateLimiting.AddAdminUserListRateLimiting"/>'ten SONRA çağrılır (global ayarlar orada).</summary>
    public static IServiceCollection AddTopicPerformanceRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<TopicPerformanceRateLimitOptions>()
            .BindConfiguration(TopicPerformanceRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<RateLimiterOptions>(options =>
            options.AddPolicy<string, TopicPerformanceRateLimitPolicy>(Policy));

        return services;
    }
}

/// <summary>Sub başına sabit pencere + 429 <c>{ message, errorCode: "RateLimited" }</c>.</summary>
public sealed class TopicPerformanceRateLimitPolicy : IRateLimiterPolicy<string>
{
    private readonly IOptionsMonitor<TopicPerformanceRateLimitOptions> _options;
    private readonly ILogger<TopicPerformanceRateLimitPolicy> _logger;

    public TopicPerformanceRateLimitPolicy(
        IOptionsMonitor<TopicPerformanceRateLimitOptions> options, ILogger<TopicPerformanceRateLimitPolicy> logger)
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
        _logger.LogWarning("[TopicPerformance] Rate limit aşıldı: sub={Sub} path={Path}",
            AdminUserListRateLimiting.PartitionKey(context.HttpContext), context.HttpContext.Request.Path.Value);

        await AdminUserListRateLimiting.WriteRejectionAsync(
            context, "student.topicPerformance.rateLimited", _options.CurrentValue.WindowSeconds, errorCode: "RateLimited", cancellationToken);
    }
}
