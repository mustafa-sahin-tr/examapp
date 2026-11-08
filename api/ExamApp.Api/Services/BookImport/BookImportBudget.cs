using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services.Labeling;

namespace ExamApp.Api.Services.BookImport;

/// <summary>Issue #1024: işin duraklama nedeni.</summary>
public enum BookImportPauseReason
{
    /// <summary>Aktarım ya da toplam günlük Claude bütçesi doldu; <see cref="BookImportPause.RetryAfter"/> sonra kendiliğinden devam.</summary>
    Budget,

    /// <summary>İşin maliyet tavanı aşıldı; Admin sürdürene kadar bekler.</summary>
    CostCeiling,
}

/// <summary>Issue #1024: iş başarısız değil, duraklatıldı (koşucu durumu yazar; bitmemiş test yeniden çalışmada baştan alınır).</summary>
public sealed class BookImportPause(BookImportPauseReason reason, TimeSpan? retryAfter = null)
    : Exception(reason == BookImportPauseReason.Budget ? "Daily Claude budget reached." : "Job cost ceiling reached.")
{
    public BookImportPauseReason Reason { get; } = reason;

    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>
/// Issue #1024: aktarımın bütün Claude çağrıları (kurulum ve tespit) buradan geçer. Her canlı çağrıdan önce
/// <see cref="EnsureAsync"/>: biriken maliyet yazılır, sonra aktarım bütçesi (<c>Anthropic:DailyBudgetUsdImport</c>), toplam bütçe
/// (<c>DailyBudgetUsdTotal</c>) ve işin tavanı denetlenir; doluysa <see cref="BookImportPause"/>. Kullanıcı bütçesi ve saatlik canlı
/// çağrı kotası (<c>LiveCallsPerHour</c>) kullanılmaz: aktarım öğretmenin canlı işaretlemesini tüketmez. Ödenmiş çağrı
/// <see cref="Record"/> ile hem işe (<see cref="BookImportJob.AiCostUsd"/>, <see cref="BookImportCost"/> satırı) hem bütçe sayaçlarına
/// yazılır. Bir çağrı tavanı en fazla kendi maliyeti kadar aşabilir.
/// </summary>
internal sealed class BookImportBudget(AppDbContext db, IFixedWindowCounterStore store, LabelingAiOptions options, BookImportJob job,
    decimal? ceilingUsd)
{
    private readonly List<BookImportCost> _pending = [];

    /// <summary>Yazılan maliyetin aşaması ve testi (soru aktarımı test başına ayarlar).</summary>
    public BookImportStage Stage { get; set; } = BookImportStage.Setup;

    public int? TestId { get; set; }

    /// <summary>Tavan; null ya da 0 → yok.</summary>
    public decimal? CeilingUsd { get; } = ceilingUsd is > 0 ? ceilingUsd : null;

    public void Record(string modelId, LabelingAiUsage usage, decimal costUsd)
    {
        if (costUsd <= 0 && usage.Input <= 0 && usage.Output <= 0)
            return;
        job.AiCostUsd += costUsd;
        _pending.Add(new BookImportCost
        {
            JobId = job.Id,
            Stage = Stage,
            TestId = TestId,
            ModelId = modelId.Length <= BookImportCost.MaxModelLength ? modelId : modelId[..BookImportCost.MaxModelLength],
            InputTokens = usage.Input,
            OutputTokens = usage.Output,
            CostUsd = Math.Round(costUsd, 6),
            CreatedAt = DateTime.UtcNow,
        });
    }

    public void Record(LabelingAiModelOption model, LabelingAiUsage usage) =>
        Record(model.Id, usage, LabelingAiQuota.Cost(model, usage.Input, usage.Output));

    /// <summary>Canlı çağrıdan önce: birikeni yazar, bütçe ya da tavan doluysa duraklatır.</summary>
    public async Task EnsureAsync(CancellationToken ct)
    {
        await FlushAsync();
        if (CeilingUsd is { } ceiling && job.AiCostUsd >= ceiling)
            throw new BookImportPause(BookImportPauseReason.CostCeiling);
        try
        {
            await LabelingAiQuota.EnsureBudgetAsync(store, LabelingAiQuota.ImportBudgetKey, options.DailyBudgetUsdImport, ct);
            await LabelingAiQuota.EnsureBudgetAsync(store, LabelingAiQuota.TotalBudgetKey, options.DailyBudgetUsdTotal, ct);
        }
        catch (LabelingRateLimitException full)
        {
            throw new BookImportPause(BookImportPauseReason.Budget, full.RetryAfter);
        }
    }

    /// <summary>Biriken maliyet satırlarını ve bütçe sayaçlarını yazar (ödenmiş harcama: iptal edilemez).</summary>
    public async Task FlushAsync()
    {
        if (_pending.Count == 0)
            return;
        var usd = _pending.Sum(c => c.CostUsd);
        await LabelingAiQuota.ChargeBudgetAsync(store, LabelingAiQuota.ImportBudgetKey, usd);
        await LabelingAiQuota.ChargeBudgetAsync(store, LabelingAiQuota.TotalBudgetKey, usd);
        db.BookImportCosts.AddRange(_pending);
        _pending.Clear();
        await db.SaveChangesAsync(CancellationToken.None);
    }
}
