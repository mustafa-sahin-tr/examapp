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
/// <c>RateLimiting:QuestionBankSearch</c> ayarları (issue #471). Varsayılan: kullanıcı başına dakikada 120 arama.
/// </summary>
public sealed class QuestionBankSearchRateLimitOptions
{
    public const string SectionName = "RateLimiting:QuestionBankSearch";

    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 120;

    [Range(1, 86_400)]
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
/// Soru bankası araması (<c>GET api/questions/bank</c>) için KULLANICI (sub) başına sabit pencere rate limit (issue #471
/// security review L1). Erişim kuralı yeni değil (soru okuma kuralıyla aynı) ama sayfalı tek uç, görülebilen tüm soruları
/// ve imzalı görsellerini bir döngüyle toplu çekmeyi kolaylaştırıyordu; bu kova onu yavaşlatır ve sorgu yükünü sınırlar.
/// Filtre değişimleri UI'da 250 ms debounce'lu olduğu için normal kullanım limitin çok altında kalır.
/// <see cref="AdminAccountStatusRateLimiting"/> ile aynı desen (süreç içi sabit pencere).
/// </summary>
public static class QuestionBankSearchRateLimiting
{
    public const string Policy = "question-bank-search";

    /// <summary><see cref="AdminUserListRateLimiting.AddAdminUserListRateLimiting"/>'ten SONRA çağrılır (global ayarlar orada).</summary>
    public static IServiceCollection AddQuestionBankSearchRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<QuestionBankSearchRateLimitOptions>()
            .BindConfiguration(QuestionBankSearchRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<RateLimiterOptions>(options =>
            options.AddPolicy<string, QuestionBankSearchRateLimitPolicy>(Policy));

        return services;
    }
}

/// <summary>Sub başına sabit pencere + policy'ye özgü 429 metni (<c>questions.bank.rateLimited</c>).</summary>
public sealed class QuestionBankSearchRateLimitPolicy : IRateLimiterPolicy<string>
{
    private readonly IOptionsMonitor<QuestionBankSearchRateLimitOptions> _options;
    private readonly ILogger<QuestionBankSearchRateLimitPolicy> _logger;

    public QuestionBankSearchRateLimitPolicy(
        IOptionsMonitor<QuestionBankSearchRateLimitOptions> options, ILogger<QuestionBankSearchRateLimitPolicy> logger)
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
        _logger.LogWarning("[QuestionBankSearch] Rate limit aşıldı: sub={Sub} path={Path}",
            AdminUserListRateLimiting.PartitionKey(context.HttpContext), context.HttpContext.Request.Path.Value);

        await AdminUserListRateLimiting.WriteRejectionAsync(
            context, "questions.bank.rateLimited", _options.CurrentValue.WindowSeconds, errorCode: null, cancellationToken);
    }
}
