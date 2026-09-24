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
/// <c>RateLimiting:AdminStudentSchool</c> ayarları (issue #277 madde 8). Varsayılan: admin başına dakikada 20 okul değişikliği.
/// </summary>
public sealed class AdminStudentSchoolRateLimitOptions
{
    public const string SectionName = "RateLimiting:AdminStudentSchool";

    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 20;

    [Range(1, 86_400)]
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
/// Öğrenci okul değişikliği ucuna (<c>PUT api/admin/students/{id}/school</c>) KULLANICI (sub) başına sabit pencere rate
/// limit (issue #277). <see cref="AdminAccountStatusRateLimiting"/> (#155) ile aynı desen ve yardımcılar; kova ondan
/// AYRIDIR (farklı policy adı → farklı partition): toplu okul taşıma hesap kapatma bütçesini tüketmesin. Amaç çalınmış
/// admin token'ıyla öğrencileri toplu olarak başka okulun kapsamına taşımayı yavaşlatmak.
/// </summary>
public static class AdminStudentSchoolRateLimiting
{
    public const string Policy = "admin-student-school";

    /// <summary><see cref="AdminUserListRateLimiting.AddAdminUserListRateLimiting"/>'ten SONRA çağrılır (global ayarlar orada).</summary>
    public static IServiceCollection AddAdminStudentSchoolRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<AdminStudentSchoolRateLimitOptions>()
            .BindConfiguration(AdminStudentSchoolRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<RateLimiterOptions>(options =>
            options.AddPolicy<string, AdminStudentSchoolRateLimitPolicy>(Policy));

        return services;
    }
}

/// <summary>Sub başına sabit pencere + policy'ye özgü 429 metni (<c>admin.studentSchool.rateLimited</c>).</summary>
public sealed class AdminStudentSchoolRateLimitPolicy : IRateLimiterPolicy<string>
{
    private readonly IOptionsMonitor<AdminStudentSchoolRateLimitOptions> _options;
    private readonly ILogger<AdminStudentSchoolRateLimitPolicy> _logger;

    public AdminStudentSchoolRateLimitPolicy(
        IOptionsMonitor<AdminStudentSchoolRateLimitOptions> options, ILogger<AdminStudentSchoolRateLimitPolicy> logger)
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
        _logger.LogWarning("[AdminStudentSchool] Rate limit aşıldı: sub={Sub} path={Path}",
            AdminUserListRateLimiting.PartitionKey(context.HttpContext), context.HttpContext.Request.Path.Value);

        await AdminUserListRateLimiting.WriteRejectionAsync(
            context, "admin.studentSchool.rateLimited", _options.CurrentValue.WindowSeconds, cancellationToken);
    }
}
