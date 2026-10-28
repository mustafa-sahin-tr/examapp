using System.IO;
using System.Text;
using System.Text.Json;
using ExamApp.Api.Helpers;

namespace ExamApp.Api.Services.Interactions;

/// <summary>
/// issue #587: <c>regionPaint</c> kontrolcüsü — bölge seçme ve boyama (bileşen E). Dilim a: ızgara modu ("eş karelere ayrılmış şeklin
/// 2/5'ini siyaha, 0,35'ini maviye boyayalım").
/// <list type="bullet">
/// <item>Plan v1: <c>{version:1,type:"regionPaint",mode:"grid",rows,cols,palette:[{id,color,label?}]}</c>. Izgara 2..20 × 2..20;
/// palet 1..8 renk, <c>color</c> sabit adlı renklerden (<see cref="Colors"/>, istemci CSS token'ına çevirir), palette tekil; <c>label</c>
/// isteğe bağlı düz metin (≤ 40).</item>
/// <item>Çözüm: <c>{"counts":{"p1":40,"p2":35}}</c> — her palet rengine kaç hücre boyanacağı (0 = bu renk kullanılmamalı, çeldirici);
/// hücrelerin yeri serbest. Toplam ≤ hücre sayısı, en az bir renk &gt; 0.</item>
/// <item>Öğrenci payload'u: <c>{version:1,type:"regionPaint",grid:["..1..",...]}</c> — satır başına <c>cols</c> karakter; <c>.</c> boş,
/// <c>1</c>..<c>8</c> palet sırası (1 tabanlı). Boyasız ızgara = cevap yok.</item>
/// </list>
/// Kontrol: satır = palet rengi (plan sırası); doğru ⇔ o renkte boyanan hücre sayısı çözümdekine eşit. Geri bildirim
/// <c>{"rows":[{id,correct}]}</c>; beklenen sayı yazılmaz. Çözümü olmayan soru puanlanamaz (0/0).
/// </summary>
public sealed class RegionPaintGrader : IInteractionGrader
{
    public const string Type = "regionPaint";

    public const string GridMode = "grid";

    public const int MinSize = 2;
    public const int MaxSize = 20;
    public const int MaxPalette = 8;
    public const int MaxLabelLength = 40;

    public const char Empty = '.';

    /// <summary>İzinli palet renkleri (istemcide <c>--paint-&lt;ad&gt;</c> token'ı). Serbest CSS rengi alınmaz.</summary>
    public static readonly IReadOnlySet<string> Colors = new HashSet<string>(StringComparer.Ordinal)
    {
        "red", "orange", "yellow", "green", "blue", "purple", "pink", "brown", "black", "gray",
    };

    private static readonly HashSet<string> RootKeys = new(StringComparer.Ordinal) { "version", "type", "mode", "rows", "cols", "palette" };
    private static readonly HashSet<string> PaletteKeys = new(StringComparer.Ordinal) { "id", "color", "label" };

    private static readonly JsonDocumentOptions StrictOptions = new() { MaxDepth = 8, AllowDuplicateProperties = false };
    private static readonly JsonDocumentOptions ReadOptions = new() { MaxDepth = 256 };
    private static readonly JsonDocumentOptions DefinitionOptions = new() { MaxDepth = 32, AllowDuplicateProperties = false };

    public string InteractionType => Type;

    public bool Validate(string? plan, string? payload)
    {
        if (payload == null)
            return true;
        if (payload.Length > InteractionPayload.MaxLength)
            return false;
        return TryReadPlan(plan, ReadOptions, strict: false, out var grid) && TryParsePayload(payload, grid, out _);
    }

    /// <summary>Boyasız ızgara = cevap yok → null.</summary>
    public string? Normalize(string? payload)
    {
        if (payload == null || !TryParsePayload(payload, null, out var cells))
            return payload;
        return cells.All(row => row.All(c => c == Empty)) ? null : payload;
    }

    public InteractionGradeResult Grade(InteractionGradeInput input)
    {
        if (!TryReadPlan(input.Plan, ReadOptions, strict: false, out var grid) || !TryReadSolution(input.Solution, grid, out var counts))
            return InteractionGradeResult.Ungradable;
        var painted = Painted(grid, input.Payload);
        var rows = grid.Palette.Where(p => counts.ContainsKey(p.Id))
            .Select(p => (p.Id, painted.GetValueOrDefault(p.Id) == counts[p.Id])).ToList();
        return InteractionGradeResult.Of(rows.Count(r => r.Item2), rows.Count, WriteFeedback(rows));
    }

