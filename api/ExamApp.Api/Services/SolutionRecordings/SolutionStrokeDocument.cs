using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ExamApp.Api.Services.SolutionRecordings;

/// <summary>
/// Issue #1226: çözüm kaydının çizgi belgesi. Koordinatlar soru görselinin genişliği <see cref="ViewWidth"/> sayılarak
/// tutulur (karalama alanıyla aynı ölçü, #782); <c>height</c> aynı ölçekte görselin yüksekliğidir. Zamanlar ms, kaydın
/// başından. İstemciden gelen belge burada doğrulanır ve yeniden yazılır: saklanan JSON yalnız bu alanları taşır.
/// </summary>
public sealed class SolutionStrokeDocument
{
    public const int ViewWidth = 1000;
    public const int MaxHeight = 20_000;
    public const int MaxStrokes = 4000;
    public const int MaxPointsPerStroke = 5000;
    public const int MaxTotalPoints = 200_000;
    public const int MaxJsonLength = 3_000_000;

    /// <summary>Arayüzdeki kalem renkleri (<c>shared/utils/pen-color.ts</c>, #1241); renk anahtar olarak saklanır.</summary>
    public static readonly IReadOnlySet<string> Colors = new HashSet<string>(StringComparer.Ordinal)
    {
        "default", "red", "green", "blue", "orange",
    };

    [JsonPropertyName("v")]
    public int Version { get; set; }

    [JsonPropertyName("width")]
    public int Width { get; set; }

    [JsonPropertyName("height")]
    public int Height { get; set; }

    [JsonPropertyName("strokes")]
    public List<SolutionStroke> Strokes { get; set; } = [];

    /// <summary>Geçerliyse saklanacak (kurallı) JSON; değilse null.</summary>
    public static string? Normalize(string? json, int durationMs)
    {
        if (string.IsNullOrWhiteSpace(json) || json.Length > MaxJsonLength)
            return null;
        SolutionStrokeDocument? document;
        try
        {
            document = JsonSerializer.Deserialize<SolutionStrokeDocument>(json);
        }
        catch (JsonException)
        {
            return null;
        }
        if (document is null || document.Version != Data.QuestionSolutionRecording.CurrentSchemaRevision
            || document.Width != ViewWidth || document.Height is < 1 or > MaxHeight
            || document.Strokes is null || document.Strokes.Count > MaxStrokes)
            return null;

        var totalPoints = 0;
        foreach (var stroke in document.Strokes)
        {
            if (stroke is null || !Colors.Contains(stroke.Color ?? string.Empty)
                || stroke.Start < 0 || stroke.Start > durationMs
                || stroke.ErasedAt is { } erased && (erased < stroke.Start || erased > durationMs)
                || stroke.Points is null || stroke.Points.Count < 3 || stroke.Points.Count % 3 != 0
                || stroke.Points.Count > MaxPointsPerStroke * 3)
                return null;
            for (var i = 0; i < stroke.Points.Count; i += 3)
            {
                // x, y görsel ölçeğinde; dt çizginin başından ms.
                if (stroke.Points[i] is < 0 or > ViewWidth || stroke.Points[i + 1] is < 0 or > MaxHeight
                    || stroke.Points[i + 2] < 0 || stroke.Points[i + 2] > durationMs)
                    return null;
            }
            totalPoints += stroke.Points.Count / 3;
            if (totalPoints > MaxTotalPoints)
                return null;
        }

        document.Strokes = document.Strokes.OrderBy(s => s.Start).ToList();
        return JsonSerializer.Serialize(document);
    }
}

public sealed class SolutionStroke
{
    [JsonPropertyName("c")]
    public string? Color { get; set; }

    /// <summary>Çizginin başladığı an (ms).</summary>
    [JsonPropertyName("t")]
    public int Start { get; set; }

    /// <summary>Çizginin silindiği an (geri al / temizle); silinmediyse yok.</summary>
    [JsonPropertyName("e")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? ErasedAt { get; set; }

    /// <summary>Düz dizi: <c>x, y, dt, x, y, dt, ...</c>.</summary>
    [JsonPropertyName("p")]
    public List<int> Points { get; set; } = [];
}
