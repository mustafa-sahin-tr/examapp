using System.IO;
using System.Text;
using System.Text.Json;
using ExamApp.Api.Helpers;

namespace ExamApp.Api.Services.Interactions;

/// <summary>
/// issue #588: <c>numberLine</c> kontrolcüsü — ızgara tuvali ailesinin (bileşen F) sayı doğrusu dilimi ("kesir kartlarını sayı
/// doğrusuna yerleştirelim", olasılık spektrumu "imkânsız … kesin").
/// <list type="bullet">
/// <item>Plan v1: <c>{version:1,type:"numberLine",divisions,labels:[{at,text}],cards:[{id,label}],tolerance?,hideTicks?}</c>. Doğru
/// <c>divisions</c> (1..100) eş parçaya bölünür; çentikler 0..divisions. <c>labels</c> çentiğin altındaki metin (çentik başına en çok
/// bir, düz metin ≤ 30; uçlar da dahil hepsi isteğe bağlı). Kartlar 1..20, etiket zorunlu düz metin ≤ 40. <c>tolerance</c> 0..10 çentik
/// (tahmin soruları: 100 bölmede "0,37'yi yaklaşık işaretle"); <c>hideTicks</c> yalnız görünüm (etiketsiz çentikler çizilmez).
/// Değerler planda yok: etiketlerden ve çentik sırasından öğrenci bulur.</item>
/// <item>Çözüm: <c>{"positions":{"k1":3,"k2":null}}</c> — her kart için çentik (0..divisions) ya da <c>null</c> (çeldirici: doğruya
/// konmamalı). En az bir kartın çentiği olur.</item>
/// <item>Öğrenci payload'u: <c>{version:1,type:"numberLine",placed:{"k1":3}}</c> — doğruya konan kartlar; bir çentiğe birden çok kart
/// konabilir. Kartsız = cevap yok.</item>
/// </list>
/// Kontrol: satır = kart (plan sırası); hedefli kart doğru ⇔ konduğu çentik hedefe tolerans içinde yakın; çeldirici doğru ⇔ konmamış
/// (sürükle-yerleştir kartlarıyla aynı). Geri bildirim <c>{"rows":[{id,correct}]}</c>; hedef çentik yazılmaz.
/// </summary>
public sealed class NumberLineGrader : IInteractionGrader
{
    public const string Type = "numberLine";

    public const int MaxDivisions = 100;
    public const int MaxCards = 20;
    public const int MaxTolerance = 10;
    public const int MaxLabelLength = 30;
    public const int MaxCardLength = 40;

