using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace ExamApp.Api.Models.Dtos.Labeling;

// Issue #580: cevap anahtarı okuma ve etkinlik çözümü (işaretleme ekranı, Admin + QuestionTrainer). Durum değerleri küçük harf
// (pending | approved), kaynak değerleri enum adı (Key | AI | Human).

/// <summary><c>POST api/labeling/answer-key/read</c> — kitabın cevap anahtarı sayfasını Claude ile okur.</summary>
public class AnswerKeyReadRequestDto
{
    public int BookId { get; set; }

    /// <summary>
    /// Kitabın DOSYA sayfası; verilirse kitabın cevap anahtarı aralığında olmalı (yoksa 400 <c>LabelingNotAnswerKeyPage</c>). Toplu
    /// yüklemede bilinmiyorsa null: okuma (kitap, görsel SHA-256) ile saklanır, aynı görsel yeniden yüklenince kayıttan döner.
    /// </summary>
    public int? FilePage { get; set; }

    /// <summary>Anahtar sayfasının görseli, base64 (data URL öneki kabul edilir).</summary>
    [Required]
    public string ImageBase64 { get; set; } = string.Empty;

    [Required, MaxLength(64)]
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// true: kayıt olsa da Claude'a yeniden sor ve sayfanın maddelerini değiştir. Sayfada öğretmen düzeltmesi / silmesi varsa ayrıca
    /// <see cref="ConfirmOverwrite"/> gerekir, yoksa 409 <c>LabelingAnswerKeyHasEdits</c>.
    /// </summary>
    public bool Force { get; set; }

    /// <summary>Review W6: <see cref="Force"/> ile düzeltilmiş maddelerin üzerine yazmayı onaylar.</summary>
    public bool ConfirmOverwrite { get; set; }
}

/// <summary>Sayfa pikseli kutu.</summary>
public record LabelingBoxDto(int X, int Y, int Width, int Height);

/// <summary>
/// Cevap anahtarı maddesi. <see cref="PrintedPage"/>: maddenin ait olduğu ETKİNLİĞİN anahtarda yazan bastırılmış sayfası (yoksa null).
/// <see cref="Box"/>: görsel cevabın anahtar sayfasındaki kutusu (sayfa pikseli); metin cevapta null.
/// </summary>
public record AnswerKeyEntryDto(
    int Id,
    int? FilePage,
    int? PrintedPage,
    string? ActivityLabel,
    string? ItemLabel,
    string? AnswerText,
    bool Visual,
    LabelingBoxDto? Box,
    string? EditedByName = null,
    DateTime? EditedAt = null);

/// <summary>
/// <c>Origin</c>: <c>live</c> (Claude'a soruldu) | <c>cached</c> (kayıtlı okuma; ücretsiz, maliyet/token ilk okumanın).
/// <see cref="Stale"/>: kayıtlı okuma başka görsel / model / <c>answerKey</c> talimatıyla yapılmış ama sayfada öğretmen düzeltmesi
/// olduğu için yeniden okunmadı (yenilemek için <c>force</c> + <c>confirmOverwrite</c>). <see cref="HasEdits"/>: sayfada düzeltilmiş ya da
/// silinmiş madde var.
/// </summary>
public record AnswerKeyReadResponseDto(
    string Origin,
    int PromptVersion,
    decimal EstimatedCostUsd,
    IReadOnlyList<AnswerKeyEntryDto> Entries,
    int InputTokens,
    int OutputTokens,
    bool Stale = false,
    bool HasEdits = false);

/// <summary><c>GET api/labeling/books/{bookId}/answer-key</c>.</summary>
public record AnswerKeyListResponseDto(IReadOnlyList<AnswerKeyEntryDto> Entries, int Total);

/// <summary><c>PUT api/labeling/answer-key/entries/{id}</c> — öğretmenin okuma düzeltmesi (alanlar bütün olarak yazılır).</summary>
public class AnswerKeyEntryUpdateRequestDto
{
    [Range(1, 100_000)]
    public int? PrintedPage { get; set; }

    [MaxLength(64)]
    public string? ActivityLabel { get; set; }

    [MaxLength(64)]
    public string? ItemLabel { get; set; }

    [MaxLength(1000)]
    public string? AnswerText { get; set; }
}

/// <summary>
/// <c>POST api/labeling/activity-solution</c> — taslak etkinliğin çözümü. <see cref="Mode"/> <c>key</c>: seçilen anahtar maddelerinin
/// metni + görsel kırpmaları ve etkinlik kırpması (<c>solveFromKey</c> talimatı, kaynak Key); <c>ai</c>: yalnız etkinlik kırpması
/// (<c>solveAi</c>, kaynak AI). Çözüm her iki durumda onay bekler.
/// </summary>
public class ActivitySolutionRequestDto
{
    public const int MaxKeyEntries = 60;

    /// <summary>Etkinliğin bulunduğu sayfa, base64.</summary>
    [Required]
    public string ImageBase64 { get; set; } = string.Empty;

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    /// <summary><c>matrixChoice</c> | <c>slotFill</c> | <c>dragPlace</c> | <c>match</c>.</summary>
    public string? InteractionType { get; set; }

