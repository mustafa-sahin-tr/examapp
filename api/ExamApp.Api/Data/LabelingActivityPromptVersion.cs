using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>
/// Issue #579: etkinlik yapısı çıkarımının TİP BAŞINA talimatı. Her kayıt o tipin yeni sürümüdür (geçmiş silinmez); etkin olan
/// tipin en yüksek <see cref="Version"/>'ı, kayıt yoksa koddaki varsayılan (sürüm 0). Sistem mesajı, koordinat kuralı ve yanıt
/// şeması kodda sabittir. (<see cref="InteractionType"/>, <see cref="Version"/>) benzersiz.
/// </summary>
public class LabelingActivityPromptVersion
{
    public int Id { get; set; }

    /// <summary><c>matrixChoice</c> | <c>slotFill</c> | <c>dragPlace</c> | <c>match</c>.</summary>
    [MaxLength(32)]
    public string InteractionType { get; set; } = string.Empty;

    public int Version { get; set; }

    /// <summary><c>{width}</c>/<c>{height}</c> gönderilen kırpmanın boyutuyla değiştirilir.</summary>
    public string Instruction { get; set; } = string.Empty;

    [MaxLength(64)]
    public string? CreatedByKeycloakId { get; set; }

    [MaxLength(200)]
    public string? CreatedByName { get; set; }

    public DateTime CreatedAt { get; set; }
}
