using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
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
/// <c>RateLimiting:StudentSelfReset</c> ayarları (issue #243). Varsayılan: öğrenci başına saatte 1 sıfırlama.
/// </summary>
public sealed class StudentSelfResetRateLimitOptions
{
    public const string SectionName = "RateLimiting:StudentSelfReset";

    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 1;

    [Range(1, 86_400)]
    public int WindowSeconds { get; set; } = 3600;
}

/// <summary>
/// Öğrencinin kendi verisini sıfırladığı uca (<c>POST api/student/me/reset</c>) KULLANICI (sub) başına sabit pencere
/// rate limit (issue #243). Her çağrı bir Hangfire işi + BadgeService sıfırlaması + outbox mesajı ürettiğinden
/// tekrar tekrar tetiklenmesi kuyruk/broker yükü yaratır. <see cref="AdminPasswordResetRateLimiting"/> (#156) ile aynı
/// desen ve yardımcılar (partition = Keycloak sub, 429 + Retry-After + yerelleştirilmiş düz metin); kovası ayrıdır.
/// Aynı kullanıcı için bekleyen işin tekilleştirilmesi ayrıca <c>StudentResetScheduler</c>'dadır.
/// </summary>
public static class StudentSelfResetRateLimiting
{
    public const string Policy = "student-self-reset";

    /// <summary><see cref="AdminUserListRateLimiting.AddAdminUserListRateLimiting"/>'ten SONRA çağrılır (global ayarlar orada).</summary>
    public static IServiceCollection AddStudentSelfResetRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<StudentSelfResetRateLimitOptions>()
            .BindConfiguration(StudentSelfResetRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<RateLimiterOptions>(options =>
            options.AddPolicy<string, StudentSelfResetRateLimitPolicy>(Policy));

        return services;
    }
}

/// <summary>Sub başına sabit pencere + policy'ye özgü 429 metni (<c>student.reset.rateLimited</c>).</summary>
public sealed class StudentSelfResetRateLimitPolicy : IRateLimiterPolicy<string>
{
    private readonly IOptionsMonitor<StudentSelfResetRateLimitOptions> _options;
    private readonly ILogger<StudentSelfResetRateLimitPolicy> _logger;

    public StudentSelfResetRateLimitPolicy(
        IOptionsMonitor<StudentSelfResetRateLimitOptions> options, ILogger<StudentSelfResetRateLimitPolicy> logger)
    {
        _options = options;
        _logger = logger;
    }

    /// <summary>sub claim'i olmayan istekler için ayrılmış partition; hepsi reddedilir (bkz. <see cref="GetPartition"/>).</summary>
    internal const string MissingSubPartitionKey = "sub:<missing>";

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        // issue #243 review: sub yoksa AdminUserListRateLimiting.PartitionKey "sub:unknown" döner ve tüm kimliksiz
        // istekler TEK kovayı paylaşırdı — saatte 1 izinli bu uçta biri kovayı tüketince diğerleri de 429 alırdı
        // (global DoS). [Authorize(Roles="Student")] Keycloak token'ında sub'ı pratikte garanti eder ama sözleşmeyle
        // değil; limiter'ı sub'a bağlı bırakmak yerine sub'sız isteği burada koşulsuz reddediyoruz (401, bkz. RejectAsync).
        // Controller zaten sub'sız isteği 401'le reddettiği için meşru bir akış etkilenmez.
        if (string.IsNullOrEmpty(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)))
        {
            return RateLimitPartition.Get(MissingSubPartitionKey, _ => new RejectAllRateLimiter());
        }

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
            _logger.LogWarning("[StudentSelfReset] sub claim'i olmayan istek reddedildi: path={Path}",
                context.HttpContext.Request.Path.Value);
            context.HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        _logger.LogWarning("[StudentSelfReset] Rate limit aşıldı: sub={Sub} path={Path}",
            AdminUserListRateLimiting.PartitionKey(context.HttpContext), context.HttpContext.Request.Path.Value);

        await AdminUserListRateLimiting.WriteRejectionAsync(
            context, "student.reset.rateLimited", _options.CurrentValue.WindowSeconds, cancellationToken);
    }
}

/// <summary>
/// Her izni reddeden limiter (issue #243 review): sub'sız istekleri ortak bir kovaya almak yerine koşulsuz kesmek için.
/// FixedWindow/Concurrency limiter'lar PermitLimit=0'ı kabul etmediğinden ayrı, durumsuz bir tip.
/// </summary>
internal sealed class RejectAllRateLimiter : RateLimiter
{
    private static readonly RateLimitLease Rejected = new RejectedLease();

    public override TimeSpan? IdleDuration => null;

    public override RateLimiterStatistics? GetStatistics() => new()
    {
        CurrentAvailablePermits = 0,
        CurrentQueuedCount = 0,
    };

    protected override RateLimitLease AttemptAcquireCore(int permitCount) => Rejected;

    protected override ValueTask<RateLimitLease> AcquireAsyncCore(int permitCount, CancellationToken cancellationToken) =>
        ValueTask.FromResult(Rejected);

    private sealed class RejectedLease : RateLimitLease
    {
        public override bool IsAcquired => false;

        public override IEnumerable<string> MetadataNames => Array.Empty<string>();

        public override bool TryGetMetadata(string metadataName, out object? metadata)
        {
            metadata = null;
            return false;
        }
    }
}
