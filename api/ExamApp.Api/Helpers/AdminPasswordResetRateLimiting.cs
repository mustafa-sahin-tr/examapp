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
/// <c>RateLimiting:AdminPasswordReset</c> ayarları (issue #156). Varsayılan: admin başına dakikada 10 sıfırlama.
/// </summary>
public sealed class AdminPasswordResetRateLimitOptions
{
    public const string SectionName = "RateLimiting:AdminPasswordReset";

    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 10;

    [Range(1, 86_400)]
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
/// Admin şifre sıfırlama uçlarına (<c>POST api/admin/{teachers|students}/{id}/reset-password</c>) KULLANICI (sub)
/// başına sabit pencere rate limit (issue #156). <see cref="AdminUserListRateLimiting"/> (#246) ile aynı desen ve aynı
/// yardımcılar (partition = Keycloak sub, 429 + Retry-After + yerelleştirilmiş düz metin); kova ondan AYRIDIR
/// (farklı policy adı → farklı partition). Öğretmen + öğrenci ucu tek kovayı paylaşır.
/// Amaç: çalınmış bir admin token'ıyla toplu hesap ele geçirmeyi yavaşlatmak.
/// </summary>
public static class AdminPasswordResetRateLimiting
{
    public const string Policy = "admin-password-reset";

    /// <summary><see cref="AdminUserListRateLimiting.AddAdminUserListRateLimiting"/>'ten SONRA çağrılır (global ayarlar orada).</summary>
    public static IServiceCollection AddAdminPasswordResetRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<AdminPasswordResetRateLimitOptions>()
            .BindConfiguration(AdminPasswordResetRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<RateLimiterOptions>(options =>
            options.AddPolicy<string, AdminPasswordResetRateLimitPolicy>(Policy));

        return services;
    }
}

/// <summary>Sub başına sabit pencere + policy'ye özgü 429 metni (<c>admin.passwordReset.rateLimited</c>).</summary>
public sealed class AdminPasswordResetRateLimitPolicy : IRateLimiterPolicy<string>
{
    private readonly IOptionsMonitor<AdminPasswordResetRateLimitOptions> _options;
    private readonly ILogger<AdminPasswordResetRateLimitPolicy> _logger;

    public AdminPasswordResetRateLimitPolicy(
        IOptionsMonitor<AdminPasswordResetRateLimitOptions> options, ILogger<AdminPasswordResetRateLimitPolicy> logger)
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
        _logger.LogWarning("[AdminPasswordReset] Rate limit aşıldı: sub={Sub} path={Path}",
            AdminUserListRateLimiting.PartitionKey(context.HttpContext), context.HttpContext.Request.Path.Value);

        await AdminUserListRateLimiting.WriteRejectionAsync(
            context, "admin.passwordReset.rateLimited", _options.CurrentValue.WindowSeconds, cancellationToken);
    }
}