    /// <summary>issue #584: öğe = palet rengi, cevap = o renkte boyanan hücre sayısı (0 = cevapsız).</summary>
    public IReadOnlyList<InteractionItemResponse> Analyze(InteractionGradeInput input)
    {
        if (!TryReadPlan(input.Plan, ReadOptions, strict: false, out var grid) || !TryReadSolution(input.Solution, grid, out var counts))
            return [];
        var painted = Painted(grid, input.Payload);
        return grid.Palette.Where(p => counts.ContainsKey(p.Id)).Select(p =>
        {
            var count = painted.GetValueOrDefault(p.Id);
            var answer = count == 0 ? null : count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return new InteractionItemResponse(p.Id, InteractionAnalysis.TextLabel(string.IsNullOrWhiteSpace(p.Label) ? p.Color : p.Label),
                count == counts[p.Id], answer, answer);
        }).ToList();
    }

    /// <summary>
    /// Kayıt kuralı (issue #587): mod <c>grid</c>, ızgara 2..20 × 2..20, palet 1..8 tekil id ve tekil izinli renk, etiket isteğe bağlı
    /// düz metin (≤ 40). Çözüm boşsa geçerli (taslak); doluysa her palet rengine 0..hücre sayısı tam sayı, toplam ≤ hücre sayısı, en az
    /// bir renk &gt; 0.
    /// </summary>
    public bool ValidateDefinition(string? plan, string? solution)
    {
        if (!TryReadPlan(plan, DefinitionOptions, strict: true, out var grid))
            return false;
        if (string.IsNullOrWhiteSpace(solution))
            return true;
        try
        {
            using var doc = JsonDocument.Parse(solution, DefinitionOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasOnlyKeys(root, ["counts"])
                || !root.TryGetProperty("counts", out var countsElement) || countsElement.ValueKind != JsonValueKind.Object)
                return false;
            var ids = grid.Palette.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var total = 0;
            foreach (var entry in countsElement.EnumerateObject())
            {
                if (!ids.Contains(entry.Name) || !seen.Add(entry.Name) || entry.Value.ValueKind != JsonValueKind.Number
                    || !entry.Value.TryGetInt32(out var count) || count < 0 || count > grid.Cells)
                    return false;
                total += count;
            }
            return seen.Count == ids.Count && total > 0 && total <= grid.Cells;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    // ---- Model ----

    private sealed record Paint(string Id, string Color, string Label);

    private sealed record Grid(int Rows, int Cols, List<Paint> Palette)
    {
        public int Cells => Rows * Cols;
    }

    /// <summary>Palet id'si → boyanan hücre sayısı.</summary>
    private static Dictionary<string, int> Painted(Grid grid, string? payload)
    {
        var painted = new Dictionary<string, int>(StringComparer.Ordinal);
        if (payload == null || !TryParsePayload(payload, grid, out var cells))
            return painted;
        foreach (var row in cells)
            foreach (var c in row)
            {
                if (c == Empty)
                    continue;
                var id = grid.Palette[c - '1'].Id;
                painted[id] = painted.GetValueOrDefault(id) + 1;
            }
        return painted;
    }

    // ---- Okuma ----

    private static bool TryReadPlan(string? plan, JsonDocumentOptions options, bool strict, out Grid grid)
    {
        grid = new Grid(0, 0, new List<Paint>());
        if (string.IsNullOrWhiteSpace(plan))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(plan, options);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;
            if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                || !string.Equals(type.GetString(), Type, StringComparison.Ordinal))
                return false;
            if (!TryReadString(root, "mode", out var mode) || mode != GridMode)
                return false;
            if (strict)
            {
                if (!HasOnlyKeys(root, RootKeys))
                    return false;
                if (root.TryGetProperty("version", out var version)
                    && (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1))
                    return false;
            }
            if (!TryReadInt(root, "rows", out var rows) || !TryReadInt(root, "cols", out var cols)
                || rows is < MinSize or > MaxSize || cols is < MinSize or > MaxSize)
                return false;

            if (!root.TryGetProperty("palette", out var paletteElement) || paletteElement.ValueKind != JsonValueKind.Array)
                return false;
            var palette = new List<Paint>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var colors = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in paletteElement.EnumerateArray())
            {
                if (raw.ValueKind != JsonValueKind.Object || (strict && !HasOnlyKeys(raw, PaletteKeys))
                    || !TryReadString(raw, "id", out var id) || !InteractionIds.IsValid(id) || !ids.Add(id)
                    || !TryReadString(raw, "color", out var color) || !Colors.Contains(color) || !colors.Add(color)
                    || !TryReadLabel(raw, strict, out var label))
                {
                    if (strict) return false;
                    continue;
                }
                palette.Add(new Paint(id, color, label));
            }
            // Payload palet sırasını tek karakterle yazar: okumada da en çok 8 renk.
            if (palette.Count is 0 or > MaxPalette)
                return false;
            grid = new Grid(rows, cols, palette);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Etiket isteğe bağlı; varsa düz metin, sınır içinde (kayıtta). Okumada eksik / bozuksa boş.</summary>
    private static bool TryReadLabel(JsonElement element, bool strict, out string label)
    {
        label = string.Empty;
        if (!element.TryGetProperty("label", out var value) || value.ValueKind == JsonValueKind.Null)
            return true;
        if (value.ValueKind != JsonValueKind.String)
            return !strict;
        label = value.GetString() ?? string.Empty;
        return !strict || (label.Length <= MaxLabelLength && !PlainTextAttribute.ContainsControlCharacters(label));
    }

    private static bool TryReadInt(JsonElement item, string name, out int value)
    {
        value = 0;
        return item.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value);
    }

