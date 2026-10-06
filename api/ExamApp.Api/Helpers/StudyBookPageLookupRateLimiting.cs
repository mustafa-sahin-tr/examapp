using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Helpers;

/// <summary>
/// <c>RateLimiting:StudyBookPageLookup</c> ayarları (issue #365 S3, security Low-1). Varsayılan: kullanıcı başına dakikada
/// 30 istek (her istek en fazla 200 StatObject; editör bir JSON kılavuzu için birkaç istek atar).
/// </summary>
public sealed class StudyBookPageLookupRateLimitOptions
{
    public const string SectionName = "RateLimiting:StudyBookPageLookup";

    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 30;

    [Range(1, 86_400)]
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
/// <c>POST api/study-items/book-pages/lookup</c> için KULLANICI (sub) başına sabit pencere rate limit (issue #365 S3).
/// <see cref="AdminAccountStatusRateLimiting"/> ile aynı desen (süreç içi, replica başına). Amaç: tek bir hesabın
/// istek başına 200 StatObject'lik sorguyu döngüye sokup MinIO'yu boğmasını / nesne adı taramasını yavaşlatmak.
/// </summary>
public static class StudyBookPageLookupRateLimiting
{
    public const string Policy = "study-book-page-lookup";

    /// <summary><see cref="AdminUserListRateLimiting.AddAdminUserListRateLimiting"/>'ten SONRA çağrılır (global ayarlar orada).</summary>
    public static IServiceCollection AddStudyBookPageLookupRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<StudyBookPageLookupRateLimitOptions>()
            .BindConfiguration(StudyBookPageLookupRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<RateLimiterOptions>(options =>
            options.AddPolicy<string, StudyBookPageLookupRateLimitPolicy>(Policy));

        return services;
    }
}

/// <summary>Sub başına sabit pencere + policy'ye özgü 429 metni (<c>study.bookPages.rateLimited</c>, JSON gövde).</summary>
public sealed class StudyBookPageLookupRateLimitPolicy : IRateLimiterPolicy<string>
{
    private readonly IOptionsMonitor<StudyBookPageLookupRateLimitOptions> _options;
    private readonly ILogger<StudyBookPageLookupRateLimitPolicy> _logger;

    public StudyBookPageLookupRateLimitPolicy(
        IOptionsMonitor<StudyBookPageLookupRateLimitOptions> options, ILogger<StudyBookPageLookupRateLimitPolicy> logger)
    {
        _options = options;
        _logger = logger;
    }

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        // sub'sız istek ortak "unknown" kovasını paylaşmasın (#243/#279 item 7, StudentSelfReset deseni): koşulsuz
        // reddedilir (401). Uç [Authorize] olduğu için pratikte görülmez.
        if (string.IsNullOrEmpty(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)))
            return RateLimitPartition.Get(StudentSelfResetRateLimitPolicy.MissingSubPartitionKey, _ => new RejectAllRateLimiter());

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
        if (string.IsNullOrEmpty(context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)))
        {
            // Kota aşımı değil kimlik eksikliği: 429 + Retry-After yanıltıcı olurdu.
            context.HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        _logger.LogWarning("[StudyBookPageLookup] Rate limit aşıldı: sub={Sub}",
            AdminUserListRateLimiting.PartitionKey(context.HttpContext));

        await AdminUserListRateLimiting.WriteRejectionAsync(
            context, "study.bookPages.rateLimited", _options.CurrentValue.WindowSeconds, errorCode: "RateLimited",
            cancellationToken);
    }
}
