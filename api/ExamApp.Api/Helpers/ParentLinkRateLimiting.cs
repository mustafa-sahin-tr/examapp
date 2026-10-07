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

/// <summary>Veli bağlantısı rate limit ayarlarının ortak şekli (issue #419).</summary>
public abstract class ParentLinkRateLimitOptionsBase
{
    [Range(1, int.MaxValue)]
    public int PermitLimit { get; set; }

    [Range(1, 86_400)]
    public int WindowSeconds { get; set; }

    /// <summary>Kova adı (Redis anahtar öneki de bundan türer). Yapılandırmadan bağlanmaz.</summary>
    internal abstract string PolicyName { get; }

    /// <summary>429 gövdesindeki yerelleştirilmiş mesaj anahtarı.</summary>
    internal abstract string MessageKey { get; }
}

/// <summary>
/// <c>RateLimiting:ParentLinkRedeem</c> — velinin kod denemesi. PO kararı: veli (sub) başına dakikada 5. Başarılı ve başarısız
/// denemeler aynı kovadan düşer (kod tahmini için tek sayaç).
/// </summary>
public sealed class ParentLinkRedeemRateLimitOptions : ParentLinkRateLimitOptionsBase
{
    public const string SectionName = "RateLimiting:ParentLinkRedeem";
    public ParentLinkRedeemRateLimitOptions() { PermitLimit = 5; WindowSeconds = 60; }
    internal override string PolicyName => ParentLinkRateLimiting.RedeemPolicy;
    internal override string MessageKey => "parentLinks.redeemRateLimited";
}

/// <summary>
/// <c>RateLimiting:ParentLinkInvite</c> — öğrencinin kod üretmesi (her çağrı bir satır yazar). Varsayılan: öğrenci başına
/// saatte 10.
/// </summary>
public sealed class ParentLinkInviteRateLimitOptions : ParentLinkRateLimitOptionsBase
{
    public const string SectionName = "RateLimiting:ParentLinkInvite";
    public ParentLinkInviteRateLimitOptions() { PermitLimit = 10; WindowSeconds = 3600; }
    internal override string PolicyName => ParentLinkRateLimiting.InvitePolicy;
    internal override string MessageKey => "parentLinks.inviteRateLimited";
}

/// <summary>
/// <c>RateLimiting:ParentChildSummary</c> — issue #420 review: velinin çocuk özeti okuması (her çağrı birkaç toplama sorgusu
/// + audit kontrolü). Varsayılan: veli (sub) başına dakikada 30 — panel açılışı/çocuk değişimi için bol, otomatik çekmeyi keser.
/// V3/V4 veli okuma uçları da bu kovayı paylaşabilir.
/// </summary>
public sealed class ParentChildSummaryRateLimitOptions : ParentLinkRateLimitOptionsBase
{
    public const string SectionName = "RateLimiting:ParentChildSummary";
    public ParentChildSummaryRateLimitOptions() { PermitLimit = 30; WindowSeconds = 60; }
    internal override string PolicyName => ParentLinkRateLimiting.ChildSummaryPolicy;
    internal override string MessageKey => "parentLinks.summaryRateLimited";
}

/// <summary>
/// <c>RateLimiting:ParentChildActivity</c> — issue #421: velinin ödev/test listesi ve test sonucu özeti okumaları (ortak kova;
/// özetten ayrı, sayfalama + sonuç tıklamaları özet kovasını tüketmesin). Varsayılan: veli (sub) başına dakikada 60.
/// </summary>
public sealed class ParentChildActivityRateLimitOptions : ParentLinkRateLimitOptionsBase
{
    public const string SectionName = "RateLimiting:ParentChildActivity";
    public ParentChildActivityRateLimitOptions() { PermitLimit = 60; WindowSeconds = 60; }
    internal override string PolicyName => ParentLinkRateLimiting.ChildActivityPolicy;
    internal override string MessageKey => "parentLinks.activityRateLimited";
}

/// <summary>
/// issue #419: veli bağlantısı uçlarına KULLANICI (Keycloak <c>sub</c>) başına dağıtık sabit pencere rate limit —
/// <see cref="DirectMessageRateLimiting"/> (#106) ile aynı altyapı: <see cref="IFixedWindowCounterStore"/> (Redis varsa dağıtık,
/// kesintide fail-open; yoksa süreç içi) + <see cref="DistributedFixedWindowRateLimiter"/>; sub'sız istek koşulsuz 401;
/// 429 + Retry-After + JSON <c>{ message, errorCode: "RateLimited" }</c>.
/// </summary>
public static class ParentLinkRateLimiting
{
    public const string RedeemPolicy = "parent-link-redeem";
    public const string InvitePolicy = "parent-link-invite";

