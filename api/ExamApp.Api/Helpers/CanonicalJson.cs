using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ExamApp.Api.Helpers;

/// <summary>
/// Issue #580: JSON'un kanonik biçimi — nesne anahtarları sıralı (ordinal), boşluksuz; dizi sırası korunur. Aynı içeriğin farklı
/// anahtar sırası / boşlukla gelmesi aynı hash'i ve eşitliği verir (plan hash'i, çözüm değişti mi karşılaştırması).
/// </summary>
public static class CanonicalJson
{
    /// <summary>Kanonik metin; null/boş → null; JSON değilse girdinin kırpılmış hali (karşılaştırma yine çalışır).</summary>
    public static string? Normalize(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return Sort(JsonNode.Parse(json))?.ToJsonString() ?? "null";
        }
        catch (JsonException)
        {
            return json.Trim();
        }
    }

    public static bool Equal(string? a, string? b) => string.Equals(Normalize(a), Normalize(b), StringComparison.Ordinal);

    private static JsonNode? Sort(JsonNode? node) => node switch
    {
        JsonObject obj => new JsonObject(obj.OrderBy(p => p.Key, StringComparer.Ordinal)
            .Select(p => new System.Collections.Generic.KeyValuePair<string, JsonNode?>(p.Key, Sort(p.Value?.DeepClone())))),
        JsonArray arr => new JsonArray(arr.Select(x => Sort(x?.DeepClone())).ToArray()),
        _ => node?.DeepClone(),
    };
}
