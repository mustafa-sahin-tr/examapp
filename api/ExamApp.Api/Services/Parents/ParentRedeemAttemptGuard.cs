using System;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Helpers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Parents;

/// <summary><c>ParentLinks:RedeemGuard</c> — başarısız davet kodu denemeleri için hesap ve platform tavanları (issue #419 review).</summary>
public sealed class ParentRedeemGuardOptions
{
    public const string SectionName = "ParentLinks:RedeemGuard";

    /// <summary>Hesap (veli) başına pencere içinde izin verilen başarısız deneme sayısı; aşılınca pencere bitene kadar 429.</summary>
    [Range(1, 10_000)]
    public int MaxFailuresPerAccount { get; set; } = 20;

    [Range(60, 7 * 86_400)]
    public int AccountWindowSeconds { get; set; } = 86_400;

    /// <summary>Platform geneli: pencere içinde bu kadar başarısız denemeden SONRA devre açılır (herkese 429).</summary>
    [Range(1, 1_000_000)]
    public int GlobalFailureThreshold { get; set; } = 200;

    [Range(1, 3_600)]
    public int GlobalWindowSeconds { get; set; } = 60;

    /// <summary>Devre açıldıktan sonra redeem'in herkese kapalı kaldığı süre.</summary>
    [Range(1, 86_400)]
    public int BreakerCooldownSeconds { get; set; } = 120;
}

/// <summary>Redeem'e izin var mı; yoksa ne kadar sonra.</summary>
public readonly record struct RedeemGuardDecision(bool Allowed, int RetryAfterSeconds)
{
    public static readonly RedeemGuardDecision Allow = new(true, 0);
}

/// <summary>
/// issue #419 review (Orta: çok hesaplı kod brute-force'u). Dakikada 5'lik istek kovasının (<see cref="ParentLinkRateLimiting"/>)
/// üstüne iki BAŞARISIZLIK sayacı: (1) hesap başına günde 20, (2) platform geneli devre kesici (dakikada 200 başarısızlık →
/// 2 dk + Error log). Sayaçlar <see cref="IFixedWindowCounterStore"/>'da (Redis varsa dağıtık, kesintide süreç içi); okuma
/// <see cref="IFixedWindowCounterStore.PeekAsync"/> ile yapılır — pencere yalnızca İLK başarısızlıkta başlar.
/// Devre açıkken yalnızca pencerede EN AZ BİR başarısız denemesi olan hesaplar reddedilir (re-review: devre herkesi
/// kilitleyip meşru velileri cezalandırmasın); hiç başarısızlığı olmayan hesap kodunu girebilir. Saldırgan her hesapla en
/// fazla bir deneme yapabilir. Dağıtık global sayacın eşiği aştığını gören HER replica kendi "açık-kadar" zamanını tam
/// cooldown kadar ileri alır (yalnızca sayaç penceresine bağlı kalsaydı pencere sonuna yakın açılan devre birkaç saniyede
/// kapanırdı).
/// </summary>
public interface IParentRedeemAttemptGuard
{
    /// <summary>Kod aranmadan önce: hesap tavanı dolmuşsa ya da devre açık ve hesabın başarısızlığı varsa reddedilir.</summary>
    ValueTask<RedeemGuardDecision> CheckAsync(int parentUserId, CancellationToken ct = default);

    /// <summary>Genel <c>InvalidCode</c> sonucundan sonra çağrılır.</summary>
    ValueTask RecordFailureAsync(int parentUserId, CancellationToken ct = default);
}

/// <inheritdoc cref="IParentRedeemAttemptGuard"/>
public sealed class ParentRedeemAttemptGuard : IParentRedeemAttemptGuard
{
    private readonly IFixedWindowCounterStore _store;
    private readonly IOptionsMonitor<ParentRedeemGuardOptions> _options;
    private readonly ILogger<ParentRedeemAttemptGuard> _logger;
    private readonly TimeProvider _time;
    private readonly string _prefix;
    private long _openUntilTicks;

