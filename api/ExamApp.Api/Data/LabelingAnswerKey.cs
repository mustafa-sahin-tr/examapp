using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>
/// Issue #580: bir kitabın cevap anahtarı sayfasının son Claude okuması (kitap + DOSYA sayfası başına tek satır). Önbellek: aynı sayfa
/// görseli (<see cref="ImageSha256"/>) + model + <c>answerKey</c> talimatının parmak izi (<see cref="PromptHash"/>) tekrar istenirse
/// Claude'a gidilmez, kayıtlı (öğretmenin düzelttiği dahil) maddeler döner. Canlı okuma maddelerin hepsini değiştirir.
/// </summary>
public class LabelingAnswerKeyPage
{
    public int Id { get; set; }

    public int BookId { get; set; }

    /// <summary>
    /// Kitabın dosya sayfası (cevap anahtarı aralığında). Kapsam genişlemesi: toplu yüklemede sayfa bilinmeyebilir → null; o zaman
    /// okuma (kitap, görsel SHA-256) ile tekildir (aynı görsel yeniden yüklenince kayıttan döner).
    /// </summary>
    public int? FilePage { get; set; }

    /// <summary>Okunan sayfa görselinin SHA-256'sı (ham baytlar, küçük harf hex).</summary>
    [MaxLength(64)]
    public string ImageSha256 { get; set; } = string.Empty;

    [MaxLength(64)]
    public string ModelName { get; set; } = string.Empty;

    [MaxLength(64)]
    public string PromptHash { get; set; } = string.Empty;

    /// <summary><c>answerKey</c> talimatının sürümü (gösterim; 0 = koddaki varsayılan).</summary>
    public int PromptVersion { get; set; }

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    public decimal EstimatedCostUsd { get; set; }

    [MaxLength(64)]
    public string? CreatedByKeycloakId { get; set; }

    [MaxLength(200)]
    public string? CreatedByName { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public ICollection<LabelingAnswerKeyEntry> Entries { get; set; } = new List<LabelingAnswerKeyEntry>();
}

/// <summary>
/// Issue #580: cevap anahtarının bir maddesi. <see cref="PrintedPage"/> = maddenin ait olduğu ETKİNLİĞİN anahtarda yazan bastırılmış
/// sayfası (anahtar sayfasının kendi numarası değil); <see cref="ActivityLabel"/>/<see cref="ItemLabel"/> anahtarda yazdığı gibi (eşleme
/// <c>ActivityLabelMatcher</c> ile normalize edilir). Görsel cevapta (<see cref="Visual"/>) kutu anahtar sayfasının pikselidir ve kırpması
/// <see cref="CropJpeg"/>'de saklanır (yalnız Claude'a girdi; tarayıcıya sunulmaz).
/// </summary>
public class LabelingAnswerKeyEntry
{
    public const int MaxLabelLength = 64;
    public const int MaxAnswerLength = 1000;

    /// <summary>Saklanan görsel cevap kırpmasının uzun kenarı (px) — satır başına bayt üst sınırı (~150 KB).</summary>
    public const int MaxCropEdge = 1024;

    public int Id { get; set; }

    public int PageId { get; set; }

    public LabelingAnswerKeyPage? Page { get; set; }

    /// <summary>Sorgu kolaylığı için kitabın kopyası (sayfa satırının <c>BookId</c>'si).</summary>
    public int BookId { get; set; }

    /// <summary>Anahtar sayfasının dosya sayfası (sayfa satırının kopyası); toplu yüklemede bilinmiyorsa null.</summary>
    public int? FilePage { get; set; }

    /// <summary>Okuma sırası (sayfada yukarıdan aşağı).</summary>
    public int Order { get; set; }

    public int? PrintedPage { get; set; }

    [MaxLength(MaxLabelLength)]
    public string? ActivityLabel { get; set; }

    [MaxLength(MaxLabelLength)]
    public string? ItemLabel { get; set; }

    [MaxLength(MaxAnswerLength)]
    public string? AnswerText { get; set; }

    public bool Visual { get; set; }

    public int? BoxX { get; set; }

    public int? BoxY { get; set; }

    public int? BoxWidth { get; set; }

    public int? BoxHeight { get; set; }

    /// <summary>Sayfa başına en çok görsel cevap (fazlası metin olarak saklanır, kırpmasız) — review D-3.</summary>
    public const int MaxVisualEntriesPerPage = 60;

    /// <summary>Sayfa başına saklanan kırpmaların toplam bayt üst sınırı (aşan maddeler kırpmasız kalır) — review D-3.</summary>
    public const int MaxCropBytesPerPage = 4 * 1024 * 1024;

    /// <summary>Görsel cevabın kırpması (JPEG, uzun kenar ≤ <see cref="MaxCropEdge"/>); yoksa null.</summary>
    public byte[]? CropJpeg { get; set; }

    /// <summary><see cref="CropJpeg"/>'in SHA-256'sı (çözüm önbelleğinin anahtar girdisi).</summary>
    [MaxLength(64)]
    public string? CropSha256 { get; set; }

    [MaxLength(64)]
    public string? EditedByKeycloakId { get; set; }

    [MaxLength(200)]
    public string? EditedByName { get; set; }

    public DateTime? EditedAt { get; set; }

    /// <summary>
    /// Review W6: öğretmenin sildiği madde yumuşak silinir (kim, ne zaman); listelerde ve çözüm girdisinde görünmez, sayfa "düzeltilmiş"
    /// sayılır (yeniden okuma onaysız üzerine yazmaz). Onaylı yeniden okuma (<c>confirmOverwrite</c>) satırları gerçekten siler.
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    [MaxLength(64)]
    public string? DeletedByKeycloakId { get; set; }

    [MaxLength(200)]
    public string? DeletedByName { get; set; }
}
