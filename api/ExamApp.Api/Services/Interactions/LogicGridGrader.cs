using System.IO;
using System.Text;
using System.Text.Json;
using ExamApp.Api.Helpers;

namespace ExamApp.Api.Services.Interactions;

/// <summary>
/// issue #586: <c>logicGrid</c> kontrolcüsü — sözel mantık tablosu (bileşen G; kişi × özellik).
/// <list type="bullet">
/// <item>Plan v1: <c>{version:1,type:"logicGrid",subject:{label,items:[{id,label}]},categories:[{id,label,items:[{id,label}]}],clues:[{id,text}]}</c>.
/// Konu öğeleri (ör. kişiler) satırlar; her kategorinin (ör. renk) seçenek sayısı konu öğesi sayısına eşittir. İpuçları düz metin.</item>
/// <item>Çözüm: <c>{"assign":{"p1":{"c1":"o2",...},...}}</c> — her konu öğesine her kategoriden tek seçenek; bir kategoride her seçenek
/// tek konu öğesine ait (birebir).</item>
/// <item>Öğrenci payload'u: <c>{version:1,type:"logicGrid",cells:[{row:"p1",col:"o2",mark:"yes"|"no"}]}</c> — ✓ / ✗ işaretleri
/// (✗ öğrencinin notudur, kontrolde yalnız ✓ sayılır). İsteğe bağlı <c>notes</c>: kitaptaki merdiven tablonun kategori × kategori
/// karalama blokları, <c>{"c3:c1":"y..n...."}</c> — anahtar satır kategorisi : sütun kategorisi (satır kategorisi planda sonra gelir),
/// değer satır öncelikli n × n karakter (<c>.</c> boş, <c>y</c> ✓, <c>n</c> ✗). Notlar puanlanmaz, yalnız saklanır.</item>
/// </list>
/// Kontrol: satır = (konu öğesi, kategori) çifti (konu sırası × kategori sırası, id <c>p1:c1</c>); doğru ⇔ o kategoride tam bir ✓ var ve
/// çözümdeki seçenek. Geri bildirim <c>{"rows":[{id,correct}]}</c>; doğru seçenek yazılmaz. Çözümü olmayan soru puanlanamaz (0/0).
/// </summary>
public sealed class LogicGridGrader : IInteractionGrader
{
    public const string Type = "logicGrid";

    public const string Yes = "yes";
    public const string No = "no";

    public const int MinItems = 2;
    public const int MaxItems = 8;
    public const int MaxCategories = 4;
    public const int MaxClues = 20;
    public const int MaxLabelLength = 60;
    public const int MaxClueLength = 300;

