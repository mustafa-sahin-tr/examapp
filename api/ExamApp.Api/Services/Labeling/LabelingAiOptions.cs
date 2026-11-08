using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Services.Labeling;

/// <summary>
/// İşaretleme ekranının Claude ön-etiketleyicisi (<c>Anthropic</c> bölümü). <see cref="ApiKey"/> repoya girmez:
/// Aspire'da <c>Parameters:anthropic-api-key</c> user-secret'ı → <c>Anthropic__ApiKey</c>; boşsa AI ucu 503 döner.
/// </summary>
public class LabelingAiOptions
{
    public const string SectionName = "Anthropic";

    public string? ApiKey { get; set; }

    /// <summary>İzinli modeller ve 1M token fiyatları (maliyet tahmini). Listede olmayan model 400.</summary>
    [MinLength(1)]
    public List<LabelingAiModelOption> Models { get; set; } = [];

    /// <summary>Claude'a gönderilen görselin uzun kenarı (px); büyükse küçültülür, kutular orijinale ölçeklenir.</summary>
    [Range(512, 4096)]
    public int MaxImageEdge { get; set; } = 1568;

    /// <summary>Şık tespiti için aynı anda en fazla kaç soru kırpması gönderilir.</summary>
    [Range(1, 16)]
    public int MaxParallelCrops { get; set; } = 4;

    /// <summary>
    /// Bir sayfada şık tespiti yapılacak en fazla soru (her soru ayrı bir Claude çağrısıdır); fazlası şıksız kalmaz,
    /// hiç alınmaz. İstek başına maliyetin üst sınırı.
    /// </summary>
    [Range(1, 200)]
    public int MaxQuestionsPerPage { get; set; } = 40;

    /// <summary>Kullanıcı başına 24 saatlik tahmini Claude harcama sınırı (USD).</summary>
    [Range(0.01, 10_000)]
    public decimal DailyBudgetUsdPerUser { get; set; } = 5m;

    /// <summary>Tüm kullanıcılar için 24 saatlik tahmini Claude harcama sınırı (USD).</summary>
    [Range(0.01, 10_000)]
    public decimal DailyBudgetUsdTotal { get; set; } = 25m;

    /// <summary>
    /// Issue #1024: kitap aktarımlarının 24 saatlik ortak Claude harcama sınırı (USD). Aktarım çağrıları bu sayaca ve
    /// <see cref="DailyBudgetUsdTotal"/>'a yazılır, kullanıcı bütçesine ve <see cref="LiveCallsPerHour"/>'a yazılmaz: toplamın
    /// en az <c>Total − Import</c> kadarı canlı işaretlemeye kalır.
    /// </summary>
    [Range(0.01, 10_000)]
    public decimal DailyBudgetUsdImport { get; set; } = 15m;

    /// <summary>
    /// Issue #1052: hızlı eklemelerin 24 saatlik ortak Claude harcama sınırı (USD); kitap aktarımıyla aynı kural (kullanıcı bütçesine
    /// yazılmaz, toplama yazılır), ayrı sayaç: büyük bir kitap aktarımı öğretmenlerin hızlı eklemesini bekletmesin.
    /// </summary>
    [Range(0.01, 10_000)]
    public decimal DailyBudgetUsdQuickAdd { get; set; } = 5m;

    /// <summary>Kullanıcı başına saatte en fazla gerçek Claude çağrısı (kayıttan dönenler sayılmaz).</summary>
    [Range(1, 10_000)]
    public int LiveCallsPerHour { get; set; } = 60;

    /// <summary>
    /// Issue #579 (review L1): kullanıcı başına dakikada en fazla <c>activity-structure</c> isteği — kayıttan dönenler dahil
    /// (kırpma hash'i ve DB sorgusu her istekte çalışır).
    /// </summary>
    [Range(1, 10_000)]
    public int ActivityStructureRequestsPerMinute { get; set; } = 30;

    /// <summary><c>low|medium|high|xhigh</c> (MEAI <c>ReasoningEffort</c>); boşsa modelin varsayılanı.</summary>
    public string? Effort { get; set; } = "low";

    /// <summary>
    /// Issue #1025: geçici hatada (bağlantı, zaman aşımı, 408 / 409 / 429 / 5xx) Anthropic SDK'nın kısa üstel beklemeli yeniden
    /// deneme sayısı; sonra çağrı <see cref="LabelingAiException"/> ile düşer (kitap aktarımında sayfa / test hatası).
    /// </summary>
    [Range(0, 10)]
    public int MaxRetries { get; set; } = 3;
}

public class LabelingAiModelOption
{
    [Required]
    public string Id { get; set; } = string.Empty;

    [Required]
    public string DisplayName { get; set; } = string.Empty;

    public decimal InputUsdPerMTok { get; set; }

    public decimal OutputUsdPerMTok { get; set; }

    public bool IsDefault { get; set; }
}
