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
}

public class PredictionListDto
{
    [JsonPropertyName("success")]
    public bool Success { get; set; } = true;

    [JsonPropertyName("predictions")]
    public List<PredictionDto> Predictions { get; set; } = new();
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
    /// </summary>
    [Range(1, int.MaxValue)] // eksik alan 0 olarak bağlanır → 400
    public int GradeId { get; set; }

    /// <summary>true: sayfa daha önce bankaya kaydedildiyse de yeniden kaydet (sorular ikinci kez oluşur).</summary>
    public bool Force { get; set; }
}

public record LabelingBankSaveResponseDto(string ImageSha256, LabelingBankInfoDto Bank);

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
