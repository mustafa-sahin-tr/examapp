using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>Bir sayfadaki soru/şık kutularının nereden geldiği.</summary>
public enum LabelingSource
{
    /// <summary>YOLO (question-detector) tahmini — eğitime gönderim anındaki anlık görüntü, önbellek değil.</summary>
    Model,

    /// <summary>Claude tahmini; model adı başına ayrı satır.</summary>
    Ai,

    /// <summary>Kullanıcının eğitime gönderdiği (kabul edilen) son hali; tek satır, üzerine yazılır.</summary>
    Manual,
}

/// <summary>
/// Bir sayfa için tek kaynaktan gelen kutular. (PageId, Source, ModelName) benzersizdir: AI'da ModelName Claude model
/// id'si, Model/Manual'da boş string.
/// </summary>
public class LabelingResult
{
    public int Id { get; set; }

    public int PageId { get; set; }

    public LabelingPage Page { get; set; } = null!;

    public LabelingSource Source { get; set; }

    [MaxLength(64)]
    public string ModelName { get; set; } = string.Empty;

    /// <summary>question-detector <c>/predict</c> ile aynı şekilde PredictionList (orijinal piksel, şıklar sayfaya göre).</summary>
    public string BoxesJson { get; set; } = "{}";

    /// <summary>Manuel kaydın düzeltmeye hangi kaynaktan başlandığı (Model/Ai); diğer kaynaklarda boş.</summary>
    public LabelingSource? BaseSource { get; set; }

    [MaxLength(64)]
    public string? BaseModelName { get; set; }

    public int? InputTokens { get; set; }

    public int? OutputTokens { get; set; }

    public decimal? EstimatedCostUsd { get; set; }

    /// <summary>Keycloak <c>sub</c>. Admin'in Teacher satırı olmayabilir, bu yüzden FK yok.</summary>
    [MaxLength(64)]
    public string? CreatedByKeycloakId { get; set; }

    /// <summary>Kaydeden kullanıcının o anki görünen adı (token'dan).</summary>
    [MaxLength(200)]
    public string? CreatedByName { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
