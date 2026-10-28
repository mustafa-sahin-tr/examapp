using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using ExamApp.Api.Services.Interactions;

namespace ExamApp.Api.Services.Questions;

/// <summary>
/// Issue #590: görseli olmayan sorunun (görselsiz etkinlik maddesi, metin MCQ) sınıflandırıcıya giden düz metni — kapsam
/// başlığı/metni/yönergesi, soru kökü (HTML'siz), şık metinleri ve etkileşim planındaki öğe etiketleri (satır, kolon, kart,
/// hedef, sol/sağ öğe) ile görselsiz düzenin metni (boşluklu metin, tablo; yer tutucular <c>____</c>). Plan çözümsüz
/// saklandığı için (#570) çözüm metne girmez; çözüm alanı hiç okunmaz.
/// </summary>
public static partial class QuestionClassificationText
{
    /// <summary>Sınıflandırıcıya giden metnin üst sınırı (karakter); fazlası kesilir.</summary>
    public const int MaxLength = 4000;

    // Plan JSON'unda metin taşıyan alanlar; id/kutu/tip gibi yapısal alanlar atlanır.
    private static readonly HashSet<string> TextKeys = new(StringComparer.Ordinal) { "label", "text" };

    private static readonly JsonDocumentOptions ReadOptions = new() { MaxDepth = 64 };

    public static string Build(
        string? passageTitle,
        string? passageText,
        string? instruction,
        string? questionText,
        string? questionSubText,
        IEnumerable<string?> answerTexts,
        string? interactionPlan)
    {
        var lines = new List<string>();
        Add(lines, passageTitle);
        Add(lines, passageText);
        Add(lines, instruction);
        Add(lines, questionText);
        Add(lines, questionSubText);
        foreach (var answer in answerTexts)
            Add(lines, answer);
        foreach (var label in PlanLabels(interactionPlan))
            Add(lines, label);

        var text = string.Join("\n", lines);
        return text.Length <= MaxLength ? text : text[..MaxLength];
    }

    /// <summary>HTML etiketlerini atar, varlıkları çözer, boşlukları birleştirir.</summary>
    internal static string ToPlainText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var withoutTags = TagPattern().Replace(value, " ");
        return SpacePattern().Replace(WebUtility.HtmlDecode(withoutTags), " ").Trim();
    }

    private static void Add(List<string> lines, string? value)
    {
        var plain = ToPlainText(value);
        if (plain.Length > 0)
            lines.Add(plain);
    }

    /// <summary>Plandaki <c>label</c>/<c>text</c> değerleri, belge sırasıyla. Bozuk plan = boş.</summary>
    private static IEnumerable<string> PlanLabels(string? plan)
    {
        if (string.IsNullOrWhiteSpace(plan))
            return [];
        try
        {
            using var doc = JsonDocument.Parse(plan, ReadOptions);
            var labels = new List<string>(InteractionLayout.DisplayLines(doc.RootElement));
            Collect(doc.RootElement, labels);
            return labels;
        }
        catch (JsonException)
        {
            return [];
        }
    }

    private static void Collect(JsonElement element, List<string> labels)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    // Düzen (#590 dilim b) yer tutucusuz biçimde yukarıda eklendi.
                    if (property.Name == "layout")
                        continue;
                    if (TextKeys.Contains(property.Name) && property.Value.ValueKind == JsonValueKind.String)
                    {
                        if (property.Value.GetString() is { Length: > 0 } value)
                            labels.Add(value);
                    }
                    else
                    {
                        Collect(property.Value, labels);
                    }
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray())
                    Collect(item, labels);
                break;
        }
    }

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex TagPattern();

    [GeneratedRegex(@"\s+")]
    private static partial Regex SpacePattern();
}
