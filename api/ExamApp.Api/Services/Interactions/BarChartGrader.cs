using System.IO;
using System.Text;
using System.Text.Json;
using ExamApp.Api.Helpers;

namespace ExamApp.Api.Services.Interactions;

/// <summary>
/// issue #588 (dilim b): <c>barChart</c> kontrolcüsü — ızgara tuvali ailesinin (bileşen F) grafik kurma dilimi ("tablodaki verilere göre
/// sütun grafiğini çizelim", "çizgi grafiğini tamamlayalım").
/// <list type="bullet">
/// <item>Plan v1: <c>{version:1,type:"barChart",style?,title?,xLabel?,yLabel?,max,step,unit?,categories:[{id,label}],fixed?}</c>.
/// Dikey eksen 0..<c>max</c>; <c>step</c> etiketli ızgara çizgisi aralığı (<c>max</c>'ı böler, en çok 20 aralık), <c>unit</c> öğrencinin
/// ayarlayabildiği en küçük adım (<c>step</c>'i böler, varsayılan <c>step</c>, en çok 100 adım). <c>style</c> <c>bar</c> (varsayılan) |
/// <c>line</c> | <c>dot</c> (nokta grafiği, dilim d: her <c>unit</c> bir nokta, en çok <see cref="MaxDots"/>) — yalnız görünüm. Kategoriler 2..12, etiket zorunlu düz metin ≤ 30; başlık ≤ 80, eksen adları ≤ 40.
/// <c>fixed</c> (<c>{"c1":6}</c>) önceden çizilmiş, öğrencinin değiştiremediği sütunlar ("grafiği tamamla"); en az bir kategori serbest.</item>
/// <item>Çözüm: <c>{"values":{"c2":4}}</c> — her serbest kategoriye <c>unit</c>'in katı 0..max; sabit kategori yazılmaz.</item>
/// <item>Öğrenci payload'u: <c>{version:1,type:"barChart",values:{"c2":4}}</c> — yalnız serbest kategoriler; yazılmayan kategori 0
/// sayılır. Boş <c>values</c> = cevap yok.</item>
/// </list>
/// Kontrol: satır = serbest kategori (plan sırası), doğru ⇔ değer çözümdekine eşit. Geri bildirim <c>{"rows":[{id,correct}]}</c>; hedef
/// değer yazılmaz. Veriler (tablo) görselde ya da yönergede durur; planda yok.
/// </summary>
public sealed class BarChartGrader : IInteractionGrader
{
    public const string Type = "barChart";

    public const int MaxValue = 1000;
    public const int MaxSteps = 20;
    public const int MaxUnits = 100;
    public const int MaxDots = 20;
    public const int MinCategories = 2;
    public const int MaxCategories = 12;
    public const int MaxCategoryLength = 30;
    public const int MaxTitleLength = 80;
    public const int MaxAxisLength = 40;

