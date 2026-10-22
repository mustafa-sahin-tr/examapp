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
    string? CreatedByName,
    DateTime CreatedAt,
    DateTime UpdatedAt);

/// <summary><c>GET api/labeling/pages/{sha256}</c> — sayfanın kayıtlı tüm kaynakları. Kayıt yoksa 404.</summary>
public record LabelingPageDto(string ImageSha256, int Width, int Height, IReadOnlyList<LabelingResultDto> Results);

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

    /// <summary>Gönderim anındaki YOLO tahmini; verilirse Model kaydı olarak saklanır.</summary>
    public PredictionListDto? ModelSnapshot { get; set; }
}
