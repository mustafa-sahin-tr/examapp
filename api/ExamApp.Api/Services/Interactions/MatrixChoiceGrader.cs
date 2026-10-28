using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ExamApp.Api.Services.Interactions;

/// <summary>
/// issue #574: <c>matrixChoice</c> kontrolcüsü — "satır × seçenek" (D/Y tabloları, ✓ ile sınıflandırma, çoklu seçim,
/// &lt; &gt; =). Satır başına doğru/yanlış.
/// <list type="bullet">
/// <item>Plan v1: <c>{version:1,type:"matrixChoice",multiPerRow:false,columns:[{id,label?}],rows:[{id,label?,box,cells?}]}</c>;
/// <c>box</c> ve <c>cells</c> (kolon id → kutu) soru bölgesine göre göreli koordinatlardır (dragDropLabeling hedefleri gibi).</item>
/// <item>Çözüm (#570, <c>Question.InteractionSolution</c>): <c>{"rowId":["colId", ...], ...}</c>.</item>
/// <item>Öğrenci payload'u: <c>{version:1,type:"matrixChoice",selections:{"rowId":["colId"]}}</c>.</item>
/// </list>
/// Kayıtta plan her düzeyde izinli alan listesine (allowlist) uymalı, id'ler <see cref="InteractionIds"/> kuralına uyar (review #574; ayrılmış adlar #575).
/// Satırlar = plandaki TÜM satırlar, plan sırasıyla (cevapsız satır yanlış). Satır doğru ⇔ seçilen küme == çözüm kümesi.
/// Geri bildirim yalnız satır id'si + doğru/yanlış taşır; beklenen kolon ASLA yazılmaz. Satırların hepsi geri bildirimde
/// olduğu için (dragDropLabeling'den farklı olarak) hangi satırların çözümlü olduğu da sızmaz. Çözümü olmayan soru
/// puanlanamaz (0/0).
/// </summary>
public sealed class MatrixChoiceGrader : IInteractionGrader
{
    public const string Type = "matrixChoice";

    public const int MinColumns = 2;
    public const int MaxColumns = 10;
    public const int MinRows = 1;
    public const int MaxRows = 60;
    public const int MaxIdLength = 32;
    public const int MaxLabelLength = 200;


    // review #574 (M1): kayıtta planın her düzeyinde izinli alanlar; bilinmeyen alan (gizli veri / iç içe "solution") reddedilir.
    private static readonly HashSet<string> RootKeys = new(StringComparer.Ordinal) { "version", "type", "multiPerRow", "columns", "rows" };
    private static readonly HashSet<string> ColumnKeys = new(StringComparer.Ordinal) { "id", "label" };
    private static readonly HashSet<string> RowKeys = new(StringComparer.Ordinal) { "id", "label", "box", "cells" };
    private static readonly HashSet<string> BoxKeys = new(StringComparer.Ordinal) { "x", "y", "width", "height" };

    private static bool IsValidId(string? id) => InteractionIds.IsValid(id);

    private static bool HasOnlyKeys(JsonElement obj, HashSet<string> allowed)
    {
        foreach (var property in obj.EnumerateObject())
        {
            if (!allowed.Contains(property.Name))
                return false;
        }
        return true;
    }

    // Payload (istemci girdisi) katı: yinelenen anahtar yasak (InteractionPlanSolution yazma kuralıyla aynı gerekçe).
    private static readonly JsonDocumentOptions StrictOptions = new() { MaxDepth = 16, AllowDuplicateProperties = false };

    // Plan/çözüm (kayıtlı veri) okuması toleranslı: okuma istisna atmamalı.
    private static readonly JsonDocumentOptions ReadOptions = new() { MaxDepth = 256 };

    // Yazma yolu (ValidateDefinition) katı.
    private static readonly JsonDocumentOptions DefinitionOptions = new() { MaxDepth = 64, AllowDuplicateProperties = false };

    public string InteractionType => Type;

