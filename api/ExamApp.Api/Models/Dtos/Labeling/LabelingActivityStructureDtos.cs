using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace ExamApp.Api.Models.Dtos.Labeling;

/// <summary>
/// Issue #579: <c>POST api/labeling/activity-structure</c> — sayfadaki etkinlik bloklarının taslak etkileşim planı. Her blok
/// kendi tipinin talimatı + şemasıyla ayrı bir Claude çağrısına gider (paralel, <c>Anthropic:MaxParallelCrops</c>); aynı kırpma
/// + tip + model + o tipin prompt'u için kayıtlı sonuç varsa (ve <see cref="Force"/> değilse) ücretsiz döner.
/// </summary>
public class ActivityStructureRequestDto
{
    public const int MaxBlocks = 20;

    /// <summary>Bütün sayfa, base64 (data URL öneki kabul edilir).</summary>
    [Required]
    public string ImageBase64 { get; set; } = string.Empty;

    /// <summary>Claude model id'si; yapılandırmadaki izinli listede olmalı.</summary>
    [Required, MaxLength(64)]
    public string Model { get; set; } = string.Empty;

    /// <summary>true: kayıt olsa da her bloğu Claude'a yeniden sor ve kaydın üzerine yaz.</summary>
    public bool Force { get; set; }

    /// <summary>1..<see cref="MaxBlocks"/> blok; yanıt aynı sırada döner.</summary>
    public List<ActivityStructureBlockDto>? Blocks { get; set; }
}

/// <summary>Bir etkinlik bloğu: istemci kimliği, sayfa pikseli kutusu ve etkileşim tipi.</summary>
public class ActivityStructureBlockDto
{
    public const int MaxKeyLength = 64;

    /// <summary>İstemcinin blok kimliği (istekte tekil, ≤ 64 karakter); yanıtta aynen döner.</summary>
    public string? Key { get; set; }

    public double X { get; set; }

    public double Y { get; set; }

    public double Width { get; set; }

    public double Height { get; set; }

    /// <summary><c>matrixChoice</c> | <c>slotFill</c> | <c>dragPlace</c> | <c>match</c> | <c>textSelect</c> (#582).</summary>
    public string? InteractionType { get; set; }
}

/// <summary>
/// Bir bloğun sonucu. <c>Status</c>: <c>ok</c> | <c>error</c> (yalnız bu blokta Claude başarısız; <see cref="Error"/> yerelleştirilmiş
/// mesaj). <c>Origin</c>: <c>live</c> (Claude'a gerçekten soruldu) | <c>cached</c> (kayıtlı sonuç, maliyet yok; token/maliyet
/// sonucu ilk üreten çağrınındır) | null (hata). <see cref="Plan"/>: tipin InteractionPlan v1'i, kutular BLOK kutusuna göre
/// göreli sayfa pikseli (çözüm yok, #580). <see cref="Valid"/>/<see cref="ValidationErrors"/>: tipin kayıt doğrulaması
/// (<c>ValidateDefinition</c>) — taslak geçersiz olabilir, yine döner.
/// </summary>
public record ActivityStructureResultDto(
    string Key,
    string InteractionType,
    string Status,
    string? Origin,
    JsonElement? Plan,
    string? Instruction,
    bool Valid,
    IReadOnlyList<string> ValidationErrors,
    int? PromptVersion,
    int? InputTokens,
    int? OutputTokens,
    decimal? EstimatedCostUsd,
    string? Error);

public record ActivityStructureResponseDto(IReadOnlyList<ActivityStructureResultDto> Results);

/// <summary>Bir tipin talimat sürümü; <c>Version</c> 0 = koddaki varsayılan (<c>CreatedByName</c>/<c>CreatedAt</c> null).</summary>
public record ActivityPromptVersionDto(int Version, string Instruction, string? CreatedByName, DateTime? CreatedAt);

/// <summary>Bir tipin etkin talimatı, koddaki varsayılanı ve son sürümleri (yeniden eskiye).</summary>
public record ActivityPromptTypeStateDto(
    string InteractionType,
    ActivityPromptVersionDto Active,
    ActivityPromptVersionDto Defaults,
    IReadOnlyList<ActivityPromptVersionDto> History);

/// <summary><c>GET api/labeling/activity-prompts</c>.</summary>
public record ActivityPromptStateDto(IReadOnlyList<ActivityPromptTypeStateDto> Types);

/// <summary><c>PUT api/labeling/activity-prompts/{interactionType}</c>; <c>{width}</c>/<c>{height}</c> yer tutucuları desteklenir.</summary>
public class ActivitySavePromptRequestDto
{
    public string? Instruction { get; set; }
}
