using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>
/// Issue #580: işaretleme ekranındaki bir etkinlik taslağının çözümü (bankaya yazılmadan önce; #581 bankaya kayıtta Question'a taşır).
/// Önbellek anahtarı (<see cref="CropSha256"/>, <see cref="InteractionType"/>, <see cref="PlanHash"/>, <see cref="Mode"/>,
/// <see cref="KeyInputHash"/>, <see cref="ModelName"/>) benzersiz DEĞİLDİR (review W5: geçmiş tutulur); kayıttan dönen, anahtarı ve o
/// modun etkin talimat parmak izi (<see cref="PromptHash"/>) tutan EN YENİ satırdır. Canlı sonuç (ya da <c>force</c>) en yeni satırın
/// üzerine yalnız o satır onaylanmamış ve insan düzeltmesi görmemişse yazar; aksi halde yeni satır açılır (onaylayan / düzelten
/// kaybolmaz). Çözüm taslak id'lerine göredir (<c>PrepareForStorage</c> öncesi).
/// </summary>
public class LabelingActivitySolution
{
    public const string ModeKey = "key";
    public const string ModeAi = "ai";

    public int Id { get; set; }

    /// <summary>Etkinlik bloğu kırpmasının SHA-256'sı (#579 <c>LabelingActivityResult.CropSha256</c> ile aynı tanım).</summary>
    [MaxLength(64)]
    public string CropSha256 { get; set; } = string.Empty;

    [MaxLength(32)]
    public string InteractionType { get; set; } = string.Empty;

    /// <summary>Taslak planın SHA-256'sı (yeniden serileştirilmiş JSON).</summary>
    [MaxLength(64)]
    public string PlanHash { get; set; } = string.Empty;

    /// <summary>Çözümün ait olduğu taslak plan (InteractionPlan v1, taslak id'leri).</summary>
    public string PlanJson { get; set; } = "{}";

    /// <summary><see cref="ModeKey"/> | <see cref="ModeAi"/> — hangi talimatla üretildi (kaynak insan düzeltmesiyle değişse de kalır).</summary>
    [MaxLength(8)]
    public string Mode { get; set; } = ModeAi;

    /// <summary>Anahtar girdisinin SHA-256'sı (madde metinleri + görsel kırpma hash'leri); <c>ai</c> modunda boş string.</summary>
    [MaxLength(64)]
    public string KeyInputHash { get; set; } = string.Empty;

    /// <summary>Kullanılan anahtar maddelerinin id'leri (JSON dizi, gösterim); <c>ai</c> modunda null.</summary>
    public string? KeyEntryIdsJson { get; set; }

    [MaxLength(64)]
    public string ModelName { get; set; } = string.Empty;

    [MaxLength(64)]
    public string PromptHash { get; set; } = string.Empty;

    /// <summary>Çözümü üreten talimatın sürümü (onay ekranında gösterilir; 0 = koddaki varsayılan).</summary>
    public int? PromptVersion { get; set; }

    /// <summary>Tipin çözüm biçimi (<c>matrixChoice</c> <c>{"r1":["c1"]}</c> …); Claude yanıt veremediyse null.</summary>
    public string? SolutionJson { get; set; }

    public SolutionSource Source { get; set; } = SolutionSource.AI;

    public SolutionStatus Status { get; set; } = SolutionStatus.Pending;

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    public decimal EstimatedCostUsd { get; set; }

    [MaxLength(64)]
    public string? CreatedByKeycloakId { get; set; }

    [MaxLength(200)]
    public string? CreatedByName { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    [MaxLength(64)]
    public string? EditedByKeycloakId { get; set; }

    [MaxLength(200)]
    public string? EditedByName { get; set; }

    public DateTime? EditedAt { get; set; }

    [MaxLength(64)]
    public string? ApprovedByKeycloakId { get; set; }

    [MaxLength(200)]
    public string? ApprovedByName { get; set; }

    public DateTime? ApprovedAt { get; set; }
}
