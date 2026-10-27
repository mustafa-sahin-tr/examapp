using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ExamApp.Api.Models.Dtos.Labeling;

/// <summary>
/// question-detector <c>/predict</c> yanıtıyla aynı şekil (snake_case alanlar) — UI'daki <c>applyPrediction</c> değişmeden
/// kullanır. Koordinatlar orijinal görsel pikselidir; şıklar da sayfaya göre mutlaktır.
/// </summary>
public class PredictionDto
{
    [JsonPropertyName("class_id")]
    public int ClassId { get; set; }

    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("y")]
    public int Y { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("subpredictions")]
    public List<PredictionDto> Subpredictions { get; set; } = new();

    /// <summary>
    /// Issue #578: blok türü — <c>mcq_question</c> | <c>activity</c> | <c>info_box</c> | <c>answer_key</c> | <c>noise</c>
    /// (<see cref="Services.Labeling.LabelingBlocks"/>). null = <c>mcq_question</c> (YOLO, eski kayıtlar). Yalnız üst düzey kutu.
    /// </summary>
    [JsonPropertyName("kind")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Kind { get; set; }

    /// <summary>Issue #578: bileşen ailesi <c>A</c>..<c>I</c> (epic #569); yalnız <c>kind = activity</c> iken.</summary>
    [JsonPropertyName("component")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Component { get; set; }

    /// <summary>
    /// Issue #578: <c>matrixChoice</c> | <c>slotFill</c> | <c>dragPlace</c> | <c>match</c>; yalnız <c>kind = activity</c> iken ve
    /// bileşenle tutarlıysa (A → matrixChoice, B → slotFill, C → dragPlace | match), aksi halde null.
    /// </summary>
    [JsonPropertyName("interaction_type")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InteractionType { get; set; }

    /// <summary>
    /// Issue #580 (kapsam genişlemesi): bloğun basılı etiketi — etkinlikte "EŞLEŞTİRME - 1", "Uygulama - 2", "ETKİNLİK 24"; çoktan
    /// seçmelide soru numarası ("5"). En çok <see cref="MaxLabelLength"/> karakter; yoksa null (yazılmaz). Cevap anahtarı eşlemesinin
    /// girdisi (<c>POST books/{id}/answer-key/lookup</c>).
    /// </summary>
    [JsonPropertyName("label")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Label { get; set; }

    public const int MaxLabelLength = 64;
}

public class PredictionListDto
{
    [JsonPropertyName("success")]
    public bool Success { get; set; } = true;

    [JsonPropertyName("predictions")]
    public List<PredictionDto> Predictions { get; set; } = new();

    /// <summary>
    /// Issue #580 (kapsam genişlemesi): sayfanın basılı test / bölüm başlığı ("TEST 01", "Destekleme Testi 2"); yoksa null (yazılmaz).
    /// Çoktan seçmeli sorunun anahtar eşlemesinde <c>sectionLabel</c>. En çok <see cref="PredictionDto.MaxLabelLength"/> karakter.
    /// </summary>
    [JsonPropertyName("section_label")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? SectionLabel { get; set; }
}

/// <summary><c>GET api/labeling/ai-models</c> — UI'daki model listesi (ad + 1M token fiyatı).</summary>
public record LabelingAiModelDto(string Id, string DisplayName, decimal InputUsdPerMTok, decimal OutputUsdPerMTok, bool IsDefault);

public record LabelingResultDto(
    string Source,
    string ModelName,
    PredictionListDto Predictions,
    string? BaseSource,
    string? BaseModelName,
    int? InputTokens,
    int? OutputTokens,
    decimal? EstimatedCostUsd,
    int? PromptVersion,
    string? CreatedByName,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int? BasePromptVersion = null);

/// <summary>
/// <c>GET api/labeling/pages/{sha256}</c> — sayfanın kayıtlı tüm kaynakları. Kayıt yoksa 404. <c>Bank</c>: sayfa soru
/// bankasına kaydedildiyse son kaydın özeti.
/// </summary>
public record LabelingPageDto(
    string ImageSha256,
    int Width,
    int Height,
    IReadOnlyList<LabelingResultDto> Results,
    LabelingBankInfoDto? Bank = null);

/// <summary>Sayfanın soru bankasına son kaydı (issue #470).</summary>
public record LabelingBankInfoDto(DateTime SavedAt, string? SavedByName, int QuestionCount);

/// <summary>Bankaya kaydedilecek kutu (orijinal sayfa pikseli, şıklar da sayfaya göre mutlak).</summary>
public class LabelingBankBoxDto
{
    public int X { get; set; }

    public int Y { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }
}

public class LabelingBankQuestionDto : LabelingBankBoxDto
{
    /// <summary>Şıklar okunma sırasıyla (A, B, C, ...).</summary>
    [Required, MaxLength(10)]
    public List<LabelingBankBoxDto> Options { get; set; } = new();

    /// <summary><see cref="Options"/> içindeki doğru şıkkın sırası.</summary>
    public int CorrectOptionIndex { get; set; } = -1;

    /// <summary>Issue #577: etkinlik etiketi ("Etkinlik 9"); yalnız <c>BookId</c> verildiyse saklanır.</summary>
    [MaxLength(Data.QuestionSource.MaxLabelLength)]
    public string? ActivityLabel { get; set; }

    /// <summary>Issue #577: madde etiketi ("3", "b"); yalnız <c>BookId</c> verildiyse saklanır.</summary>
    [MaxLength(Data.QuestionSource.MaxLabelLength)]
    public string? ItemLabel { get; set; }
}

/// <summary>
/// <c>POST api/labeling/pages/bank</c> — sayfadaki soruları teste bağlamadan soru bankasına kaydeder (issue #470).
/// </summary>
public class LabelingBankSaveRequestDto
{
    /// <summary>Ham görsel, base64 (data URL öneki kabul edilir).</summary>
    [Required]
    public string ImageBase64 { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public List<LabelingBankQuestionDto> Questions { get; set; } = new();

    /// <summary>
    /// Issue #565: sayfanın sınıfı (zorunlu, var olan bir <c>Grade</c>). Sınıflandırıcı soruları yalnız bu sınıfın
    /// konularına yerleştirir (<c>QuestionCreatedEvent.GradeId</c>).
    /// Issue #577: kitap sayfasında verilmezse içindekilerdeki sınıf kullanılır; verilirse o kazanır. İkisi de yoksa 400.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int? GradeId { get; set; }

    /// <summary>
    /// Issue #577: sayfanın kitabı. <see cref="FilePage"/> ile birlikte verilir: ders/konu/alt konu içindekilerden gelir
    /// (<c>ClassificationSource = Book</c>), <c>BookName</c> kitap adı olur, her soruya <c>QuestionSource</c> yazılır ve aynı
    /// kırpma bankada varsa (<c>Force</c> değilse) 409 <c>LabelingCropAlreadyInBank</c>.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int? BookId { get; set; }

    /// <summary>Issue #577: kitabın dosya sayfası (1'den başlar). <see cref="BookId"/> ile birlikte zorunlu.</summary>
    [Range(1, int.MaxValue)]
    public int? FilePage { get; set; }

    /// <summary>true: sayfa (ya da kırpma) daha önce bankaya kaydedildiyse de yeniden kaydet (sorular ikinci kez oluşur).</summary>
    public bool Force { get; set; }
}

/// <summary>
/// <c>TocApplied</c> (issue #577): kitap sayfasının içindekiler ders/konusu sorulara uygulandı (sınıf içindekilerle aynı,
/// satır silinmiş taksonomiye işaret etmiyor ve en az konu var → <c>ClassificationSource = Book</c>).
/// </summary>
public record LabelingBankSaveResponseDto(string ImageSha256, LabelingBankInfoDto Bank, bool TocApplied = false);

public class LabelingAiDetectRequestDto
{
    /// <summary>Ham görsel, base64 (data URL öneki kabul edilir).</summary>
    [Required]
    public string ImageBase64 { get; set; } = string.Empty;

    /// <summary>Claude model id'si; yapılandırmadaki izinli listede olmalı.</summary>
    [Required, MaxLength(64)]
    public string Model { get; set; } = string.Empty;

    /// <summary>true: kayıt olsa da Claude'a yeniden sor ve kaydın üzerine yaz.</summary>
    public bool Force { get; set; }
}

/// <summary><c>Origin</c>: <c>live</c> (Claude'a gerçekten soruldu) veya <c>cached</c> (kayıtlı sonuç, maliyet yok).</summary>
public record LabelingAiDetectResponseDto(string Origin, string ImageSha256, LabelingResultDto Result);

/// <summary>Claude ön-etiketleme prompt'unun bir sürümü. <c>Version</c> 0 = koddaki varsayılanlar.</summary>
public record LabelingPromptDto(
    int Version,
    string QuestionInstruction,
    string OptionInstruction,
    int QuestionPadding,
    int OptionPadding,
    bool ExpandQuestionToOptions,
    string? CreatedByName,
    DateTime? CreatedAt);

/// <summary><c>GET/PUT api/labeling/prompt</c>: etkin sürüm, varsayılanlar ve son sürümler (yeniden eskiye).</summary>
public record LabelingPromptStateDto(LabelingPromptDto Active, LabelingPromptDto Defaults, IReadOnlyList<LabelingPromptDto> History);

/// <summary><c>PUT api/labeling/prompt</c> — yeni sürüm olarak kaydedilir (eskiler silinmez).</summary>
public class LabelingSavePromptRequestDto
{
    /// <summary>Soru aşaması talimatı; <c>{width}</c>/<c>{height}</c> yer tutucuları desteklenir.</summary>
    [Required, MaxLength(4000)]
    public string QuestionInstruction { get; set; } = string.Empty;

    /// <summary>Şık aşaması talimatı; aynı yer tutucular.</summary>
    [Required, MaxLength(4000)]
    public string OptionInstruction { get; set; } = string.Empty;

    [Range(0, 50)]
    public int QuestionPadding { get; set; }

    [Range(0, 50)]
    public int OptionPadding { get; set; }

    public bool ExpandQuestionToOptions { get; set; }
}

/// <summary><c>PUT api/labeling/pages/manual</c> — eğitime gönderilen son hal (+ o anki YOLO tahmini).</summary>
public class LabelingSaveManualRequestDto
{
    [Required]
    public string ImageBase64 { get; set; } = string.Empty;

    [Required]
    public PredictionListDto Predictions { get; set; } = new();

    /// <summary>Düzeltmeye başlanan kaynak: <c>Model</c> veya <c>Ai</c> (yoksa boş).</summary>
    [MaxLength(16)]
    public string? BaseSource { get; set; }

    [MaxLength(64)]
    public string? BaseModelName { get; set; }

    /// <summary>Başlangıç kaynağı <c>Ai</c> ise o Claude sonucunun prompt sürümü.</summary>
    public int? BasePromptVersion { get; set; }

    /// <summary>Gönderim anındaki YOLO tahmini; verilirse Model kaydı olarak saklanır.</summary>
    public PredictionListDto? ModelSnapshot { get; set; }
}
