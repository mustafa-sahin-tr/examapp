using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>
/// Issue #579: bir etkinlik kırpmasının Claude'dan gelen taslak planı (önbellek). Anahtar (<see cref="CropSha256"/>,
/// <see cref="InteractionType"/>, <see cref="ModelName"/>) benzersizdir; satır yalnız <see cref="PromptHash"/> o tipin etkin
/// prompt parmak iziyle eşleşirse kayıttan döner (bir tipin talimatı değişince yalnız o tipin satırları geçersiz kalır).
/// <c>force</c> ve her canlı sonuç satırın üzerine yazar.
/// </summary>
public class LabelingActivityResult
{
    public int Id { get; set; }

    /// <summary>
    /// Blok kırpmasının SHA-256'sı (<see cref="Services.Labeling.QuestionCropHash"/>, #577 soru kaynağıyla aynı tanım: bankaya
    /// kayıttaki yolla yeniden kodlanmış sayfadan kırpma).
    /// </summary>
    [MaxLength(64)]
    public string CropSha256 { get; set; } = string.Empty;

    [MaxLength(32)]
    public string InteractionType { get; set; } = string.Empty;

    /// <summary>Claude model id'si.</summary>
    [MaxLength(64)]
    public string ModelName { get; set; } = string.Empty;

    /// <summary>Tipin prompt parmak izi (<see cref="Services.Labeling.ActivityPromptSettings.Fingerprint"/>).</summary>
    [MaxLength(64)]
    public string PromptHash { get; set; } = string.Empty;

    /// <summary>Sonucu üreten tip talimatının sürümü (gösterim; 0 = koddaki varsayılan).</summary>
    public int PromptVersion { get; set; }

    /// <summary>InteractionPlan v1 (kutular blok kutusuna göre göreli sayfa pikseli).</summary>
    public string PlanJson { get; set; } = "{}";

    /// <summary>Etkinliğin basılı yönerge cümlesi; yoksa null.</summary>
    public string? Instruction { get; set; }

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    public decimal EstimatedCostUsd { get; set; }

    /// <summary>Keycloak <c>sub</c>. Admin'in Teacher satırı olmayabilir, bu yüzden FK yok.</summary>
    [MaxLength(64)]
    public string? CreatedByKeycloakId { get; set; }

    [MaxLength(200)]
    public string? CreatedByName { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }
}
