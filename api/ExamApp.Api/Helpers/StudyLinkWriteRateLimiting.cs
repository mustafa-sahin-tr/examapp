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

/// <summary><c>RateLimiting:StudyLinkWrite</c> ayarları (issue #61). Varsayılan: kullanıcı başına dakikada 30 yazma.</summary>
public sealed class StudyLinkWriteRateLimitOptions
{
    public const string SectionName = "RateLimiting:StudyLinkWrite";

    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 30;

    [Range(1, 86_400)]
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
/// Çalışma linki yazma uçlarına (POST/PUT/DELETE/reorder — <c>api/study-links</c>) KULLANICI (sub) başına sabit pencere
/// rate limit (issue #61 güvenlik incelemesi, MEDIUM-2). <see cref="StudentSelfResetRateLimiting"/> ile aynı desen:
/// partition = Keycloak sub, bellek içi (tek instance), 429 + Retry-After + yerelleştirilmiş metin, sub'sız istek 401.
/// </summary>
public static class StudyLinkWriteRateLimiting
{
    public const string Policy = "study-link-write";

    /// <summary><see cref="AdminUserListRateLimiting.AddAdminUserListRateLimiting"/>'ten SONRA çağrılır (global ayarlar orada).</summary>
    public static IServiceCollection AddStudyLinkWriteRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<StudyLinkWriteRateLimitOptions>()
            .BindConfiguration(StudyLinkWriteRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<RateLimiterOptions>(options =>
            options.AddPolicy<string, StudyLinkWriteRateLimitPolicy>(Policy));

        return services;
    }
}

/// <summary>Sub başına sabit pencere + policy'ye özgü 429 metni (<c>studyLinks.rateLimited</c>).</summary>
public sealed class StudyLinkWriteRateLimitPolicy : IRateLimiterPolicy<string>
{
    internal const string MissingSubPartitionKey = "sub:<missing>";

    private readonly IOptionsMonitor<StudyLinkWriteRateLimitOptions> _options;
    private readonly ILogger<StudyLinkWriteRateLimitPolicy> _logger;

    public StudyLinkWriteRateLimitPolicy(
        IOptionsMonitor<StudyLinkWriteRateLimitOptions> options, ILogger<StudyLinkWriteRateLimitPolicy> logger)
    {
        _options = options;
        _logger = logger;
    }

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        // Sub'sız istekler tek ortak kovayı paylaşıp birbirini kilitlemesin diye koşulsuz reddedilir (bkz. #243 review).
        if (string.IsNullOrEmpty(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)))
            return RateLimitPartition.Get(MissingSubPartitionKey, _ => new RejectAllRateLimiter());

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
            _logger.LogWarning("[StudyLinkWrite] sub claim'i olmayan istek reddedildi: path={Path}",
                context.HttpContext.Request.Path.Value);
            context.HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        _logger.LogWarning("[StudyLinkWrite] Rate limit aşıldı: sub={Sub} path={Path}",
            AdminUserListRateLimiting.PartitionKey(context.HttpContext), context.HttpContext.Request.Path.Value);

        await AdminUserListRateLimiting.WriteRejectionAsync(
            context, "studyLinks.rateLimited", _options.CurrentValue.WindowSeconds, cancellationToken);
    }
}
