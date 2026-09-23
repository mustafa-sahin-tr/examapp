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
/// <c>RateLimiting:AdminAccountStatus</c> ayarları (issue #155). Varsayılan: admin başına dakikada 20 durum değişikliği.
/// </summary>
public sealed class AdminAccountStatusRateLimitOptions
{
    public const string SectionName = "RateLimiting:AdminAccountStatus";

    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 20;

    [Range(1, 86_400)]
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
/// Hesap durumu uçlarına (<c>PATCH api/admin/{teachers|students}/{id}/account-status</c>) KULLANICI (sub) başına sabit
/// pencere rate limit (issue #155). <see cref="AdminPasswordResetRateLimiting"/> (#156) ile aynı desen ve yardımcılar;
/// kova ondan AYRIDIR (farklı policy adı → farklı partition). Öğretmen + öğrenci ucu tek kovayı paylaşır.
/// Ayrı kova gerekçesi: disable → enable geri alma (yanlış tıklama düzeltmesi) şifre sıfırlama bütçesini tüketmesin,
/// şifre sıfırlama sonrası acil hesap kapatma da 429'a takılmasın. Amaç yine çalınmış admin token'ıyla toplu hesap
/// kapatmayı (DoS) yavaşlatmak.
/// </summary>
public static class AdminAccountStatusRateLimiting
{
    public const string Policy = "admin-account-status";

    /// <summary><see cref="AdminUserListRateLimiting.AddAdminUserListRateLimiting"/>'ten SONRA çağrılır (global ayarlar orada).</summary>
    public static IServiceCollection AddAdminAccountStatusRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<AdminAccountStatusRateLimitOptions>()
            .BindConfiguration(AdminAccountStatusRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<RateLimiterOptions>(options =>
            options.AddPolicy<string, AdminAccountStatusRateLimitPolicy>(Policy));

        return services;
    }
}

/// <summary>Sub başına sabit pencere + policy'ye özgü 429 metni (<c>admin.accountStatus.rateLimited</c>).</summary>
public sealed class AdminAccountStatusRateLimitPolicy : IRateLimiterPolicy<string>
{
    private readonly IOptionsMonitor<AdminAccountStatusRateLimitOptions> _options;
    private readonly ILogger<AdminAccountStatusRateLimitPolicy> _logger;

    public AdminAccountStatusRateLimitPolicy(
        IOptionsMonitor<AdminAccountStatusRateLimitOptions> options, ILogger<AdminAccountStatusRateLimitPolicy> logger)
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
        _logger.LogWarning("[AdminAccountStatus] Rate limit aşıldı: sub={Sub} path={Path}",
            AdminUserListRateLimiting.PartitionKey(context.HttpContext), context.HttpContext.Request.Path.Value);

        await AdminUserListRateLimiting.WriteRejectionAsync(
            context, "admin.accountStatus.rateLimited", _options.CurrentValue.WindowSeconds, cancellationToken);
    }
}
