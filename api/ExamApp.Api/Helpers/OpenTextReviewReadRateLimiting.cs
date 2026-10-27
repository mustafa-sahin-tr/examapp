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
/// <c>RateLimiting:OpenTextReviewRead</c> ayarları (issue #583). Varsayılan: kullanıcı başına dakikada 60 kuyruk okuması.
/// </summary>
public sealed class OpenTextReviewReadRateLimitOptions
{
    public const string SectionName = "RateLimiting:OpenTextReviewRead";

    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 60;

    [Range(1, 86_400)]
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
/// issue #583 (review D1): açık uçlu değerlendirme kuyruğu okuması (<c>GET api/open-text-reviews</c>) için KULLANICI (sub) başına
/// sabit pencere rate limit — öğrenci metinleri sayfa sayfa kazınamasın. Yazma kovası <see cref="OpenTextReviewRateLimiting"/>.
/// </summary>
public static class OpenTextReviewReadRateLimiting
{
    public const string Policy = "open-text-review-read";

    /// <summary><see cref="AdminUserListRateLimiting.AddAdminUserListRateLimiting"/>'ten SONRA çağrılır (global ayarlar orada).</summary>
    public static IServiceCollection AddOpenTextReviewReadRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<OpenTextReviewReadRateLimitOptions>()
            .BindConfiguration(OpenTextReviewReadRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<RateLimiterOptions>(options =>
            options.AddPolicy<string, OpenTextReviewReadRateLimitPolicy>(Policy));

        return services;
    }
}

/// <summary>Sub başına sabit pencere + 429 <c>{ message, errorCode: "RateLimited" }</c> (<c>worksheets.openTextReview.readRateLimited</c>).</summary>
public sealed class OpenTextReviewReadRateLimitPolicy : IRateLimiterPolicy<string>
{
    private readonly IOptionsMonitor<OpenTextReviewReadRateLimitOptions> _options;
    private readonly ILogger<OpenTextReviewReadRateLimitPolicy> _logger;

    public OpenTextReviewReadRateLimitPolicy(
        IOptionsMonitor<OpenTextReviewReadRateLimitOptions> options, ILogger<OpenTextReviewReadRateLimitPolicy> logger)
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
        _logger.LogWarning("[OpenTextReviewRead] Rate limit aşıldı: sub={Sub} path={Path}",
            AdminUserListRateLimiting.PartitionKey(context.HttpContext), context.HttpContext.Request.Path.Value);

        await AdminUserListRateLimiting.WriteRejectionAsync(
            context, "worksheets.openTextReview.readRateLimited", _options.CurrentValue.WindowSeconds, errorCode: "RateLimited", cancellationToken);
    }
}