    /// <summary>issue #420: <c>GET api/parent/children/{studentId}/summary</c>.</summary>
    public const string ChildSummaryPolicy = "parent-child-summary";

    /// <summary>issue #421: <c>GET .../assignments</c> ve <c>GET .../test-results/{testInstanceId}</c> (ortak kova).</summary>
    public const string ChildActivityPolicy = "parent-child-activity";

    /// <summary>429 gövdesindeki hata kodu (UI dallanması).</summary>
    public const string RateLimitedErrorCode = "RateLimited";

    /// <summary><see cref="AdminUserListRateLimiting.AddAdminUserListRateLimiting"/>'ten SONRA çağrılır (sayaç deposu orada).</summary>
    public static IServiceCollection AddParentLinkRateLimiting(this IServiceCollection services)
    {
        Add<ParentLinkRedeemRateLimitOptions>(services, ParentLinkRedeemRateLimitOptions.SectionName, RedeemPolicy);
        Add<ParentLinkInviteRateLimitOptions>(services, ParentLinkInviteRateLimitOptions.SectionName, InvitePolicy);
        Add<ParentChildSummaryRateLimitOptions>(services, ParentChildSummaryRateLimitOptions.SectionName, ChildSummaryPolicy);
        Add<ParentChildActivityRateLimitOptions>(services, ParentChildActivityRateLimitOptions.SectionName, ChildActivityPolicy);
        return services;
    }

    private static void Add<TOptions>(IServiceCollection services, string section, string policy)
        where TOptions : ParentLinkRateLimitOptionsBase, new()
    {
        services.AddOptions<TOptions>()
            .BindConfiguration(section)
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.Configure<RateLimiterOptions>(options =>
            options.AddPolicy<string, ParentLinkRateLimitPolicy<TOptions>>(policy));
    }
}

/// <summary>Tek kovalı policy (redeem / davet kodu / çocuk özeti / ödev-test okumaları).</summary>
public sealed class ParentLinkRateLimitPolicy<TOptions> : IRateLimiterPolicy<string>
    where TOptions : ParentLinkRateLimitOptionsBase, new()
{
    internal const string MissingSubPartitionKey = "sub:<missing>";

    private readonly IOptionsMonitor<TOptions> _options;
    private readonly IFixedWindowCounterStore _store;
    private readonly string _instanceName;
    private readonly ILogger<ParentLinkRateLimitPolicy<TOptions>> _logger;

    public ParentLinkRateLimitPolicy(
        IOptionsMonitor<TOptions> options,
        IFixedWindowCounterStore store,
        IConfiguration configuration,
        ILogger<ParentLinkRateLimitPolicy<TOptions>> logger)
    {
        _options = options;
        _store = store;
        _instanceName = configuration["Redis:InstanceName"] ?? string.Empty;
        _logger = logger;
    }

    private static bool HasSub(HttpContext httpContext)
        => !string.IsNullOrEmpty(httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier));

    public RateLimitPartition<string> GetPartition(HttpContext httpContext)
    {
        if (!HasSub(httpContext))
            return RateLimitPartition.Get(MissingSubPartitionKey, _ => new RejectAllRateLimiter());

        var settings = _options.CurrentValue;
        var sub = AdminUserListRateLimiting.PartitionKey(httpContext);
        var storeKey = _instanceName + "ratelimit:" + settings.PolicyName + ":" + sub;
        return RateLimitPartition.Get(settings.PolicyName + ":" + sub, _ => new DistributedFixedWindowRateLimiter(
            _store, storeKey, settings.PermitLimit, TimeSpan.FromSeconds(settings.WindowSeconds)));
    }

    public Func<OnRejectedContext, CancellationToken, ValueTask>? OnRejected => RejectAsync;

    private async ValueTask RejectAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        var settings = _options.CurrentValue;
        if (!HasSub(context.HttpContext))
        {
            _logger.LogWarning("[ParentLinks:{Policy}] sub claim'i olmayan istek reddedildi: path={Path}",
                settings.PolicyName, context.HttpContext.Request.Path.Value);
            context.HttpContext.Response.StatusCode = StatusCodes.Status401Unauthorized;
            return;
        }

        _logger.LogWarning("[ParentLinks:{Policy}] Rate limit aşıldı: sub={Sub} path={Path}",
            settings.PolicyName, AdminUserListRateLimiting.PartitionKey(context.HttpContext), context.HttpContext.Request.Path.Value);

        await AdminUserListRateLimiting.WriteRejectionAsync(
            context, settings.MessageKey, settings.WindowSeconds, ParentLinkRateLimiting.RateLimitedErrorCode, cancellationToken);
    }
}
