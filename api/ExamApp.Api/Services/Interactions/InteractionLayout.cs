using System.Text.Json;
using System.Text.RegularExpressions;
using ExamApp.Api.Helpers;

namespace ExamApp.Api.Services.Interactions;

/// <summary>
/// issue #590 (dilim b): görselsiz plan düzeni (<c>layout</c>) — kutu koordinatı yerine öğelerin nasıl dizileceği. Plan öğrenciye
/// gider; düzen metninde çözüm YOKTUR (cevap yerine yer tutucu).
/// <list type="bullet">
/// <item><c>{kind:"list"}</c> — her öğe kendi etiketiyle bir satır (soru + kısa cevap).</item>
/// <item><c>{kind:"text",text}</c> — satır içi metin; <c>[[id]]</c> yer tutucusu öğenin (kutu / hedef) yeridir.</item>
/// <item><c>{kind:"table",header?:[..],rows:[[..],..]}</c> — hücreler düz metin, <c>[[id]]</c> içeren hücre cevap kutusudur.</item>
/// </list>
/// Kayıt kuralı: her öğe metinde/tabloda TAM bir kez geçer, bilinmeyen id'li yer tutucu yok, düz metin süzgeci
/// (<see cref="PlainTextAttribute"/>) ve boyut sınırları.
/// </summary>
public static class InteractionLayout
{
    public const string KindList = "list";
    public const string KindText = "text";
    public const string KindTable = "table";

    public const int MaxTextLength = 4000;
    public const int MaxTableRows = 30;
    public const int MaxTableColumns = 8;
    public const int MaxCellLength = 200;

    private static readonly HashSet<string> ListKeys = new(StringComparer.Ordinal) { "kind" };
    private static readonly HashSet<string> TextKeys = new(StringComparer.Ordinal) { "kind", "text" };
    private static readonly HashSet<string> TableKeys = new(StringComparer.Ordinal) { "kind", "header", "rows" };

    /// <summary>Yer tutucu: <c>[[id]]</c> (id deseni <see cref="InteractionIds"/>).</summary>
    public static readonly Regex Placeholder = new(@"\[\[([A-Za-z0-9_-]{1,32})\]\]", RegexOptions.CultureInvariant);

    /// <summary>
    /// Kayıt doğrulaması. <paramref name="layout"/> plandaki <c>layout</c> değeri; <paramref name="allowedKinds"/> tipin
    /// desteklediği düzenler; <paramref name="itemIds"/> plan sırasıyla öğe id'leri. <paramref name="kind"/> geçerliyse düzen türü.
    /// </summary>
    public static bool TryValidate(JsonElement layout, IReadOnlyCollection<string> allowedKinds, IReadOnlyCollection<string> itemIds,
        out string kind)
    {
        kind = string.Empty;
        if (layout.ValueKind != JsonValueKind.Object || !layout.TryGetProperty("kind", out var kindProp)
            || kindProp.ValueKind != JsonValueKind.String)
            return false;
        kind = kindProp.GetString() ?? string.Empty;
        if (!allowedKinds.Contains(kind))
            return false;

        switch (kind)
        {
            case KindList:
                return HasOnlyKeys(layout, ListKeys);
            case KindText:
            {
                if (!HasOnlyKeys(layout, TextKeys) || !layout.TryGetProperty("text", out var text)
                    || text.ValueKind != JsonValueKind.String)
                    return false;
                var value = text.GetString() ?? string.Empty;
                if (value.Length > MaxTextLength || string.IsNullOrWhiteSpace(value) || PlainTextAttribute.ContainsControlCharacters(value))
                    return false;
                return CoversExactlyOnce(new[] { value }, itemIds);
            }
            case KindTable:
            {
                if (!HasOnlyKeys(layout, TableKeys) || !layout.TryGetProperty("rows", out var rows)
                    || rows.ValueKind != JsonValueKind.Array)
                    return false;
                var rowCount = rows.GetArrayLength();
                if (rowCount is < 1 or > MaxTableRows)
                    return false;
                var cells = new List<string>();
                int? width = null;
                if (layout.TryGetProperty("header", out var header) && header.ValueKind != JsonValueKind.Null)
                {
                    if (!TryReadCells(header, cells, out var headerCells) || Placeholder.IsMatch(string.Concat(headerCells)))
                        return false;
                    width = headerCells.Count;
                }
                foreach (var row in rows.EnumerateArray())
                {
                    if (!TryReadCells(row, cells, out var rowCells))
                        return false;
                    width ??= rowCells.Count;
                    if (rowCells.Count != width)
                        return false;
                }
                return CoversExactlyOnce(cells, itemIds);
            }
            default:
                return false;
        }
    }

    /// <summary>Okuma: düzen türü (bilinmeyen / bozuk = null). İstisna atmaz.</summary>
    public static string? ReadKind(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("layout", out var layout)
            || layout.ValueKind != JsonValueKind.Object || !layout.TryGetProperty("kind", out var kind)
            || kind.ValueKind != JsonValueKind.String)
            return null;
        return kind.GetString();
    }

    /// <summary>
    /// Düzenin görünen metni (sınıflandırma metni için): yer tutucular boşluk çizgisine (<c>____</c>) çevrilir, tablo satırları
    /// <c>" | "</c> ile birleşir. Düzen yoksa / okunamazsa boş liste. İstisna atmaz.
    /// </summary>
    public static IReadOnlyList<string> DisplayLines(JsonElement root)
    {
        var lines = new List<string>();
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("layout", out var layout)
            || layout.ValueKind != JsonValueKind.Object)
            return lines;
        if (layout.TryGetProperty("text", out var text) && text.ValueKind == JsonValueKind.String)
            lines.Add(Blank(text.GetString() ?? string.Empty));
        if (layout.TryGetProperty("header", out var header) && header.ValueKind == JsonValueKind.Array)
            lines.Add(JoinCells(header));
        if (layout.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array)
        {
            foreach (var row in rows.EnumerateArray())
            {
                if (row.ValueKind == JsonValueKind.Array)
                    lines.Add(JoinCells(row));
            }
        }
        return lines.Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
    }

    private static string Blank(string value) => Placeholder.Replace(value, "____");

    private static string JoinCells(JsonElement row) =>
        string.Join(" | ", row.EnumerateArray().Where(c => c.ValueKind == JsonValueKind.String).Select(c => Blank(c.GetString() ?? string.Empty)));

    private static bool TryReadCells(JsonElement row, List<string> all, out List<string> cells)
    {
        cells = new List<string>();
        if (row.ValueKind != JsonValueKind.Array)
            return false;
        var count = row.GetArrayLength();
        if (count is < 1 or > MaxTableColumns)
            return false;
        foreach (var cell in row.EnumerateArray())
        {
            if (cell.ValueKind != JsonValueKind.String)
                return false;
            var value = cell.GetString() ?? string.Empty;
            if (value.Length > MaxCellLength || PlainTextAttribute.ContainsControlCharacters(value))
                return false;
            cells.Add(value);
        }
        all.AddRange(cells);
        return true;
    }

    private static bool CoversExactlyOnce(IEnumerable<string> texts, IReadOnlyCollection<string> itemIds)
    {
        var expected = new HashSet<string>(itemIds, StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var text in texts)
        {
            foreach (Match match in Placeholder.Matches(text))
            {
                var id = match.Groups[1].Value;
                if (!expected.Contains(id) || !seen.Add(id))
                    return false;
            }
        }
        return seen.Count == expected.Count;
    }

    private static bool HasOnlyKeys(JsonElement obj, HashSet<string> allowed)
    {
        foreach (var property in obj.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
                return false;
        }
        return true;
    }
}
