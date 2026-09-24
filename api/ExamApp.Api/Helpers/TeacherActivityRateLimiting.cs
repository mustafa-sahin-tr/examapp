using System;
using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Helpers;

/// <summary>
/// <c>RateLimiting:TeacherActivity</c> ayarları (issue #265). Varsayılan: öğretmen başına dakikada 60 istek — dashboard
/// açılışı iki uç çağırır (own + students), yani dakikada ~30 sayfa yenilemesi; normal kullanımı etkilemez.
/// </summary>
public sealed class TeacherActivityRateLimitOptions
{
    public const string SectionName = "RateLimiting:TeacherActivity";

    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; } = 60;

    [Range(1, 86_400)]
    public int WindowSeconds { get; set; } = 60;
}

/// <summary>
/// Öğretmen dashboard aktivite uçlarına (<c>GET api/teacher/own-activity-summary</c>,
/// <c>GET api/teacher/students-activity-summary</c>) ÖĞRETMEN (Keycloak <c>sub</c>) başına sabit pencere rate limit
/// (issue #265). Amaç: <c>days=90</c> ile tekrarlı çağrının 90 günlük toplamayı DB'ye durmadan yaptırmasını sınırlamak.
///
/// <see cref="AdminUserListRateLimiting"/> (#246/#262) altyapısını yeniden kullanır: aynı <see cref="IFixedWindowCounterStore"/>
/// (Redis varsa dağıtık — tüm replica'lar ortak sayaç; kesintide fail-open; <c>Redis:Configuration</c> boşsa süreç içi) ve
/// <see cref="DistributedFixedWindowRateLimiter"/>. İki uç TEK kovayı paylaşır. Sub'sız istek koşulsuz 401 (ortak kova yok,
/// #279 item 7). 429 + Retry-After + yerelleştirilmiş <c>teacher.activity.rateLimited</c>. Audit yok (admin verisi değil).
/// </summary>
public static class TeacherActivityRateLimiting
{
    public const string Policy = "teacher-activity";

    /// <summary>
    /// <see cref="AdminUserListRateLimiting.AddAdminUserListRateLimiting"/>'ten SONRA çağrılır (global 429 ayarları ve
    /// <see cref="IFixedWindowCounterStore"/> kaydı orada).
    /// </summary>
    public static IServiceCollection AddTeacherActivityRateLimiting(this IServiceCollection services)
    {
        services.AddOptions<TeacherActivityRateLimitOptions>()
            .BindConfiguration(TeacherActivityRateLimitOptions.SectionName)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<RateLimiterOptions>(options =>
            options.AddPolicy<string, TeacherActivityRateLimitPolicy>(Policy));

        return services;
    }
}

/// <summary>Sub başına DAĞITIK sabit pencere + policy'ye özgü 429 metni (<c>teacher.activity.rateLimited</c>).</summary>
public sealed class TeacherActivityRateLimitPolicy : IRateLimiterPolicy<string>
{
    /// <summary>Redis anahtar öneki: <c>{Redis:InstanceName}ratelimit:teacher-activity:sub:{sub}</c>.</summary>
    internal const string KeyPrefix = "ratelimit:" + TeacherActivityRateLimiting.Policy + ":";

    internal const string MissingSubPartitionKey = "sub:<missing>";

    private readonly IOptionsMonitor<TeacherActivityRateLimitOptions> _options;
    private readonly IFixedWindowCounterStore _store;
    private readonly string _instanceName;
    private readonly ILogger<TeacherActivityRateLimitPolicy> _logger;

    public TeacherActivityRateLimitPolicy(
        IOptionsMonitor<TeacherActivityRateLimitOptions> options,
        IFixedWindowCounterStore store,
        IConfiguration configuration,
        ILogger<TeacherActivityRateLimitPolicy> logger)
    {
        _options = options;
        _store = store;
        _instanceName = configuration["Redis:InstanceName"] ?? string.Empty;
        _logger = logger;
    }

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        // Sub'sız istekler tek ortak kovayı paylaşıp birbirini kilitlemesin diye koşulsuz reddedilir (#243 review, #279).
        if (string.IsNullOrEmpty(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)))
            return RateLimitPartition.Get(MissingSubPartitionKey, _ => new RejectAllRateLimiter());

        var settings = _options.CurrentValue;
        var partitionKey = AdminUserListRateLimiting.PartitionKey(httpContext);
        var storeKey = _instanceName + KeyPrefix + partitionKey;
        return RateLimitPartition.Get(partitionKey, _ => new DistributedFixedWindowRateLimiter(
            _store, storeKey, settings.PermitLimit, TimeSpan.FromSeconds(settings.WindowSeconds)));
    }

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => RejectAsync;

    private async ValueTask RejectAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier)))
        {
            // Kota aşımı değil kimlik eksikliği: 429 + Retry-After yanıltıcı olurdu.
            _logger.LogWarning("[TeacherActivity] sub claim'i olmayan istek reddedildi: path={Path}",
                context.HttpContext.Request.Path.Value);
            context.HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        _logger.LogWarning("[TeacherActivity] Rate limit aşıldı: sub={Sub} path={Path}",
            AdminUserListRateLimiting.PartitionKey(context.HttpContext), context.HttpContext.Request.Path.Value);

        await AdminUserListRateLimiting.WriteRejectionAsync(
            context, "teacher.activity.rateLimited", _options.CurrentValue.WindowSeconds, cancellationToken);
    }
}
