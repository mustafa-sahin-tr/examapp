using System;
using System.Collections.Generic;
using ExamApp.Api.Services.Interactions;

namespace ExamApp.Api.Services.Labeling;

/// <summary>
/// Issue #578: işaretleme ekranında sayfa bloklarının türü. Claude 1. aşaması her bloğa <c>kind</c>, etkinlikse bileşen ailesi
/// (epic #569, A–I) ve tahmini etkileşim tipi yazar. <c>kind = null</c> (YOLO, eski kayıtlar) <see cref="McqQuestion"/> sayılır.
/// Yalnız üst düzey kutular taşır; şıklar (subpredictions) taşımaz.
/// </summary>
public static class LabelingBlocks
{
    public const string McqQuestion = "mcq_question";
    public const string Activity = "activity";
    public const string InfoBox = "info_box";
    public const string AnswerKey = "answer_key";
    public const string Noise = "noise";

    public static IReadOnlySet<string> Kinds { get; } =
        new HashSet<string>(StringComparer.Ordinal) { McqQuestion, Activity, InfoBox, AnswerKey, Noise };

    /// <summary>Bileşen aileleri: A Matris seçim … I Özel simülasyon.</summary>
    public static IReadOnlySet<string> Components { get; } =
        new HashSet<string>(StringComparer.Ordinal) { "A", "B", "C", "D", "E", "F", "G", "H", "I" };

    /// <summary>Kontrolcüsü yazılmış etkileşim tipleri (grader sabitleri).</summary>
    public static IReadOnlySet<string> InteractionTypes { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        MatrixChoiceGrader.Type, SlotFillGrader.Type, DragPlaceGrader.Type, MatchGrader.Type, TextSelectGrader.Type,
    };

    /// <summary>
    /// Bileşen ↔ etkileşim tipi tutarlılığı: A → matrixChoice, B → slotFill, C → dragPlace | match, D → textSelect (#582).
    /// Diğer ailelerin henüz kontrolcüsü yok (her tip tutarsız).
    /// </summary>
    public static bool IsConsistent(string? component, string interactionType) => component switch
    {
        "A" => interactionType == MatrixChoiceGrader.Type,
        "B" => interactionType == SlotFillGrader.Type,
        "C" => interactionType == DragPlaceGrader.Type || interactionType == MatchGrader.Type,
        "D" => interactionType == TextSelectGrader.Type,
        _ => false,
    };

    /// <summary>Normalleştirilmiş blok türü (bkz. <see cref="Normalize"/>).</summary>
    public readonly record struct Classification(string Kind, string? Component, string? InteractionType, bool UnknownKind);

    /// <summary>
    /// Claude'un yazdığı değerleri sunucu kuralına çeker: bilinmeyen <c>kind</c> → <see cref="Noise"/> (<c>UnknownKind</c>;
    /// çağıran loglar). Bileşen ve etkileşim tipi yalnız <see cref="Activity"/>'de kalır; bileşen A–I dışıysa null. Etkileşim
    /// tipi bileşenden türetilir: A/B/D tek tipli (model ne yazarsa yazsın; sayfa prompt'u D için "other" ister), C'de modelin
    /// dragPlace/match seçimi geçerliyse o, değilse null; E–I null.
    /// </summary>
    public static Classification Normalize(string? kind, string? component, string? interactionType)
    {
        var normalizedKind = kind?.Trim().ToLowerInvariant() ?? string.Empty;
        var unknown = !Kinds.Contains(normalizedKind);
        if (unknown)
            normalizedKind = Noise;
        if (normalizedKind != Activity)
            return new Classification(normalizedKind, null, null, unknown);

        var normalizedComponent = component?.Trim().ToUpperInvariant();
        if (normalizedComponent is null || !Components.Contains(normalizedComponent))
            return new Classification(Activity, null, null, false);

        var requested = CanonicalInteractionType(interactionType);
        var derived = normalizedComponent switch
        {
            "A" => MatrixChoiceGrader.Type,
            "B" => SlotFillGrader.Type,
            "D" => TextSelectGrader.Type,
            "C" when requested is not null && IsConsistent("C", requested) => requested,
            _ => null,
        };
        return new Classification(Activity, normalizedComponent, derived, false);
    }

    /// <summary>
    /// Elle kayıt doğrulaması (<c>PUT pages/manual</c>): <c>kind</c> null ya da beş değerden biri; bileşen ve etkileşim tipi
    /// yalnız <see cref="Activity"/>'de, bileşen A–I, etkileşim tipi bileşenle tutarlı (bileşensiz tip geçersiz). Değerler
    /// birebir (büyük/küçük harf duyarlı) karşılaştırılır.
    /// </summary>
    public static bool IsValidManual(string? kind, string? component, string? interactionType)
    {
        if (kind is not null && !Kinds.Contains(kind))
            return false;
        if (kind != Activity)
            return component is null && interactionType is null;
        if (component is not null && !Components.Contains(component))
            return false;
        return interactionType is null || IsConsistent(component, interactionType);
    }

    private static string? CanonicalInteractionType(string? value)
    {
        var trimmed = value?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return null;
        foreach (var type in InteractionTypes)
        {
            if (string.Equals(type, trimmed, StringComparison.OrdinalIgnoreCase))
                return type;
        }
        return null;
    }
}