    private static readonly HashSet<string> RootKeys = new(StringComparer.Ordinal) { "version", "type", "subject", "categories", "clues" };
    private static readonly HashSet<string> GroupKeys = new(StringComparer.Ordinal) { "id", "label", "items" };
    private static readonly HashSet<string> ItemKeys = new(StringComparer.Ordinal) { "id", "label" };
    private static readonly HashSet<string> ClueKeys = new(StringComparer.Ordinal) { "id", "text" };

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
        if (!TryReadPlan(plan, ReadOptions, strict: false, out var grid))
            return false;
        return TryParsePayload(payload, out var cells, out var notes)
            && cells.All(c => grid.Subjects.Any(s => s.Id == c.Row) && grid.OptionCategory.ContainsKey(c.Col))
            && notes.All(n => IsNoteBlock(grid, n.Key, n.Value));
    }

    /// <summary>İşaretsiz payload (ne ✓/✗ ne karalama işareti) = cevap yok → null.</summary>
    public string? Normalize(string? payload)
        => payload != null && TryParsePayload(payload, out var cells, out var notes) && cells.Count == 0
            && notes.Values.All(text => text.All(ch => ch == NoteEmpty)) ? null : payload;

    /// <summary>Karalama bloğu: iki farklı kategori, satır kategorisi planda sonra; metin n × n karakter.</summary>
    private static bool IsNoteBlock(Grid grid, string key, string text)
    {
        var parts = key.Split(':');
        if (parts.Length != 2)
            return false;
        var row = grid.Categories.FindIndex(c => c.Id == parts[0]);
        var col = grid.Categories.FindIndex(c => c.Id == parts[1]);
        return row > col && col >= 0
            && text.Length == grid.Categories[row].Items.Count * grid.Categories[col].Items.Count;
    }

    public InteractionGradeResult Grade(InteractionGradeInput input)
    {
        if (!TryReadPlan(input.Plan, ReadOptions, strict: false, out var grid) || grid.Subjects.Count == 0 || grid.Categories.Count == 0
            || !TryReadSolution(input.Solution, grid, out var assign))
            return InteractionGradeResult.Ungradable;
        var chosen = Chosen(grid, input.Payload);
        var rows = Pairs(grid).Select(p => (Key(p.Subject, p.Category), IsCorrect(p.Subject, p.Category, chosen, assign))).ToList();
        return InteractionGradeResult.Of(rows.Count(r => r.Item2), rows.Count, WriteFeedback(rows));
    }

    /// <summary>issue #584: öğe = (konu öğesi, kategori) çifti, cevap = ✓ konan seçenek (birden çoksa ilk; yoksa cevapsız).</summary>
    public IReadOnlyList<InteractionItemResponse> Analyze(InteractionGradeInput input)
    {
        if (!TryReadPlan(input.Plan, ReadOptions, strict: false, out var grid) || !TryReadSolution(input.Solution, grid, out var assign))
            return [];
        var chosen = Chosen(grid, input.Payload);
        return Pairs(grid).Select(p =>
        {
            var picks = chosen.GetValueOrDefault((p.Subject.Id, p.Category.Id)) ?? [];
            var first = picks.FirstOrDefault();
            var label = first == null ? null : p.Category.Items.FirstOrDefault(i => i.Id == first)?.Label ?? first;
            return new InteractionItemResponse(Key(p.Subject, p.Category),
                InteractionAnalysis.TextLabel($"{p.Subject.Label} – {p.Category.Label}"),
                IsCorrect(p.Subject, p.Category, chosen, assign), first, label == null ? null : InteractionAnalysis.TextLabel(label));
        }).ToList();
    }

    /// <summary>
    /// Kayıt kuralı (issue #586): 2..8 konu öğesi, 1..4 kategori, her kategoride konu öğesi kadar seçenek, tüm id'ler tekil, etiketler
    /// zorunlu düz metin (≤ 60), en çok 20 ipucu (≤ 300). Çözüm boşsa geçerli (taslak); doluysa her konu öğesine her kategoriden bir
    /// seçenek ve kategoride birebir eşleme.
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
            if (root.ValueKind != JsonValueKind.Object || !HasOnlyKeys(root, ["assign"])
                || !root.TryGetProperty("assign", out var assignElement) || assignElement.ValueKind != JsonValueKind.Object)
                return false;
            var subjects = grid.Subjects.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
            var used = new HashSet<string>(StringComparer.Ordinal);
            var count = 0;
            foreach (var subject in assignElement.EnumerateObject())
            {
                if (!subjects.Contains(subject.Name) || subject.Value.ValueKind != JsonValueKind.Object)
                    return false;
                count++;
                var categories = 0;
                foreach (var category in subject.Value.EnumerateObject())
                {
                    var group = grid.Categories.FirstOrDefault(c => c.Id == category.Name);
                    if (group == null || category.Value.ValueKind != JsonValueKind.String || category.Value.GetString() is not { } option
                        || group.Items.All(i => i.Id != option) || !used.Add(option))
                        return false;
                    categories++;
                }
                if (categories != grid.Categories.Count)
                    return false;
            }
            return count == grid.Subjects.Count;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    // ---- Model ----

    private sealed record Item(string Id, string Label);

    private sealed record Group(string Id, string Label, List<Item> Items);

    private sealed record Grid(List<Item> Subjects, List<Group> Categories, Dictionary<string, string> OptionCategory);

    private readonly record struct Mark(string Row, string Col, bool Yes);

    private static string Key(Item subject, Group category) => subject.Id + ":" + category.Id;

    private static IEnumerable<(Item Subject, Group Category)> Pairs(Grid grid)
        => grid.Subjects.SelectMany(s => grid.Categories.Select(c => (s, c)));

    /// <summary>(konu öğesi, kategori) → ✓ konan seçenekler (payload sırası).</summary>
    private static Dictionary<(string, string), List<string>> Chosen(Grid grid, string? payload)
    {
        var chosen = new Dictionary<(string, string), List<string>>();
        if (payload == null || !TryParsePayload(payload, out var cells, out _))
            return chosen;
        foreach (var cell in cells.Where(c => c.Yes))
        {
            if (!grid.OptionCategory.TryGetValue(cell.Col, out var category))
                continue;
            var key = (cell.Row, category);
            if (!chosen.TryGetValue(key, out var list))
                chosen[key] = list = new List<string>();
            if (!list.Contains(cell.Col))
                list.Add(cell.Col);
        }
        return chosen;
    }

    private static bool IsCorrect(Item subject, Group category, Dictionary<(string, string), List<string>> chosen,
        Dictionary<(string, string), string> assign)
        => assign.TryGetValue((subject.Id, category.Id), out var expected)
            && chosen.TryGetValue((subject.Id, category.Id), out var picks) && picks.Count == 1 && picks[0] == expected;

    // ---- Okuma ----

    private static bool TryReadPlan(string? plan, JsonDocumentOptions options, bool strict, out Grid grid)
    {
        grid = new Grid(new List<Item>(), new List<Group>(), new Dictionary<string, string>(StringComparer.Ordinal));
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
            if (strict)
            {
                if (!HasOnlyKeys(root, RootKeys))
                    return false;
                if (root.TryGetProperty("version", out var version)
                    && (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1))
                    return false;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (!root.TryGetProperty("subject", out var subjectElement) || !TryReadGroup(subjectElement, strict, ids, requireId: false, out var subject))
                return false;
            var categories = new List<Group>();
            if (!root.TryGetProperty("categories", out var categoriesElement) || categoriesElement.ValueKind != JsonValueKind.Array)
                return false;
            foreach (var item in categoriesElement.EnumerateArray())
            {
                if (!TryReadGroup(item, strict, ids, requireId: true, out var group))
                {
                    if (strict) return false;
                    continue;
                }
                categories.Add(group);
            }

            if (strict)
            {
                if (subject.Items.Count is < MinItems or > MaxItems || categories.Count is 0 or > MaxCategories
                    || categories.Any(c => c.Items.Count != subject.Items.Count))
                    return false;
                if (!TryValidateClues(root, ids))
                    return false;
            }

            var optionCategory = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var group in categories)
                foreach (var option in group.Items)
                    optionCategory[option.Id] = group.Id;
            grid = new Grid(subject.Items, categories, optionCategory);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Konu ya da kategori: <c>{id?,label,items:[{id,label}]}</c>. Tüm id'ler (gruplar, öğeler, ipuçları) plan içinde tekil.</summary>
    private static bool TryReadGroup(JsonElement element, bool strict, HashSet<string> ids, bool requireId, out Group group)
    {
        group = new Group(string.Empty, string.Empty, new List<Item>());
        if (element.ValueKind != JsonValueKind.Object || (strict && !HasOnlyKeys(element, requireId ? GroupKeys : ["label", "items"])))
            return false;
        var id = string.Empty;
        if (requireId && (!TryReadString(element, "id", out id) || !InteractionIds.IsValid(id) || !ids.Add(id)))
            return false;
        if (!TryReadLabel(element, "label", MaxLabelLength, strict, out var label))
            return false;
        if (!element.TryGetProperty("items", out var itemsElement) || itemsElement.ValueKind != JsonValueKind.Array)
            return false;
        var items = new List<Item>();
        foreach (var raw in itemsElement.EnumerateArray())
        {
            if (raw.ValueKind != JsonValueKind.Object || (strict && !HasOnlyKeys(raw, ItemKeys))
                || !TryReadString(raw, "id", out var itemId) || !InteractionIds.IsValid(itemId) || !ids.Add(itemId)
                || !TryReadLabel(raw, "label", MaxLabelLength, strict, out var itemLabel))
            {
                if (strict) return false;
                continue;
            }
            items.Add(new Item(itemId, itemLabel));
        }
        group = new Group(id, label, items);
        return true;
    }

    private static bool TryValidateClues(JsonElement root, HashSet<string> ids)
    {
        if (!root.TryGetProperty("clues", out var clues) || clues.ValueKind == JsonValueKind.Null)
            return true;
        if (clues.ValueKind != JsonValueKind.Array || clues.GetArrayLength() > MaxClues)
            return false;
        foreach (var clue in clues.EnumerateArray())
        {
            if (clue.ValueKind != JsonValueKind.Object || !HasOnlyKeys(clue, ClueKeys) || !TryReadString(clue, "id", out var id)
                || !InteractionIds.IsValid(id) || !ids.Add(id) || !TryReadLabel(clue, "text", MaxClueLength, strict: true, out _))
                return false;
        }
        return true;
    }

    /// <summary>Etiket: kayıtta zorunlu, düz metin, sınır içinde; okumada eksikse boş.</summary>
    private static bool TryReadLabel(JsonElement element, string name, int max, bool strict, out string label)
    {
        label = element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? string.Empty : string.Empty;
        return !strict || (!string.IsNullOrWhiteSpace(label) && label.Length <= max && !PlainTextAttribute.ContainsControlCharacters(label));
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

    private const char NoteEmpty = '.';

    /// <summary>
    /// Katı payload: <c>cells</c> en çok 8 × 32 işaret, her biri <c>{row,col,mark}</c>, aynı hücre iki kez yok; <c>notes</c> en çok
    /// 6 blok (4 kategoride kategori çiftleri), değer yalnız <c>. y n</c> ve en çok 8 × 8 karakter (plana uyum <see cref="Validate"/>'te).
    /// </summary>
    private static bool TryParsePayload(string payload, out List<Mark> cells, out Dictionary<string, string> notes)
    {
        cells = new List<Mark>();
        notes = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(payload, StrictOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasOnlyKeys(root, ["version", "type", "cells", "notes"]))
                return false;
            if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                || !string.Equals(type.GetString(), Type, StringComparison.Ordinal))
                return false;
            if (root.TryGetProperty("version", out var version)
                && (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1))
                return false;
            if (!root.TryGetProperty("cells", out var array) || array.ValueKind != JsonValueKind.Array
                || array.GetArrayLength() > MaxItems * MaxItems * MaxCategories)
                return false;
            var seen = new HashSet<(string, string)>();
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !HasOnlyKeys(item, ["row", "col", "mark"])
                    || !TryReadString(item, "row", out var row) || !InteractionIds.IsValid(row)
                    || !TryReadString(item, "col", out var col) || !InteractionIds.IsValid(col)
                    || !TryReadString(item, "mark", out var mark) || mark is not (Yes or No) || !seen.Add((row, col)))
                    return false;
                cells.Add(new Mark(row, col, mark == Yes));
            }
            if (root.TryGetProperty("notes", out var notesElement))
            {
                if (notesElement.ValueKind != JsonValueKind.Object)
                    return false;
                foreach (var block in notesElement.EnumerateObject())
                {
                    if (block.Value.ValueKind != JsonValueKind.String || block.Value.GetString() is not { } text
                        || text.Length > MaxItems * MaxItems || text.Any(ch => ch is not (NoteEmpty or 'y' or 'n'))
                        || notes.Count >= MaxCategories * (MaxCategories - 1) / 2 || !notes.TryAdd(block.Name, text))
                        return false;
                }
            }
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            cells.Clear();
            notes.Clear();
            return false;
        }
    }

    /// <summary>Çözüm: (konu öğesi, kategori) → seçenek. Toleranslı: plana uymayan atlanır; okunamaz / boşsa false.</summary>
    private static bool TryReadSolution(string? solution, Grid grid, out Dictionary<(string, string), string> assign)
    {
        assign = new Dictionary<(string, string), string>();
        if (string.IsNullOrWhiteSpace(solution))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(solution, ReadOptions);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("assign", out var element)
                || element.ValueKind != JsonValueKind.Object)
                return false;
            var subjects = grid.Subjects.Select(s => s.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var subject in element.EnumerateObject())
            {
                if (!subjects.Contains(subject.Name) || subject.Value.ValueKind != JsonValueKind.Object)
                    continue;
                foreach (var category in subject.Value.EnumerateObject())
                {
                    if (category.Value.ValueKind == JsonValueKind.String && category.Value.GetString() is { } option
                        && grid.OptionCategory.TryGetValue(option, out var owner) && owner == category.Name)
                        assign[(subject.Name, category.Name)] = option;
                }
            }
            return assign.Count > 0;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            assign.Clear();
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
