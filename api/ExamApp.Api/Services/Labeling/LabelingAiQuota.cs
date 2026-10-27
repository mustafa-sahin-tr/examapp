using System;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Helpers;

namespace ExamApp.Api.Services.Labeling;

/// <summary>
/// Claude çağrılarının ortak kotası (#578 <c>ai-detect</c> ve #579 <c>activity-structure</c> aynı sayaçları paylaşır):
/// kullanıcı başına saatlik canlı çağrı (<c>Anthropic:LiveCallsPerHour</c>) ve 24 saatlik tahmini harcama
/// (<c>DailyBudgetUsdPerUser</c> / <c>DailyBudgetUsdTotal</c>, mikro-USD sayaç). Kayıttan dönen sonuç sayılmaz.
/// </summary>
internal static class LabelingAiQuota
{
    public const string TotalBudgetKey = "labeling-ai-usd:all";
    public static readonly TimeSpan RateLimitWindow = TimeSpan.FromHours(1);
    public static readonly TimeSpan BudgetWindow = TimeSpan.FromHours(24);

    public static string UserBudgetKey(LabelingActor actor) => $"labeling-ai-usd:{actor.KeycloakId}";

    public static string UserCallsKey(LabelingActor actor) => $"labeling-ai:{actor.KeycloakId}";

    /// <summary>
    /// Önce kullanıcı ve toplam bütçe (dolduysa 429 <c>Budget</c>), sonra <paramref name="calls"/> canlı çağrılık saatlik kota
    /// (yetmezse 429). Kota tek seferde düşülür: istek ya bütünüyle kabul edilir ya da hiç sayılmaz.
    /// </summary>
    public static async Task EnsureAndAcquireAsync(
        IFixedWindowCounterStore store, LabelingActor actor, LabelingAiOptions options, int calls, CancellationToken ct)
    {
        await EnsureBudgetAsync(store, UserBudgetKey(actor), options.DailyBudgetUsdPerUser, ct);
        await EnsureBudgetAsync(store, TotalBudgetKey, options.DailyBudgetUsdTotal, ct);

        var decision = await store.TryAcquireAsync(UserCallsKey(actor), calls, options.LiveCallsPerHour, RateLimitWindow, ct);
        if (!decision.Allowed)
            throw new LabelingRateLimitException(decision.RetryAfter);
    }

    /// <summary>
    /// Issue #579 (review M2): canlı çağrılardan ÖNCE en kötü durum tahmini (<paramref name="estimateUsd"/>) kullanıcı ve toplam
    /// bütçe sayacına atomik olarak eklenir (Redis <c>INCRBY</c> + sınır kontrolü). Sınırı aşarsa eklenen geri alınır ve 429
    /// (<c>Budget</c>). Dönüş: ayrılan mikro-USD; çağrılar bitince <see cref="SettleAsync"/> ile gerçek maliyete düzeltilir.
    /// Böylece eşzamanlı istekler bütçeyi tahmin kadar aşabilir, sınırsız değil.
    /// </summary>
    public static async Task<int> ReserveBudgetAsync(
        IFixedWindowCounterStore store, LabelingActor actor, LabelingAiOptions options, decimal estimateUsd, CancellationToken ct)
    {
        // Dolu bütçe: tahmin 0 olsa da (fiyatsız model) yeni çağrı yapılmaz.
        await EnsureBudgetAsync(store, UserBudgetKey(actor), options.DailyBudgetUsdPerUser, ct);
        await EnsureBudgetAsync(store, TotalBudgetKey, options.DailyBudgetUsdTotal, ct);

        var micro = ToMicroUsd(estimateUsd);
        if (micro == 0)
            return 0;

        var user = await store.TryAcquireAsync(UserBudgetKey(actor), micro, ToMicroUsd(options.DailyBudgetUsdPerUser), BudgetWindow, ct);
        if (!user.Allowed)
        {
            await AddAsync(store, UserBudgetKey(actor), -micro);
            throw new LabelingRateLimitException(user.RetryAfter, budget: true);
        }

        var total = await store.TryAcquireAsync(TotalBudgetKey, micro, ToMicroUsd(options.DailyBudgetUsdTotal), BudgetWindow, ct);
        if (!total.Allowed)
        {
            await AddAsync(store, TotalBudgetKey, -micro);
            await AddAsync(store, UserBudgetKey(actor), -micro);
            throw new LabelingRateLimitException(total.RetryAfter, budget: true);
        }
        return micro;
    }