    private static readonly HashSet<string> RootKeys = new(StringComparer.Ordinal)
    {
        "version", "type", "style", "title", "xLabel", "yLabel", "max", "step", "unit", "categories", "fixed",
    };
    private static readonly HashSet<string> CategoryKeys = new(StringComparer.Ordinal) { "id", "label" };

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
        return TryReadPlan(plan, ReadOptions, strict: false, out var chart) && TryParsePayload(payload, chart, out _);
    }

    /// <summary>Değersiz cevap = cevap yok → null.</summary>
    public string? Normalize(string? payload)
    {
        if (payload == null || !TryParsePayload(payload, null, out var values))
            return payload;
        return values.Count == 0 ? null : payload;
    }

    public InteractionGradeResult Grade(InteractionGradeInput input)
    {
        if (!TryReadPlan(input.Plan, ReadOptions, strict: false, out var chart) || !TryReadSolution(input.Solution, chart, out var targets))
            return InteractionGradeResult.Ungradable;
        var values = Values(chart, input.Payload);
        var rows = chart.Free.Where(c => targets.ContainsKey(c.Id))
            .Select(c => (c.Id, (values.TryGetValue(c.Id, out var v) ? v : 0) == targets[c.Id])).ToList();
        return InteractionGradeResult.Of(rows.Count(r => r.Item2), rows.Count, WriteFeedback(rows));
    }

    /// <summary>issue #584: öğe = serbest kategori, cevap = çizilen değer (yazılmayan 0; cevap yoksa boş). Doğruluk
    /// <see cref="Grade"/> ile aynı: çizilmeyen sütun 0.</summary>
    public IReadOnlyList<InteractionItemResponse> Analyze(InteractionGradeInput input)
    {
        if (!TryReadPlan(input.Plan, ReadOptions, strict: false, out var chart) || !TryReadSolution(input.Solution, chart, out var targets))
            return [];
        var answered = input.Payload != null && TryParsePayload(input.Payload, chart, out var parsed) && parsed.Count > 0;
        var values = Values(chart, input.Payload);
        return chart.Free.Where(c => targets.ContainsKey(c.Id)).Select(c =>
        {
            int? value = answered ? values.TryGetValue(c.Id, out var v) ? v : 0 : null;
            var key = value?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return new InteractionItemResponse(c.Id, InteractionAnalysis.TextLabel(c.Label), (value ?? 0) == targets[c.Id], key, key);
        }).ToList();
    }

    /// <summary>
    /// Kayıt kuralı (issue #588): eksen (max 1..1000, step max'ı böler ve ≤ 20 aralık, unit step'i böler ve ≤ 100 adım), 2..12 kategori
    /// tekil id + dolu etiket, sabit değerler unit katı ve en az bir serbest kategori, metinler düz metin. Çözüm boşsa geçerli (taslak);
    /// doluysa yalnız ve her serbest kategori için unit katı 0..max.
    /// </summary>
    public bool ValidateDefinition(string? plan, string? solution)
    {
        if (!TryReadPlan(plan, DefinitionOptions, strict: true, out var chart))
            return false;
        if (string.IsNullOrWhiteSpace(solution))
            return true;
        try
        {
            using var doc = JsonDocument.Parse(solution, DefinitionOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasOnlyKeys(root, ["values"])
                || !root.TryGetProperty("values", out var values) || values.ValueKind != JsonValueKind.Object)
                return false;
            var ids = chart.Free.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in values.EnumerateObject())
            {
                if (!ids.Contains(entry.Name) || !seen.Add(entry.Name) || !IsValue(entry.Value, chart, out _))
                    return false;
            }
            return seen.Count == ids.Count;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    // ---- Model ----

    private sealed record Category(string Id, string Label);

    private sealed record Chart(int Max, int Unit, List<Category> Categories, HashSet<string> Fixed)
    {
        public IEnumerable<Category> Free => Categories.Where(c => !Fixed.Contains(c.Id));
    }

    private static bool IsValue(JsonElement element, Chart chart, out int value)
    {
        value = 0;
        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value)
            && value >= 0 && value <= chart.Max && value % chart.Unit == 0;
    }

    /// <summary>Serbest kategori id'si → değer (geçersiz / boş payload'da boş).</summary>
    private static Dictionary<string, int> Values(Chart chart, string? payload)
        => payload != null && TryParsePayload(payload, chart, out var values) ? values : new Dictionary<string, int>(StringComparer.Ordinal);

    // ---- Okuma ----

    private static bool TryReadPlan(string? plan, JsonDocumentOptions options, bool strict, out Chart chart)
    {
        chart = new Chart(0, 1, new List<Category>(), new HashSet<string>(StringComparer.Ordinal));
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
            var dots = false;
            if (strict)
            {
                if (!HasOnlyKeys(root, RootKeys))
                    return false;
                if (root.TryGetProperty("version", out var version)
                    && (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1))
                    return false;
                if (root.TryGetProperty("style", out var style)
                    && (style.ValueKind != JsonValueKind.String || style.GetString() is not ("bar" or "line" or "dot")))
                    return false;
                dots = root.TryGetProperty("style", out style) && style.GetString() == "dot";
                if (!IsOptionalText(root, "title", MaxTitleLength) || !IsOptionalText(root, "xLabel", MaxAxisLength)
                    || !IsOptionalText(root, "yLabel", MaxAxisLength))
                    return false;
            }
            if (!TryReadInt(root, "max", out var max) || max is < 1 or > MaxValue
                || !TryReadInt(root, "step", out var step) || step < 1 || max % step != 0 || max / step > MaxSteps)
                return false;
            var unit = step;
            if (root.TryGetProperty("unit", out var unitElement) && unitElement.ValueKind != JsonValueKind.Null)
            {
                if (unitElement.ValueKind != JsonValueKind.Number || !unitElement.TryGetInt32(out unit)
                    || unit < 1 || step % unit != 0 || max / unit > MaxUnits)
                    return false;
            }
            // Nokta grafiğinde sütun üst üste noktalardır: çizilebilir nokta sayısı sınırlı.
            if (dots && max / unit > MaxDots)
                return false;

            if (!root.TryGetProperty("categories", out var categoriesElement) || categoriesElement.ValueKind != JsonValueKind.Array)
                return false;
            var categories = new List<Category>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in categoriesElement.EnumerateArray())
            {
                if (raw.ValueKind != JsonValueKind.Object || (strict && !HasOnlyKeys(raw, CategoryKeys))
                    || !TryReadString(raw, "id", out var id) || !InteractionIds.IsValid(id) || !ids.Add(id)
                    || !TryReadString(raw, "label", out var label) || string.IsNullOrWhiteSpace(label)
                    || (strict && (label.Length > MaxCategoryLength || PlainTextAttribute.ContainsControlCharacters(label))))
                {
                    if (strict) return false;
                    continue;
                }
                categories.Add(new Category(id, label));
            }
            if (categories.Count is < MinCategories or > MaxCategories)
                return false;

            var fixedIds = new HashSet<string>(StringComparer.Ordinal);
            var draft = new Chart(max, unit, categories, fixedIds);
            if (root.TryGetProperty("fixed", out var fixedElement) && fixedElement.ValueKind != JsonValueKind.Null)
            {
                if (fixedElement.ValueKind != JsonValueKind.Object)
                {
                    if (strict) return false;
                }
                else
                {
                    foreach (var entry in fixedElement.EnumerateObject())
                    {
                        if (!ids.Contains(entry.Name) || !IsValue(entry.Value, draft, out _) || !fixedIds.Add(entry.Name))
                        {
                            if (strict) return false;
                        }
                    }
                }
            }
            if (fixedIds.Count >= categories.Count)
                return false;
            chart = draft;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsOptionalText(JsonElement root, string name, int maxLength)
    {
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
            return true;
        if (element.ValueKind != JsonValueKind.String)
            return false;
        var text = element.GetString() ?? string.Empty;
        return text.Length <= maxLength && !PlainTextAttribute.ContainsControlCharacters(text);
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
    /// Katı payload: <c>values</c> nesnesi, kategori id'si → değer (tam sayı). Plan verilmişse id serbest kategori ve değer unit katı
    /// 0..max; verilmezse (Normalize) yalnız biçim: geçerli id, 0..1000, en çok 12 kategori.
    /// </summary>
    private static bool TryParsePayload(string payload, Chart? chart, out Dictionary<string, int> values)
    {
        values = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(payload, StrictOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasOnlyKeys(root, ["version", "type", "values"]))
                return false;
            if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                || !string.Equals(type.GetString(), Type, StringComparison.Ordinal))
                return false;
            if (root.TryGetProperty("version", out var version)
                && (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1))
                return false;
            if (!root.TryGetProperty("values", out var element) || element.ValueKind != JsonValueKind.Object)
                return false;
            var free = chart?.Free.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var entry in element.EnumerateObject())
            {
                var ok = InteractionIds.IsValid(entry.Name) && (free == null || free.Contains(entry.Name));
                int value = 0;
                ok = ok && (chart != null
                    ? IsValue(entry.Value, chart, out value)
                    : entry.Value.ValueKind == JsonValueKind.Number && entry.Value.TryGetInt32(out value) && value >= 0 && value <= MaxValue);
                if (!ok || !values.TryAdd(entry.Name, value) || values.Count > MaxCategories)
                {
                    values.Clear();
                    return false;
                }
            }
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            values.Clear();
            return false;
        }
    }

    /// <summary>Çözüm: serbest kategori id'si → değer. Toleranslı: plana uymayan atlanır; hiç değer yoksa false.</summary>
    private static bool TryReadSolution(string? solution, Chart chart, out Dictionary<string, int> targets)
    {
        targets = new Dictionary<string, int>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(solution))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(solution, ReadOptions);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("values", out var element)
                || element.ValueKind != JsonValueKind.Object)
                return false;
            var free = chart.Free.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var entry in element.EnumerateObject())
            {
                if (free.Contains(entry.Name) && IsValue(entry.Value, chart, out var value))
                    targets[entry.Name] = value;
            }
            return targets.Count > 0;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            targets.Clear();
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