    public bool Validate(string? plan, string? payload)
    {
        if (payload == null)
            return true;
        if (payload.Length > InteractionPayload.MaxLength)
            return false;
        if (!TryParsePayload(payload, out var selections))
            return false;
        if (selections.All(s => s.Columns.Count == 0))
            return true;
        if (!TryReadPlan(plan, ReadOptions, out var planInfo))
            return false;

        var seenRows = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (rowId, columns) in selections)
        {
            if (!planInfo.RowIds.Contains(rowId) || !seenRows.Add(rowId))
                return false;
            if (!planInfo.MultiPerRow && columns.Count > 1)
                return false;
            var seenColumns = new HashSet<string>(StringComparer.Ordinal);
            foreach (var columnId in columns)
            {
                if (!planInfo.ColumnIds.Contains(columnId) || !seenColumns.Add(columnId))
                    return false;
            }
        }

        return true;
    }

    /// <summary>Seçimsiz payload (<c>selections: {}</c> ya da tüm satırlar boş dizi) = cevap yok → null.</summary>
    public string? Normalize(string? payload)
        => payload != null && TryParsePayload(payload, out var selections) && selections.All(s => s.Columns.Count == 0)
            ? null
            : payload;

    public InteractionGradeResult Grade(InteractionGradeInput input)
    {
        if (!TryReadPlan(input.Plan, ReadOptions, out var plan) || plan.Rows.Count == 0)
            return InteractionGradeResult.Ungradable;
        var expected = ReadSolution(input.Solution);
        if (expected.Count == 0)
            return InteractionGradeResult.Ungradable;

        var actual = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        if (input.Payload != null && TryParsePayload(input.Payload, out var selections))
        {
            foreach (var (rowId, columns) in selections)
                actual.TryAdd(rowId, columns.ToHashSet(StringComparer.Ordinal));
        }

        var rows = new List<(string Id, bool Correct)>(plan.Rows.Count);
        foreach (var rowId in plan.Rows)
        {
            // Çözümü boş (ya da hiç olmayan) satır da satırdır; hiçbir seçim onu doğru yapmaz (dragDropLabeling ile aynı).
            var correct = expected.TryGetValue(rowId, out var solutionColumns)
                && solutionColumns.Count > 0
                && actual.TryGetValue(rowId, out var selected)
                && selected.SetEquals(solutionColumns);
            rows.Add((rowId, correct));
        }

        return InteractionGradeResult.Of(rows.Count(r => r.Correct), rows.Count, WriteFeedback(rows));
    }

    /// <summary>issue #584: öğe = satır (plan sırası), cevap = seçilen kolon(lar) (plan kolon sırasıyla birleştirilmiş).</summary>
    public IReadOnlyList<InteractionItemResponse> Analyze(InteractionGradeInput input)
    {
        if (!TryReadPlan(input.Plan, ReadOptions, out var plan) || plan.Rows.Count == 0)
            return [];
        var expected = ReadSolution(input.Solution);
        if (expected.Count == 0)
            return [];

        var actual = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        if (input.Payload != null && TryParsePayload(input.Payload, out var selections))
        {
            foreach (var (rowId, rowColumns) in selections)
                actual.TryAdd(rowId, rowColumns.ToHashSet(StringComparer.Ordinal));
        }

        var rowLabels = InteractionAnalysis.ToMap(InteractionAnalysis.ReadEntries(input.Plan, "rows"));
        var columns = InteractionAnalysis.ReadEntries(input.Plan, "columns");
        var items = new List<InteractionItemResponse>(plan.Rows.Count);
        for (var i = 0; i < plan.Rows.Count; i++)
        {
            var rowId = plan.Rows[i];
            var chosen = actual.TryGetValue(rowId, out var selected)
                ? columns.Where(c => selected.Contains(c.Id)).ToList()
                : [];
            var correct = expected.TryGetValue(rowId, out var solutionColumns)
                && solutionColumns.Count > 0
                && selected != null
                && selected.SetEquals(solutionColumns);
            items.Add(new InteractionItemResponse(
                rowId,
                rowLabels.GetValueOrDefault(rowId) ?? "#" + (i + 1),
                correct,
                chosen.Count == 0 ? null : string.Join(',', chosen.Select(c => c.Id)),
                chosen.Count == 0 ? null : string.Join(", ", chosen.Select(c => c.Label))));
        }
        return items;
    }

    /// <summary>
    /// Kayıt kuralı (issue #574): plan zorunlu ve şemaya uygun; çözüm boşsa geçerli (taslak, puanlanamaz), doluysa plandaki
    /// her satır için en az bir (çoklu seçim kapalıysa tam bir) plan kolonu, yinelenen kolon yok, plan dışı satır yok.
    /// </summary>
    public bool ValidateDefinition(string? plan, string? solution)
    {
        if (!TryReadPlan(plan, DefinitionOptions, out var planInfo, strict: true))
            return false;
        if (string.IsNullOrWhiteSpace(solution))
            return true;

        try
        {
            using var doc = JsonDocument.Parse(solution, DefinitionOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            var covered = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!IsValidId(property.Name) || !planInfo.RowIds.Contains(property.Name) || !covered.Add(property.Name))
                    return false;
                if (property.Value.ValueKind != JsonValueKind.Array)
                    return false;
                var columns = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in property.Value.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                        return false;
                    var columnId = item.GetString() ?? string.Empty;
                    if (!IsValidId(columnId) || !planInfo.ColumnIds.Contains(columnId) || !columns.Add(columnId))
                        return false;
                }
                if (columns.Count == 0 || (!planInfo.MultiPerRow && columns.Count != 1))
                    return false;
            }

            return covered.Count == planInfo.RowIds.Count;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private sealed record PlanInfo(List<string> Rows, HashSet<string> RowIds, HashSet<string> ColumnIds, bool MultiPerRow);

    /// <summary>
    /// Plan okuma. <paramref name="strict"/> false (okuma/kontrol): yalnız id'ler ve <c>multiPerRow</c>; bozuk öğe atlanır.
    /// true (kayıt): tam şema — tip, sürüm, sınırlar, id deseni, etiket uzunluğu, kutular ve her düzeyde izinli alanlar.
    /// </summary>
    private static bool TryReadPlan(string? plan, JsonDocumentOptions options, out PlanInfo info, bool strict = false)
    {
        info = new PlanInfo(new List<string>(), new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal), false);
        if (string.IsNullOrWhiteSpace(plan))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(plan, options);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            var multiPerRow = false;
            if (root.TryGetProperty("multiPerRow", out var multi))
            {
                if (multi.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    multiPerRow = multi.GetBoolean();
                else if (strict && multi.ValueKind != JsonValueKind.Null)
                    return false;
            }

            if (strict)
            {
                if (!HasOnlyKeys(root, RootKeys))
                    return false;
                if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                    || !string.Equals(type.GetString(), Type, StringComparison.Ordinal))
                    return false;
                if (root.TryGetProperty("version", out var version)
                    && (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1))
                    return false;
            }

            if (!root.TryGetProperty("columns", out var columns) || columns.ValueKind != JsonValueKind.Array
                || !root.TryGetProperty("rows", out var rows) || rows.ValueKind != JsonValueKind.Array)
                return false;

            var columnIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var column in columns.EnumerateArray())
            {
                if (!TryReadEntry(column, strict, out var id, ColumnKeys))
                {
                    if (strict) return false;
                    continue;
                }
                if (!columnIds.Add(id) && strict)
                    return false;
            }

            var rowOrder = new List<string>();
            var rowIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var row in rows.EnumerateArray())
            {
                if (!TryReadEntry(row, strict, out var id, RowKeys))
                {
                    if (strict) return false;
                    continue;
                }
                if (strict && !IsValidRowGeometry(row, columnIds))
                    return false;
                if (rowIds.Add(id))
                    rowOrder.Add(id);
                else if (strict)
                    return false;
            }

            if (strict && (columnIds.Count is < MinColumns or > MaxColumns || rowIds.Count is < MinRows or > MaxRows))
                return false;

            info = new PlanInfo(rowOrder, rowIds, columnIds, multiPerRow);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Kolon/satır öğesi: nesne, boş olmayan string <c>id</c> (katıda ≤ 32), varsa <c>label</c> string (katıda ≤ 200).</summary>
    private static bool TryReadEntry(JsonElement entry, bool strict, out string id, HashSet<string>? allowedKeys = null)
    {
        id = string.Empty;
        if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("id", out var idProp)
            || idProp.ValueKind != JsonValueKind.String)
            return false;
        id = idProp.GetString() ?? string.Empty;
        if (id.Length == 0 || (strict && !IsValidId(id)))
            return false;
        if (strict && allowedKeys != null && !HasOnlyKeys(entry, allowedKeys))
            return false;
        if (strict && entry.TryGetProperty("label", out var label) && label.ValueKind != JsonValueKind.Null
            && (label.ValueKind != JsonValueKind.String || (label.GetString()?.Length ?? 0) > MaxLabelLength))
            return false;
        return true;
    }

    /// <summary>Satır kutusu zorunlu; <c>cells</c> varsa nesne, anahtarlar plan kolonları, değerler kutu.</summary>
    private static bool IsValidRowGeometry(JsonElement row, HashSet<string> columnIds)
    {
        if (!row.TryGetProperty("box", out var box) || !IsValidBox(box))
            return false;
        if (!row.TryGetProperty("cells", out var cells) || cells.ValueKind == JsonValueKind.Null)
            return true;
        if (cells.ValueKind != JsonValueKind.Object)
            return false;
        foreach (var cell in cells.EnumerateObject())
        {
            if (!columnIds.Contains(cell.Name) || !IsValidBox(cell.Value))
                return false;
        }
        return true;
    }

    private static bool IsValidBox(JsonElement box)
    {
        if (box.ValueKind != JsonValueKind.Object || !HasOnlyKeys(box, BoxKeys))
            return false;
        return TryNumber(box, "x", out _) && TryNumber(box, "y", out _)
            && TryNumber(box, "width", out var width) && width > 0
            && TryNumber(box, "height", out var height) && height > 0;
    }

    private static bool TryNumber(JsonElement obj, string name, out double value)
    {
        value = 0;
        return obj.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.Number
            && prop.TryGetDouble(out value) && double.IsFinite(value);
    }

    /// <summary>
    /// Katı payload ayrıştırma: nesne, <c>type</c> = <see cref="Type"/>, varsa <c>version</c> = 1, <c>selections</c> nesnesi
    /// (satır id → boş olmayan string kolon id dizisi).
    /// </summary>
    private static bool TryParsePayload(string payload, out List<(string RowId, List<string> Columns)> selections)
    {
        selections = new List<(string, List<string>)>();
        try
        {
            using var doc = JsonDocument.Parse(payload, StrictOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;
            if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                || !string.Equals(type.GetString(), Type, StringComparison.Ordinal))
                return false;
            if (root.TryGetProperty("version", out var version)
                && (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1))
                return false;
            if (!root.TryGetProperty("selections", out var map) || map.ValueKind != JsonValueKind.Object)
                return false;

            foreach (var row in map.EnumerateObject())
            {
                if (!IsValidId(row.Name) || row.Value.ValueKind != JsonValueKind.Array)
                    return false;
                var columns = new List<string>();
                foreach (var item in row.Value.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                        return false;
                    var columnId = item.GetString() ?? string.Empty;
                    if (!IsValidId(columnId))
                        return false;
                    columns.Add(columnId);
                }
                selections.Add((row.Name, columns));
            }

            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            selections.Clear();
            return false;
        }
    }

    /// <summary>Çözüm: satır id → kabul edilen kolon kümesi. Toleranslı: bozuk çözüm = boş (puanlanamaz).</summary>
    private static Dictionary<string, HashSet<string>> ReadSolution(string? solution)
    {
        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(solution))
            return result;
        try
        {
            using var doc = JsonDocument.Parse(solution, ReadOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return result;

            foreach (var row in root.EnumerateObject())
            {
                if (row.Value.ValueKind != JsonValueKind.Array)
                    continue;
                var columns = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in row.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } columnId)
                        columns.Add(columnId);
                }
                result[row.Name] = columns;
            }

            return result;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
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
