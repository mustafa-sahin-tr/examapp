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
/// <c>RateLimiting:QuestionBankLink</c> ayarları (issue #472). Varsayılan: kullanıcı başına dakikada 30 bağlama isteği.
/// </summary>
public sealed class QuestionBankLinkRateLimitOptions
{
    public const string SectionName = "RateLimiting:QuestionBankLink";

    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 30;

    [Range(1, 86_400)]
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
/// "Bankadan ekle" (<c>POST api/questions/test/{testId}/bank-questions</c>) için KULLANICI (sub) başına sabit pencere
/// rate limit (issue #472 security review L2): her istek 60'a kadar soru için görünürlük alt sorgusu, bir advisory lock ve bir
/// yazma yapar; döngüyle DB yükü üretilmesin. Normal kullanımda dialog başına tek istek atılır.
/// <see cref="QuestionBankSearchRateLimiting"/> ile aynı desen.
/// </summary>
public static class QuestionBankLinkRateLimiting
{
    public const string Policy = "question-bank-link";

    /// <summary><see cref="AdminUserListRateLimiting.AddAdminUserListRateLimiting"/>'ten SONRA çağrılır (global ayarlar orada).</summary>
    public static IServiceCollection AddQuestionBankLinkRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<QuestionBankLinkRateLimitOptions>()
            .BindConfiguration(QuestionBankLinkRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<RateLimiterOptions>(options =>
            options.AddPolicy<string, QuestionBankLinkRateLimitPolicy>(Policy));

        return services;
    }
}

/// <summary>Sub başına sabit pencere + policy'ye özgü 429 metni (<c>questions.bankLink.rateLimited</c>).</summary>
public sealed class QuestionBankLinkRateLimitPolicy : IRateLimiterPolicy<string>
{
    private readonly IOptionsMonitor<QuestionBankLinkRateLimitOptions> _options;
    private readonly ILogger<QuestionBankLinkRateLimitPolicy> _logger;

    public QuestionBankLinkRateLimitPolicy(
        IOptionsMonitor<QuestionBankLinkRateLimitOptions> options, ILogger<QuestionBankLinkRateLimitPolicy> logger)
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
        _logger.LogWarning("[QuestionBankLink] Rate limit aşıldı: sub={Sub} path={Path}",
            AdminUserListRateLimiting.PartitionKey(context.HttpContext), context.HttpContext.Request.Path.Value);

        await AdminUserListRateLimiting.WriteRejectionAsync(
            context, "questions.bankLink.rateLimited", _options.CurrentValue.WindowSeconds, errorCode: null, cancellationToken);
    }
}