    private static readonly HashSet<string> RootKeys = new(StringComparer.Ordinal)
    {
        "version", "type", "divisions", "labels", "cards", "tolerance", "hideTicks",
    };
    private static readonly HashSet<string> LabelKeys = new(StringComparer.Ordinal) { "at", "text" };
    private static readonly HashSet<string> CardKeys = new(StringComparer.Ordinal) { "id", "label" };

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
        return TryReadPlan(plan, ReadOptions, strict: false, out var line) && TryParsePayload(payload, line, out _);
    }

    /// <summary>Kartsız cevap = cevap yok → null.</summary>
    public string? Normalize(string? payload)
    {
        if (payload == null || !TryParsePayload(payload, null, out var placed))
            return payload;
        return placed.Count == 0 ? null : payload;
    }

    public InteractionGradeResult Grade(InteractionGradeInput input)
    {
        if (!TryReadPlan(input.Plan, ReadOptions, strict: false, out var line) || !TryReadSolution(input.Solution, line, out var targets))
            return InteractionGradeResult.Ungradable;
        var placed = Placed(line, input.Payload);
        var rows = line.Cards.Where(c => targets.ContainsKey(c.Id))
            .Select(c => (c.Id, IsCorrect(line, targets[c.Id], placed.TryGetValue(c.Id, out var at) ? at : null))).ToList();
        return InteractionGradeResult.Of(rows.Count(r => r.Item2), rows.Count, WriteFeedback(rows));
    }

    /// <summary>issue #584: öğe = kart, cevap = konduğu çentik (etiketi varsa etiket, yoksa "3/8" — çentik / bölme).</summary>
    public IReadOnlyList<InteractionItemResponse> Analyze(InteractionGradeInput input)
    {
        if (!TryReadPlan(input.Plan, ReadOptions, strict: false, out var line) || !TryReadSolution(input.Solution, line, out var targets))
            return [];
        var placed = Placed(line, input.Payload);
        return line.Cards.Where(c => targets.ContainsKey(c.Id)).Select(c =>
        {
            int? at = placed.TryGetValue(c.Id, out var value) ? value : null;
            var key = at?.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var label = at is { } tick
                ? line.Labels.TryGetValue(tick, out var text) ? text : $"{tick}/{line.Divisions}"
                : null;
            return new InteractionItemResponse(c.Id, InteractionAnalysis.TextLabel(c.Label), IsCorrect(line, targets[c.Id], at), key,
                label == null ? null : InteractionAnalysis.TextLabel(label), Distractor: targets[c.Id] == null);
        }).ToList();
    }

    /// <summary>
    /// Kayıt kuralı (issue #588): bölme 1..100, tolerans 0..10 (bölmeden küçük), etiketler çentik içinde ve çentik başına tek, kartlar
    /// 1..20 tekil id ve dolu etiket, metinler düz metin. Çözüm boşsa geçerli (taslak); doluysa her kartın girdisi var (çentik ya da null),
    /// en az bir hedefli kart.
    /// </summary>
    public bool ValidateDefinition(string? plan, string? solution)
    {
        if (!TryReadPlan(plan, DefinitionOptions, strict: true, out var line))
            return false;
        if (string.IsNullOrWhiteSpace(solution))
            return true;
        try
        {
            using var doc = JsonDocument.Parse(solution, DefinitionOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasOnlyKeys(root, ["positions"])
                || !root.TryGetProperty("positions", out var positions) || positions.ValueKind != JsonValueKind.Object)
                return false;
            var ids = line.Cards.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var targeted = 0;
            foreach (var entry in positions.EnumerateObject())
            {
                if (!ids.Contains(entry.Name) || !seen.Add(entry.Name))
                    return false;
                if (entry.Value.ValueKind == JsonValueKind.Null)
                    continue;
                if (entry.Value.ValueKind != JsonValueKind.Number || !entry.Value.TryGetInt32(out var at) || at < 0 || at > line.Divisions)
                    return false;
                targeted++;
            }
            return seen.Count == ids.Count && targeted > 0;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    // ---- Model ----

    private sealed record Card(string Id, string Label);

    private sealed record Line(int Divisions, int Tolerance, Dictionary<int, string> Labels, List<Card> Cards);

    private static bool IsCorrect(Line line, int? target, int? at)
        => target is { } t ? at is { } a && Math.Abs(a - t) <= line.Tolerance : at == null;

    /// <summary>Kart id'si → çentik.</summary>
    private static Dictionary<string, int> Placed(Line line, string? payload)
        => payload != null && TryParsePayload(payload, line, out var placed) ? placed : new Dictionary<string, int>(StringComparer.Ordinal);

    // ---- Okuma ----

    private static bool TryReadPlan(string? plan, JsonDocumentOptions options, bool strict, out Line line)
    {
        line = new Line(0, 0, new Dictionary<int, string>(), new List<Card>());
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
                if (root.TryGetProperty("hideTicks", out var hide) && hide.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return false;
            }
            if (!TryReadInt(root, "divisions", out var divisions) || divisions is < 1 or > MaxDivisions)
                return false;

            var tolerance = 0;
            if (root.TryGetProperty("tolerance", out var toleranceElement) && toleranceElement.ValueKind != JsonValueKind.Null)
            {
                if (toleranceElement.ValueKind != JsonValueKind.Number || !toleranceElement.TryGetInt32(out tolerance)
                    || tolerance < 0 || tolerance > MaxTolerance || (strict && tolerance >= divisions))
                {
                    if (strict) return false;
                    tolerance = 0;
                }
            }

            var labels = new Dictionary<int, string>();
            if (root.TryGetProperty("labels", out var labelsElement) && labelsElement.ValueKind != JsonValueKind.Null)
            {
                if (labelsElement.ValueKind != JsonValueKind.Array)
                {
                    if (strict) return false;
                }
                else
                {
                    foreach (var raw in labelsElement.EnumerateArray())
                    {
                        if (raw.ValueKind != JsonValueKind.Object || (strict && !HasOnlyKeys(raw, LabelKeys))
                            || !TryReadInt(raw, "at", out var at) || at < 0 || at > divisions || labels.ContainsKey(at)
                            || !TryReadString(raw, "text", out var text)
                            || (strict && (text.Length > MaxLabelLength || PlainTextAttribute.ContainsControlCharacters(text))))
                        {
                            if (strict) return false;
                            continue;
                        }
                        labels[at] = text;
                    }
                }
            }

            if (!root.TryGetProperty("cards", out var cardsElement) || cardsElement.ValueKind != JsonValueKind.Array)
                return false;
            var cards = new List<Card>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in cardsElement.EnumerateArray())
            {
                if (raw.ValueKind != JsonValueKind.Object || (strict && !HasOnlyKeys(raw, CardKeys))
                    || !TryReadString(raw, "id", out var id) || !InteractionIds.IsValid(id) || !ids.Add(id)
                    || !TryReadString(raw, "label", out var label) || string.IsNullOrWhiteSpace(label)
                    || (strict && (label.Length > MaxCardLength || PlainTextAttribute.ContainsControlCharacters(label))))
                {
                    if (strict) return false;
                    continue;
                }
                cards.Add(new Card(id, label));
            }
            if (cards.Count is 0 or > MaxCards)
                return false;
            line = new Line(divisions, tolerance, labels, cards);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
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
    /// Katı payload: <c>placed</c> nesnesi, kart id'si → çentik (tam sayı). Plan verilmişse id planda ve çentik 0..divisions; verilmezse
    /// (Normalize) yalnız biçim: geçerli id, 0..100, en çok 20 kart.
    /// </summary>
    private static bool TryParsePayload(string payload, Line? line, out Dictionary<string, int> placed)
    {
        placed = new Dictionary<string, int>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(payload, StrictOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasOnlyKeys(root, ["version", "type", "placed"]))
                return false;
            if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                || !string.Equals(type.GetString(), Type, StringComparison.Ordinal))
                return false;
            if (root.TryGetProperty("version", out var version)
                && (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1))
                return false;
            if (!root.TryGetProperty("placed", out var element) || element.ValueKind != JsonValueKind.Object)
                return false;
            var ids = line?.Cards.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            var max = line?.Divisions ?? MaxDivisions;
            foreach (var entry in element.EnumerateObject())
            {
                if (!InteractionIds.IsValid(entry.Name) || (ids != null && !ids.Contains(entry.Name))
                    || entry.Value.ValueKind != JsonValueKind.Number || !entry.Value.TryGetInt32(out var at) || at < 0 || at > max
                    || !placed.TryAdd(entry.Name, at) || placed.Count > MaxCards)
                {
                    placed.Clear();
                    return false;
                }
            }
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            placed.Clear();
            return false;
        }
    }

    /// <summary>Çözüm: kart id'si → hedef çentik (null = çeldirici). Toleranslı: plana uymayan atlanır; hedefli kart yoksa false.</summary>
    private static bool TryReadSolution(string? solution, Line line, out Dictionary<string, int?> targets)
    {
        targets = new Dictionary<string, int?>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(solution))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(solution, ReadOptions);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("positions", out var element)
                || element.ValueKind != JsonValueKind.Object)
                return false;
            var ids = line.Cards.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var entry in element.EnumerateObject())
            {
                if (!ids.Contains(entry.Name))
                    continue;
                if (entry.Value.ValueKind == JsonValueKind.Null)
                    targets[entry.Name] = null;
                else if (entry.Value.ValueKind == JsonValueKind.Number && entry.Value.TryGetInt32(out var at) && at >= 0 && at <= line.Divisions)
                    targets[entry.Name] = at;
            }
            if (!targets.Values.Any(t => t != null))
            {
                targets.Clear();
                return false;
            }
            return true;
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
