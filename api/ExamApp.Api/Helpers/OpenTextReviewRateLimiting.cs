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
/// <c>RateLimiting:OpenTextReview</c> ayarları (issue #583). Varsayılan: kullanıcı başına dakikada 60 değerlendirme yazımı.
/// </summary>
public sealed class OpenTextReviewRateLimitOptions
{
    public const string SectionName = "RateLimiting:OpenTextReview";

    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 60;

    [Range(1, 86_400)]
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
/// issue #583: açık uçlu değerlendirme yazımı (<c>PUT api/open-text-reviews/{instanceQuestionId}</c>) için KULLANICI (sub) başına
/// sabit pencere rate limit — serbest metin (geri bildirim) yazan uç; <see cref="WorksheetCommentWriteRateLimiting"/> gibi yazma
/// kovası. <see cref="ActivityRetakeRateLimiting"/> ile aynı desen.
/// </summary>
public static class OpenTextReviewRateLimiting
{
    public const string Policy = "open-text-review";

    /// <summary><see cref="AdminUserListRateLimiting.AddAdminUserListRateLimiting"/>'ten SONRA çağrılır (global ayarlar orada).</summary>
    public static IServiceCollection AddOpenTextReviewRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<OpenTextReviewRateLimitOptions>()
            .BindConfiguration(OpenTextReviewRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<RateLimiterOptions>(options =>
            options.AddPolicy<string, OpenTextReviewRateLimitPolicy>(Policy));

        return services;
    }
}

/// <summary>Sub başına sabit pencere + 429 <c>{ message, errorCode: "RateLimited" }</c> (<c>worksheets.openTextReview.rateLimited</c>).</summary>
public sealed class OpenTextReviewRateLimitPolicy : IRateLimiterPolicy<string>
{
    private readonly IOptionsMonitor<OpenTextReviewRateLimitOptions> _options;
    private readonly ILogger<OpenTextReviewRateLimitPolicy> _logger;

    public OpenTextReviewRateLimitPolicy(
        IOptionsMonitor<OpenTextReviewRateLimitOptions> options, ILogger<OpenTextReviewRateLimitPolicy> logger)
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
        _logger.LogWarning("[OpenTextReview] Rate limit aşıldı: sub={Sub} path={Path}",
            AdminUserListRateLimiting.PartitionKey(context.HttpContext), context.HttpContext.Request.Path.Value);

        await AdminUserListRateLimiting.WriteRejectionAsync(
            context, "worksheets.openTextReview.rateLimited", _options.CurrentValue.WindowSeconds, errorCode: "RateLimited", cancellationToken);
    }
}