    private static bool TryReadString(JsonElement item, string name, out string value)
    {
        value = string.Empty;
        if (!item.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.String)
            return false;
        value = element.GetString() ?? string.Empty;
        return value.Length > 0;
    }

    private static bool HasOnlyKeys(JsonElement obj, IReadOnlySet<string> allowed)
    {
        foreach (var property in obj.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
                return false;
        }
        return true;
    }

    private static bool HasOnlyKeys(JsonElement obj, string[] allowed) => HasOnlyKeys(obj, allowed.ToHashSet(StringComparer.Ordinal));

    /// <summary>
    /// Katı payload: <c>grid</c> satır dizisi; plan verilmişse satır sayısı / uzunluk planla aynı ve karakterler palet sırasında. Plan
    /// verilmezse (Normalize) yalnız biçim: en çok 20 satır × 20 karakter, <c>.</c> ya da <c>1</c>..<c>8</c>.
    /// </summary>
    private static bool TryParsePayload(string payload, Grid? grid, out List<string> cells)
    {
        cells = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(payload, StrictOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasOnlyKeys(root, ["version", "type", "grid"]))
                return false;
            if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                || !string.Equals(type.GetString(), Type, StringComparison.Ordinal))
                return false;
            if (root.TryGetProperty("version", out var version)
                && (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1))
                return false;
            if (!root.TryGetProperty("grid", out var array) || array.ValueKind != JsonValueKind.Array
                || array.GetArrayLength() > MaxSize || (grid != null && array.GetArrayLength() != grid.Rows))
                return false;
            var maxPaint = (char)('0' + (grid?.Palette.Count ?? MaxPalette));
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } row || row.Length > MaxSize
                    || (grid != null && row.Length != grid.Cols)
                    || row.Any(c => c != Empty && (c < '1' || c > maxPaint)))
                    return false;
                cells.Add(row);
            }
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            cells.Clear();
            return false;
        }
    }

    /// <summary>Çözüm: palet id'si → hücre sayısı. Toleranslı: plana uymayan / negatif atlanır; okunamaz ya da boşsa false.</summary>
    private static bool TryReadSolution(string? solution, Grid grid, out Dictionary<string, int> counts)
    {
        counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(solution))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(solution, ReadOptions);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("counts", out var element)
                || element.ValueKind != JsonValueKind.Object)
                return false;
            var ids = grid.Palette.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var entry in element.EnumerateObject())
            {
                if (ids.Contains(entry.Name) && entry.Value.ValueKind == JsonValueKind.Number && entry.Value.TryGetInt32(out var count)
                    && count >= 0)
                    counts[entry.Name] = count;
            }
            return counts.Count > 0;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            counts.Clear();
            return false;
        }
    }

    private static string WriteFeedback(IReadOnlyList<(string Id, bool Correct)> rows)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("rows");
            foreach (var (id, correct) in rows)
            {
                writer.WriteStartObject();
                writer.WriteString("id", id);
                writer.WriteBoolean("correct", correct);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
