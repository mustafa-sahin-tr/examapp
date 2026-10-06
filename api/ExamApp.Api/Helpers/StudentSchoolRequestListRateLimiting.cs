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
/// <c>RateLimiting:StudentSchoolRequestList</c> ayarları (issue #361 review). Varsayılan: kullanıcı başına dakikada 60 istek
/// (liste + menü rozeti sayacı; normal kullanımda dakikada birkaç istek).
/// </summary>
public sealed class StudentSchoolRequestListRateLimitOptions
{
    public const string SectionName = "RateLimiting:StudentSchoolRequestList";

    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 60;

    [Range(1, 86_400)]
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
/// <c>GET api/student-school-requests</c> (+ <c>/count</c>) için KULLANICI (sub) başına sabit pencere rate limit (issue #361
/// review). <see cref="StudyBookPageLookupRateLimiting"/> ile aynı desen (süreç içi, replica başına). Amaç: öğretmenin bekleyen
/// öğrenci listesini (ad + kısmi numara) döngüyle kazımasını yavaşlatmak.
/// </summary>
public static class StudentSchoolRequestListRateLimiting
{
    public const string Policy = "student-school-request-list";

    /// <summary><see cref="AdminUserListRateLimiting.AddAdminUserListRateLimiting"/>'ten SONRA çağrılır (global ayarlar orada).</summary>
    public static IServiceCollection AddStudentSchoolRequestListRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<StudentSchoolRequestListRateLimitOptions>()
            .BindConfiguration(StudentSchoolRequestListRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<RateLimiterOptions>(options =>
            options.AddPolicy<string, StudentSchoolRequestListRateLimitPolicy>(Policy));

        return services;
    }
}

/// <summary>Sub başına sabit pencere + policy'ye özgü 429 metni (<c>student.schoolMembership.rateLimited</c>, JSON gövde).</summary>
public sealed class StudentSchoolRequestListRateLimitPolicy : IRateLimiterPolicy<string>
{
    private readonly IOptionsMonitor<StudentSchoolRequestListRateLimitOptions> _options;
    private readonly ILogger<StudentSchoolRequestListRateLimitPolicy> _logger;

    public StudentSchoolRequestListRateLimitPolicy(
        IOptionsMonitor<StudentSchoolRequestListRateLimitOptions> options, ILogger<StudentSchoolRequestListRateLimitPolicy> logger)
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

        _logger.LogWarning("[StudentSchoolRequestList] Rate limit aşıldı: sub={Sub}",
            AdminUserListRateLimiting.PartitionKey(context.HttpContext));

        await AdminUserListRateLimiting.WriteRejectionAsync(
            context, "student.schoolMembership.rateLimited", _options.CurrentValue.WindowSeconds, errorCode: "RateLimited",
            cancellationToken);
    }
}
