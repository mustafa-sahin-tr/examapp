using System.Text.Json;

namespace ExamApp.Api.Services.Interactions;

/// <summary>
/// issue #584: öğretmen etkinlik analizinde bir öğrencinin bir madde öğesine (kart, satır, kutu, sol öğe, hedef, boşluk) verdiği
/// cevap. Kontrolcünün <see cref="IInteractionGrader.Analyze"/>'ı üretir; rapor servisi öğrenciler üzerinden toplar.
/// <para>
/// Öğeler çözüm/plan sırasıyla döner ve öğrenciden bağımsızdır (cevapsız öğe <see cref="AnswerKey"/> null ile yine döner) —
/// tek istisna <see cref="Distractor"/>: textSelect'te hiçbir hedefte olmayan seçili token yalnız seçildiğinde öğe olur.
/// </para>
/// </summary>
/// <param name="ItemId">Öğe id'si (plan id'si; soru içinde tekil).</param>
/// <param name="ItemLabel">Öğenin görünen adı (plan etiketi; etiketsiz öğe — görsel kırpma — plandaki sırasıyla <c>#n</c>).</param>
/// <param name="Correct">Öğe doğru mu (kontrolcünün satır kuralıyla aynı).</param>
/// <param name="AnswerKey">Öğrencinin cevabının gruplama anahtarı (aynı cevap = aynı anahtar); cevapsız = null.</param>
/// <param name="AnswerLabel">Cevabın görünen hâli (hedef/kolon/seçenek etiketi ya da yazılan metin); cevapsız = null.</param>
/// <param name="Distractor">Çeldirici öğe (çözümde yeri olmayan kart / hedef dışı token): doğru = hiç kullanılmamış.</param>
public sealed record InteractionItemResponse(
    string ItemId,
    string ItemLabel,
    bool Correct,
    string? AnswerKey,
    string? AnswerLabel,
    bool Distractor = false);

/// <summary>issue #584: analizde plan etiketlerini okuyan ortak yardımcılar (toleranslı; bozuk plan = boş).</summary>
internal static class InteractionAnalysis
{
    private static readonly JsonDocumentOptions ReadOptions = new() { MaxDepth = 256 };

    /// <summary>
    /// Plan kökündeki <paramref name="arrayName"/> dizisinin öğeleri, plan sırasıyla: id → görünen ad (<paramref name="labelProperty"/>,
    /// boşsa <c>#sıra</c>). Sayı id'ler ham metniyle okunur (dragDropLabeling eski kayıtları).
    /// </summary>
    public static List<(string Id, string Label)> ReadEntries(string? plan, string arrayName, string labelProperty = "label")
    {
        var result = new List<(string, string)>();
        using var doc = Parse(plan);
        if (doc == null || !doc.RootElement.TryGetProperty(arrayName, out var array) || array.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var item in array.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object || !TryReadId(item, out var id))
                continue;
            result.Add((id, LabelOf(item, labelProperty) ?? Ordinal(result.Count)));
        }
        return result;
    }

    /// <summary>slotFill: kutu id → (seçenek id → etiket) — yalnız <c>select</c> kutuları.</summary>
    public static Dictionary<string, Dictionary<string, string>> ReadSlotOptions(string? plan)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);
        using var doc = Parse(plan);
        if (doc == null || !doc.RootElement.TryGetProperty("slots", out var slots) || slots.ValueKind != JsonValueKind.Array)
            return result;
        foreach (var slot in slots.EnumerateArray())
        {
            if (slot.ValueKind != JsonValueKind.Object || !TryReadId(slot, out var slotId)
                || !slot.TryGetProperty("input", out var input) || input.ValueKind != JsonValueKind.Object
                || !input.TryGetProperty("options", out var options) || options.ValueKind != JsonValueKind.Array)
                continue;
            var map = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var option in options.EnumerateArray())
            {
                if (option.ValueKind == JsonValueKind.Object && TryReadId(option, out var optionId))
                    map.TryAdd(optionId, LabelOf(option, "label") ?? Ordinal(map.Count));
            }
            result.TryAdd(slotId, map);
        }
        return result;
    }

    /// <summary>Etiket haritası (id → ad); yinelenen id'de ilki.</summary>
    public static Dictionary<string, string> ToMap(IEnumerable<(string Id, string Label)> entries)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, label) in entries)
            map.TryAdd(id, label);
        return map;
    }

    /// <summary>
    /// Serbest metin cevabın gruplama anahtarı: kırpılmış, boşluklar teke indirilmiş, Türkçe küçük harf ("Kemik" = "kemik ").
    /// Doğruluk bu anahtarla DEĞİL kontrolcünün kuralıyla belirlenir; anahtar yalnız "en sık yanlış cevap" sayımı içindir.
    /// </summary>
    public static string TextKey(string value)
        => string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
            .ToLower(System.Globalization.CultureInfo.GetCultureInfo("tr-TR"));

    /// <summary>Görünen metin: kırpılmış, boşluklar teke indirilmiş, en çok <paramref name="max"/> karakter.</summary>
    public static string TextLabel(string value, int max = 120)
    {
        var text = string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return text.Length <= max ? text : text[..max] + "…";
    }

    private static string Ordinal(int index) => "#" + (index + 1);

    private static string? LabelOf(JsonElement item, string labelProperty)
        => item.TryGetProperty(labelProperty, out var label) && label.ValueKind == JsonValueKind.String
           && label.GetString() is { } text && !string.IsNullOrWhiteSpace(text)
            ? TextLabel(text)
            : null;

    private static bool TryReadId(JsonElement obj, out string id)
    {
        id = string.Empty;
        if (!obj.TryGetProperty("id", out var prop))
            return false;
        id = prop.ValueKind switch
        {
            JsonValueKind.String => prop.GetString() ?? string.Empty,
            JsonValueKind.Number => prop.GetRawText(),
            _ => string.Empty
        };
        return id.Length > 0;
    }

    private static JsonDocument? Parse(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            var doc = JsonDocument.Parse(json, ReadOptions);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                return doc;
            doc.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }
}