    public ParentRedeemAttemptGuard(
        IFixedWindowCounterStore store,
        IOptionsMonitor<ParentRedeemGuardOptions> options,
        IConfiguration configuration,
        ILogger<ParentRedeemAttemptGuard> logger,
        TimeProvider? time = null)
    {
        _store = store;
        _options = options;
        _logger = logger;
        _time = time ?? TimeProvider.System;
        _prefix = (configuration["Redis:InstanceName"] ?? string.Empty) + "parent-redeem:";
    }

    private string AccountKey(int userId) => _prefix + "fail:user:" + userId;
    private string GlobalKey => _prefix + "fail:global";

    public async ValueTask<RedeemGuardDecision> CheckAsync(int parentUserId, CancellationToken ct = default)
    {
        var o = _options.CurrentValue;

        // Hesap: sayaç tavana ULAŞTIYSA (count >= Max) pencere bitene kadar yeni deneme yok.
        var account = await _store.PeekAsync(AccountKey(parentUserId), ct);
        if (account.Count >= o.MaxFailuresPerAccount)
            return new RedeemGuardDecision(false, Seconds(account.RemainingWindow, o.AccountWindowSeconds));

        if (account.Count == 0)
            return RedeemGuardDecision.Allow; // devre açık olsa da başarısızlığı olmayan hesap muaf

        var now = _time.GetUtcNow().UtcTicks;
        var openUntil = Interlocked.Read(ref _openUntilTicks);
        if (openUntil > now)
            return new RedeemGuardDecision(false, Seconds(TimeSpan.FromTicks(openUntil - now), o.BreakerCooldownSeconds));

        // Başka replica'nın saydığı başarısızlıklar eşiği aştıysa bu replica da devreyi açar.
        var global = await _store.PeekAsync(GlobalKey, ct);
        if (global.Count > o.GlobalFailureThreshold)
        {
            Open(o);
            return new RedeemGuardDecision(false, o.BreakerCooldownSeconds);
        }

        return RedeemGuardDecision.Allow;
    }

    public async ValueTask RecordFailureAsync(int parentUserId, CancellationToken ct = default)
    {
        var o = _options.CurrentValue;
        var account = await _store.TryAcquireAsync(
            AccountKey(parentUserId), 1, o.MaxFailuresPerAccount, TimeSpan.FromSeconds(o.AccountWindowSeconds), ct);
        if (account.Count == o.MaxFailuresPerAccount)
            _logger.LogWarning("[ParentLinks] Hesap günlük başarısız kod denemesi tavanına ulaştı: parentUserId={ParentUserId}", parentUserId);

        var global = await _store.TryAcquireAsync(
            GlobalKey, 1, o.GlobalFailureThreshold, TimeSpan.FromSeconds(o.GlobalWindowSeconds), ct);
        if (!global.Allowed)
            Open(o);
        if (global.IsFirstRejection(1, o.GlobalFailureThreshold))
        {
            _logger.LogError(
                "[ParentLinks] Davet kodu devre kesicisi AÇILDI: {Window}s içinde {Threshold}+ başarısız deneme; redeem {Cooldown}s başarısız denemesi olan hesaplara kapalı.",
                o.GlobalWindowSeconds, o.GlobalFailureThreshold, o.BreakerCooldownSeconds);
        }
    }

    private void Open(ParentRedeemGuardOptions o)
    {
        var until = _time.GetUtcNow().UtcTicks + TimeSpan.FromSeconds(o.BreakerCooldownSeconds).Ticks;
        long current;
        do
        {
            current = Interlocked.Read(ref _openUntilTicks);
            if (current >= until)
                return;
        }
        while (Interlocked.CompareExchange(ref _openUntilTicks, until, current) != current);
    }

    private static int Seconds(TimeSpan? retryAfter, int fallback)
        => retryAfter is { } r ? Math.Max(1, (int)Math.Ceiling(r.TotalSeconds)) : fallback;
}