    /// <summary>Taslak plan (InteractionPlan v1, taslak id'leri); tipin kayıt doğrulamasından geçmeli.</summary>
    public JsonElement? Plan { get; set; }

    [Required, MaxLength(64)]
    public string Model { get; set; } = string.Empty;

    public bool Force { get; set; }

    /// <summary><c>key</c> | <c>ai</c>.</summary>
    public string? Mode { get; set; }

    /// <summary><c>key</c> modunda zorunlu.</summary>
    public ActivitySolutionKeyDto? Key { get; set; }
}

public class ActivitySolutionKeyDto
{
    /// <summary>1..60 anahtar maddesi (okuma sırasıyla verilir).</summary>
    public List<int>? EntryIds { get; set; }
}

/// <summary>
/// Taslak çözüm. <see cref="Origin"/>: <c>live</c> | <c>cached</c> | null (okuma / düzenleme / onay yanıtı). <see cref="Error"/> doluysa
/// Claude bu etkinliği çözemedi (<see cref="DraftSolutionId"/> null, hiçbir şey kaydedilmedi). <see cref="Valid"/>: çözüm planla
/// tipin kayıt doğrulamasından (<c>ValidateDefinition</c>) geçiyor mu — geçmeyen taslak düzeltilmeden onaylanamaz.
/// <see cref="PromptVersion"/> onay ekranında gösterilir.
/// </summary>
public record LabelingActivitySolutionDto(
    int? DraftSolutionId,
    JsonElement? Solution,
    string? Source,
    string? Status,
    int? PromptVersion,
    string? Origin,
    bool Valid,
    IReadOnlyList<string> ValidationErrors,
    decimal? EstimatedCostUsd,
    string? Error,
    string? CropSha256 = null,
    string? InteractionType = null,
    string? PlanHash = null,
    JsonElement? Plan = null,
    string? Mode = null,
    string? ModelName = null,
    IReadOnlyList<int>? KeyEntryIds = null,
    int? InputTokens = null,
    int? OutputTokens = null,
    string? CreatedByName = null,
    DateTime? CreatedAt = null,
    string? EditedByName = null,
    DateTime? EditedAt = null,
    string? ApprovedByName = null,
    DateTime? ApprovedAt = null);

/// <summary><c>GET api/labeling/activity-solution</c> — en son güncellenen önce, en çok 10.</summary>
public record ActivitySolutionListDto(IReadOnlyList<LabelingActivitySolutionDto> Solutions);

/// <summary>
/// <c>PUT api/labeling/activity-solution/{id}</c> — insan düzeltmesi (taslak planına göre geçerli olmalı). <see cref="Approve"/> false:
/// kaynak aynı kalır, durum pending'e döner; true: düzelten onaylar (kaynak Human, onaylayan + zaman kaydedilir).
/// </summary>
public class ActivitySolutionUpdateRequestDto
{
    public JsonElement? Solution { get; set; }

    public bool Approve { get; set; }
}

/// <summary>
/// Issue #580 (kapsam genişlemesi): <c>POST api/labeling/books/{bookId}/answer-key/lookup</c> — bir sayfadaki soruların / etkinliklerin
/// anahtar maddelerini tek istekte arar (sayfa başına N GET yerine).
/// </summary>
public class AnswerKeyLookupRequestDto
{
    public const int MaxItems = 60;

    /// <summary>Soruların / etkinliklerin bulunduğu kitap sayfası (DOSYA sayfası, 1'den).</summary>
    public int FilePage { get; set; }

    /// <summary>Bastırılmış sayfa; yoksa <see cref="FilePage"/>'ten (ofsetle) türetilir.</summary>
    public int? PrintedPage { get; set; }

    /// <summary>Sayfanın basılı test / bölüm başlığı ("TEST 01"); çoktan seçmeli eşlemesi için (<c>section_label</c>).</summary>
    [MaxLength(64)]
    public string? SectionLabel { get; set; }

    /// <summary>1..60 öğe; yanıt aynı sırada.</summary>
    public List<AnswerKeyLookupItemDto>? Items { get; set; }
}

public class AnswerKeyLookupItemDto
{
    /// <summary>İstemcinin öğe kimliği (tekil, ≤ 64); yanıtta aynen döner.</summary>
    public string? Key { get; set; }

    /// <summary>
    /// <c>mcq</c> | <c>activity</c>; boşsa çıkarılır: <see cref="AnswerKeyLookupRequestDto.SectionLabel"/> verildi ve etiket çıplak
    /// numaraysa <c>mcq</c>, aksi halde <c>activity</c>.
    /// </summary>
    public string? Kind { get; set; }

    /// <summary>Etkinlik etiketi ("ETKİNLİK 24") ya da çoktan seçmelide soru numarası ("5") — tespitteki <c>label</c>.</summary>
    public string? Label { get; set; }

    /// <summary>Etkinlikte isteğe bağlı madde etiketi ("a", "2").</summary>
    public string? ItemLabel { get; set; }
}

public record AnswerKeyLookupResultDto(string Key, IReadOnlyList<AnswerKeyEntryDto> Entries);

public record AnswerKeyLookupResponseDto(IReadOnlyList<AnswerKeyLookupResultDto> Results);
