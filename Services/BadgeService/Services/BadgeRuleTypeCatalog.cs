using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace BadgeService.Services;

/// <summary>
/// Issue #148 (owner decision #2): structured, per-<c>RuleType</c> schema + strict validation for the
/// admin badge-definition editor, built on the same field names <see cref="BadgeRuleEvaluator"/> already
/// reads (<c>target</c>, <c>subjectId</c>, <c>subjectName</c>) so a badge saved here evaluates exactly as
/// the existing evaluator expects. Unknown/missing/out-of-range fields are rejected with field-level
/// errors (400) rather than silently accepted.
/// </summary>
public static class BadgeRuleTypeCatalog
{
    public const int MinTarget = 1;
    public const int MaxTarget = 1_000_000;

    /// <summary>
    /// Canonical <c>RuleType</c> values accepted by the admin API (case-insensitive). Legacy aliases the
    /// evaluator also understands for already-seeded rows (e.g. "StudyStreak", "ActivityStreak") are
    /// intentionally NOT offered here — new/edited badges always use the canonical name.
    /// </summary>
    private static readonly RuleTypeSchema[] Schemas =
    {
        SimpleTarget("AnswerCount", "Toplam çözülen soru sayısı."),
        SimpleTarget("CorrectStreak", "Arka arkaya yapılan en iyi doğru cevap serisi."),
        SimpleTarget("TotalStudyTimeMinutes", "Toplam çalışma süresi (dakika)."),
        SimpleTarget("TotalCorrectAnswers", "Toplam doğru cevap sayısı."),
        SimpleTarget("ActiveDays", "Toplam aktif gün sayısı."),
        SimpleTarget("DailyStreak", "Üst üste aktif olunan gün sayısı (streak)."),
        SubjectTarget("SubjectAnswerCount", "Belirli bir derste çözülen soru sayısı."),
        SubjectTarget("SubjectCorrectCount", "Belirli bir derste verilen doğru cevap sayısı."),
        SubjectTarget("SubjectStudyTimeMinutes", "Belirli bir derste çalışılan süre (dakika)."),
    };

    public static IReadOnlyList<RuleTypeSchema> All => Schemas;

    public static bool TryGetSchema(string? ruleType, out RuleTypeSchema schema)
    {
        schema = Schemas.FirstOrDefault(s => string.Equals(s.RuleType, ruleType?.Trim(), StringComparison.OrdinalIgnoreCase))!;
        return schema is not null;
    }