    /// <summary>Ayrılan tutarı gerçek maliyete çevirir (fark iade edilir ya da eklenir). İptal edilemez.</summary>
    public static async Task SettleAsync(IFixedWindowCounterStore store, LabelingActor actor, int reservedMicroUsd, decimal actualUsd)
    {
        var delta = ToMicroUsd(actualUsd) - reservedMicroUsd;
        if (delta == 0)
            return;
        await AddAsync(store, UserBudgetKey(actor), delta);
        await AddAsync(store, TotalBudgetKey, delta);
    }

    /// <summary>
    /// <paramref name="calls"/> canlı çağrılık saatlik kota; yetmezse eklenen geri alınır (reddedilen istek kotayı yemez) ve 429.
    /// </summary>
    public static async Task AcquireCallsAsync(
        IFixedWindowCounterStore store, LabelingActor actor, LabelingAiOptions options, int calls, CancellationToken ct)
    {
        var decision = await store.TryAcquireAsync(UserCallsKey(actor), calls, options.LiveCallsPerHour, RateLimitWindow, ct);
        if (!decision.Allowed)
        {
            await RefundCallsAsync(store, actor, calls);
            throw new LabelingRateLimitException(decision.RetryAfter);
        }
    }

    /// <summary>Hiç başlamamış çağrıların saatlik kotası iade edilir.</summary>
    public static Task RefundCallsAsync(IFixedWindowCounterStore store, LabelingActor actor, int calls) =>
        calls > 0 ? AddAsync(store, UserCallsKey(actor), -calls) : Task.CompletedTask;

    // Negatif artış = iade. Pencere bu arada dolup yenilendiyse sayaç kısa süre eksiye inebilir (en fazla bir rezervasyon kadar).
    private static async Task AddAsync(IFixedWindowCounterStore store, string key, int delta) =>
        await store.TryAcquireAsync(key, delta, int.MaxValue,
            key.StartsWith("labeling-ai-usd:", StringComparison.Ordinal) ? BudgetWindow : RateLimitWindow, CancellationToken.None);

    /// <summary>Kullanıcı / toplam günlük harcama sınırı (mikro-USD sayaç) dolduysa yeni canlı çağrı yapılmaz.</summary>
    public static async Task EnsureBudgetAsync(IFixedWindowCounterStore store, string key, decimal limitUsd, CancellationToken ct)
    {
        var spent = await store.PeekAsync(key, ct);
        if (spent.Count >= ToMicroUsd(limitUsd))
            throw new LabelingRateLimitException(spent.RemainingWindow, budget: true);
    }

    /// <summary>Ödenmiş çağrının maliyeti kullanıcı ve toplam bütçeye yazılır (istemci vazgeçse de: iptal edilemez).</summary>
    public static async Task ChargeAsync(IFixedWindowCounterStore store, LabelingActor actor, decimal costUsd)
    {
        await ChargeBudgetAsync(store, UserBudgetKey(actor), costUsd);
        await ChargeBudgetAsync(store, TotalBudgetKey, costUsd);
    }

    public static async Task ChargeBudgetAsync(IFixedWindowCounterStore store, string key, decimal costUsd)
    {
        var micro = ToMicroUsd(costUsd);
        if (micro > 0)
            await store.TryAcquireAsync(key, micro, int.MaxValue, BudgetWindow, CancellationToken.None);
    }

    public static decimal Cost(LabelingAiModelOption model, long inputTokens, long outputTokens) =>
        (inputTokens * model.InputUsdPerMTok + outputTokens * model.OutputUsdPerMTok) / 1_000_000m;

    private static int ToMicroUsd(decimal usd) => (int)Math.Clamp(Math.Ceiling(usd * 1_000_000m), 0, int.MaxValue);
}