    /// <summary>
    /// Strictly validates <paramref name="ruleConfigJson"/> against <paramref name="ruleType"/>'s typed
    /// config record (unknown members rejected, required members enforced, ranges checked) and returns a
    /// re-serialized, canonical JSON body (stable property names/order) to persist.
    /// </summary>
    public static bool TryValidateAndNormalize(
        string? ruleType,
        string? ruleConfigJson,
        out string normalizedRuleConfigJson,
        out List<RuleValidationError> errors,
        ILogger? logger = null)
    {
        errors = new List<RuleValidationError>();
        normalizedRuleConfigJson = string.Empty;

        if (!TryGetSchema(ruleType, out var schema))
        {
            errors.Add(new RuleValidationError("ruleType", $"Bilinmeyen RuleType. Geçerli değerler: {string.Join(", ", Schemas.Select(s => s.RuleType))}"));
            return false;
        }

        if (string.IsNullOrWhiteSpace(ruleConfigJson))
        {
            errors.Add(new RuleValidationError("ruleConfigJson", "RuleConfigJson boş olamaz."));
            return false;
        }

        var jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

        var isSubjectRule = schema.Fields.Any(f => f.Name == "subjectId");

        try
        {
            if (isSubjectRule)
            {
                var config = JsonSerializer.Deserialize<SubjectTargetRuleConfig>(ruleConfigJson, jsonOptions);
                if (config is null)
                {
                    errors.Add(new RuleValidationError("ruleConfigJson", "RuleConfigJson çözümlenemedi."));
                    return false;
                }

                ValidateTarget(config.Target, errors);

                var hasSubjectId = config.SubjectId.HasValue;
                var hasSubjectName = !string.IsNullOrWhiteSpace(config.SubjectName);
                if (!hasSubjectId && !hasSubjectName)
                {
                    errors.Add(new RuleValidationError("subjectId", "subjectId veya subjectName alanlarından en az biri zorunludur."));
                }

                if (hasSubjectId && config.SubjectId!.Value <= 0)
                {
                    errors.Add(new RuleValidationError("subjectId", "subjectId pozitif bir tam sayı olmalıdır."));
                }

                if (errors.Count > 0)
                {
                    return false;
                }

                normalizedRuleConfigJson = JsonSerializer.Serialize(new SubjectTargetRuleConfig
                {
                    Target = config.Target,
                    SubjectId = config.SubjectId,
                    SubjectName = hasSubjectName ? config.SubjectName!.Trim() : null,
                });
                return true;
            }
            else
            {
                var config = JsonSerializer.Deserialize<SimpleTargetRuleConfig>(ruleConfigJson, jsonOptions);
                if (config is null)
                {
                    errors.Add(new RuleValidationError("ruleConfigJson", "RuleConfigJson çözümlenemedi."));
                    return false;
                }

                ValidateTarget(config.Target, errors);

                if (errors.Count > 0)
                {
                    return false;
                }

                normalizedRuleConfigJson = JsonSerializer.Serialize(new SimpleTargetRuleConfig { Target = config.Target });
                return true;
            }
        }
        catch (JsonException ex)
        {
            // Security review follow-up (L2): JsonException.Message can echo back raw request content
            // (e.g. the offending property value) — never put it in the API response. ex.Path narrows
            // down the location without leaking content; the full exception goes to the log only.
            logger?.LogWarning(ex, "RuleConfigJson doğrulaması başarısız (ruleType={RuleType}, path={Path})", ruleType, ex.Path);
            var location = string.IsNullOrEmpty(ex.Path) ? string.Empty : $" (konum: {ex.Path})";
            errors.Add(new RuleValidationError("ruleConfigJson", $"RuleConfigJson geçersiz veya bilinmeyen bir alan içeriyor{location}."));
            return false;
        }
    }

    private static void ValidateTarget(int? target, List<RuleValidationError> errors)
    {
        if (!target.HasValue)
        {
            errors.Add(new RuleValidationError("target", "target zorunludur."));
            return;
        }

        if (target.Value < MinTarget || target.Value > MaxTarget)
        {
            errors.Add(new RuleValidationError("target", $"target {MinTarget} ile {MaxTarget} arasında olmalıdır."));
        }
    }

    private static RuleTypeSchema SimpleTarget(string ruleType, string description) => new(
        ruleType,
        description,
        new[]
        {
            new RuleTypeFieldSchema("target", "integer", true, MinTarget, MaxTarget, null),
        });

    private static RuleTypeSchema SubjectTarget(string ruleType, string description) => new(
        ruleType,
        description,
        new[]
        {
            new RuleTypeFieldSchema("target", "integer", true, MinTarget, MaxTarget, null),
            new RuleTypeFieldSchema("subjectId", "integer", false, 1, null, null),
            new RuleTypeFieldSchema("subjectName", "string", false, null, null, null),
        });
}

/// <summary>Field-level validation failure (maps 1:1 to the CRUD API's 400 error body).</summary>
public sealed record RuleValidationError(string Field, string Message);

/// <summary>Schema descriptor returned by GET /api/admin/badge-definitions/rule-types.</summary>
public sealed record RuleTypeSchema(string RuleType, string Description, IReadOnlyList<RuleTypeFieldSchema> Fields);

/// <summary>
/// One editable parameter of a RuleType. <see cref="AllowedValues"/> is reserved for future closed sets
/// (e.g. subject ids) — left null today: BadgeService does not synchronously call the exam API for the
/// subject catalog (no service-to-service HTTP calls), so the admin UI is expected to source subject
/// options from the exam API directly and only send subjectId/subjectName here; BadgeService validates
/// shape/format only, not existence.
/// </summary>
public sealed record RuleTypeFieldSchema(string Name, string Type, bool Required, int? Min, int? Max, IReadOnlyList<string>? AllowedValues);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SimpleTargetRuleConfig
{
    [JsonPropertyName("target")]
    public int? Target { get; init; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SubjectTargetRuleConfig
{
    [JsonPropertyName("target")]
    public int? Target { get; init; }

    [JsonPropertyName("subjectId")]
    public int? SubjectId { get; init; }

    [JsonPropertyName("subjectName")]
    public string? SubjectName { get; init; }
}
