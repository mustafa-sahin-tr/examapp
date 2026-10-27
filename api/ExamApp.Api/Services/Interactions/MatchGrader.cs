using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ExamApp.Api.Services.Interactions;

/// <summary>
/// issue #576: <c>match</c> kontrolcüsü — sol ↔ sağ eşleştirme (çeldiricili; örnek çift önceden bağlı). Sol öğe başına
/// doğru/yanlış.
/// <list type="bullet">
/// <item>Plan v1: <c>{version:1,type:"match",left:[{id,label?,box?}],right:[{id,label?,box?}],prelinked:[{leftId,rightId}]}</c>;
/// her öğede <c>label</c> ya da <c>box</c> (soru görselinden kırpma) zorunlu. <c>prelinked</c> = bağlı gösterilen örnek çiftler;
/// puanlanmaz, çözümde yer almaz, öğrenci payload'unda kullanılamaz.</item>
/// <item>Çözüm (#570, <c>Question.InteractionSolution</c>): <c>{"l2":"r3", ...}</c> — örnek olmayan HER sol için bir sağ; bir sağ
/// en çok bir kez. Çözümde olmayan sağ öğeler çeldiricidir.</item>
/// <item>Öğrenci payload'u: <c>{version:1,type:"match",pairs:[{leftId,rightId}]}</c>; bir sol / bir sağ en çok bir kez.</item>
/// </list>
/// Satırlar = örnek olmayan TÜM sol öğeler, plan sırasıyla; satır doğru ⇔ çözümdeki sağa bağlı (bağsız = yanlış). Geri
/// bildirim yalnız sol id + doğru/yanlış taşır; beklenen sağ ASLA yazılmaz. Çözümü olmayan soru puanlanamaz (0/0).
/// Kayıtta (<see cref="PrepareForStorage"/>) sağ öğeler rastgele sıraya dizilir ve opak id alır (review #576 H1/M1); sol öğeler
/// yazarlık sırasında kalır. Kutular <see cref="InteractionBoxes"/> sınırına uyar.
/// </summary>
public sealed class MatchGrader : IInteractionGrader
{
    public const string Type = "match";

    public const int MinItems = 1;
    public const int MaxItems = 30;
    public const int MaxLabelLength = 200;

    private static readonly HashSet<string> RootKeys = new(StringComparer.Ordinal) { "version", "type", "left", "right", "prelinked" };
    private static readonly HashSet<string> ItemKeys = new(StringComparer.Ordinal) { "id", "label", "box" };
    private static readonly HashSet<string> PairKeys = new(StringComparer.Ordinal) { "leftId", "rightId" };

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

    // Payload (istemci girdisi) katı: yinelenen anahtar yasak.
    private static readonly JsonDocumentOptions StrictOptions = new() { MaxDepth = 16, AllowDuplicateProperties = false };

    // Plan/çözüm (kayıtlı veri) okuması toleranslı: okuma istisna atmamalı.
    private static readonly JsonDocumentOptions ReadOptions = new() { MaxDepth = 256 };

    // Yazma yolu (ValidateDefinition) katı.
    private static readonly JsonDocumentOptions DefinitionOptions = new() { MaxDepth = 64, AllowDuplicateProperties = false };

    private readonly IInteractionRandom _random;

    /// <param name="random">Kayıtta karıştırma için; yoksa kriptografik (<see cref="CryptoInteractionRandom"/>).</param>
    public MatchGrader(IInteractionRandom? random = null)
    {
        _random = random ?? CryptoInteractionRandom.Instance;
    }

    public string InteractionType => Type;

    public bool Validate(string? plan, string? payload)
    {
        if (payload == null)
            return true;
        if (payload.Length > InteractionPayload.MaxLength)
            return false;
        if (!TryParsePayload(payload, out var pairs))
            return false;
        if (pairs.Count == 0)
            return true;
        if (!TryReadPlan(plan, ReadOptions, out var planInfo))
            return false;

        var seenLeft = new HashSet<string>(StringComparer.Ordinal);
        var seenRight = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (leftId, rightId) in pairs)
        {
            if (!planInfo.LeftIds.Contains(leftId) || !planInfo.RightIds.Contains(rightId))
                return false;
            if (planInfo.PrelinkedLeft.Contains(leftId) || planInfo.PrelinkedRight.Contains(rightId))
                return false;
            if (!seenLeft.Add(leftId) || !seenRight.Add(rightId))
                return false;
        }

        return true;
    }

    /// <summary>Eşleşmesiz payload (<c>pairs: []</c>) = cevap yok → null.</summary>
    public string? Normalize(string? payload)
        => payload != null && TryParsePayload(payload, out var pairs) && pairs.Count == 0 ? null : payload;

    public InteractionGradeResult Grade(InteractionGradeInput input)
    {
        if (!TryReadPlan(input.Plan, ReadOptions, out var plan))
            return InteractionGradeResult.Ungradable;
        var gradable = plan.Left.Where(l => !plan.PrelinkedLeft.Contains(l)).ToList();
        if (gradable.Count == 0)
            return InteractionGradeResult.Ungradable;
        var expected = ReadSolution(input.Solution);
        if (expected.Count == 0)
            return InteractionGradeResult.Ungradable;

        var actual = new Dictionary<string, string>(StringComparer.Ordinal);
        if (input.Payload != null && TryParsePayload(input.Payload, out var pairs))
        {
            foreach (var (leftId, rightId) in pairs)
                actual.TryAdd(leftId, rightId);
        }

        var rows = new List<(string Id, bool Correct)>(gradable.Count);
        foreach (var leftId in gradable)
        {
            var correct = expected.TryGetValue(leftId, out var expectedRight)
                && actual.TryGetValue(leftId, out var chosen)
                && string.Equals(chosen, expectedRight, StringComparison.Ordinal);
            rows.Add((leftId, correct));
        }

        return InteractionGradeResult.Of(rows.Count(r => r.Correct), rows.Count, WriteFeedback(rows));
    }

    /// <summary>
    /// Kayıt kuralı (issue #576): plan zorunlu ve şemaya uygun (en az bir örnek olmayan sol); çözüm boşsa geçerli (taslak),
    /// doluysa örnek olmayan her sol için tam bir plan sağı, örnek çiftin sağı kullanılamaz, bir sağ en çok bir kez.
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
            var usedRight = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in root.EnumerateObject())
            {
                if (!IsValidId(property.Name) || !planInfo.LeftIds.Contains(property.Name)
                    || planInfo.PrelinkedLeft.Contains(property.Name) || !covered.Add(property.Name))
                    return false;
                if (property.Value.ValueKind != JsonValueKind.String)
                    return false;
                var rightId = property.Value.GetString() ?? string.Empty;
                if (!IsValidId(rightId) || !planInfo.RightIds.Contains(rightId)
                    || planInfo.PrelinkedRight.Contains(rightId) || !usedRight.Add(rightId))
                    return false;
            }

            return covered.Count == planInfo.LeftIds.Count - planInfo.PrelinkedLeft.Count;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// review #576 (H1/M1): sağ öğeleri kriptografik rastgele sıraya dizer, her birine sol id'lerinden ve birbirinden farklı 8
    /// karakterlik opak id verir; çözümü ve örnek çiftleri yeni id'lere eşler. Sol öğeler değişmez. Okunamayan tanım aynen döner.
    /// </summary>
    public (string? Plan, string? Solution) PrepareForStorage(string? plan, string? solution)
    {
        if (string.IsNullOrWhiteSpace(plan))
            return (plan, solution);
        try
        {
            if (JsonNode.Parse(plan, documentOptions: DefinitionOptions) is not JsonObject root
                || root["right"] is not JsonArray right || root["left"] is not JsonArray left)
                return (plan, solution);

            var taken = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in left)
            {
                if (item?["id"]?.GetValue<string>() is { } leftId)
                    taken.Add(leftId);
            }

            var items = right.Select(r => r!.DeepClone()).ToList();
            InteractionScramble.Shuffle(items, _random);
            var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                var obj = (JsonObject)item!;
                var newId = InteractionScramble.NewOpaqueId(taken, _random);
                idMap[obj["id"]!.GetValue<string>()] = newId;
                obj["id"] = newId;
            }
            right.Clear();
            foreach (var item in items)
                right.Add(item);

            if (root["prelinked"] is JsonArray prelinked)
            {
                foreach (var pair in prelinked)
                {
                    if (pair is not JsonObject pairObj || pairObj["rightId"]?.GetValue<string>() is not { } rightId
                        || !idMap.TryGetValue(rightId, out var mapped))
                        return (plan, solution);
                    pairObj["rightId"] = mapped;
                }
            }

            string? newSolution = solution;
            if (!string.IsNullOrWhiteSpace(solution))
            {
                if (JsonNode.Parse(solution, documentOptions: DefinitionOptions) is not JsonObject solutionRoot)
                    return (plan, solution);
                foreach (var key in solutionRoot.Select(p => p.Key).ToList())
                {
                    if (solutionRoot[key]?.GetValue<string>() is not { } rightId || !idMap.TryGetValue(rightId, out var mapped))
                        return (plan, solution);
                    solutionRoot[key] = mapped;
                }
                newSolution = InteractionScramble.Write(solutionRoot);
            }

            return (InteractionScramble.Write(root), newSolution);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException or InvalidCastException)
        {
            return (plan, solution);
        }
    }

    private sealed record PlanInfo(
        List<string> Left, HashSet<string> LeftIds, HashSet<string> RightIds,
        HashSet<string> PrelinkedLeft, HashSet<string> PrelinkedRight);

    /// <summary>
    /// Plan okuma. <paramref name="strict"/> false (okuma/kontrol): id'ler ve örnek çiftler; bozuk öğe atlanır. true (kayıt):
    /// tam şema — tip, sürüm, sınırlar, id deseni, etiket/kutu, örnek çift tutarlılığı, her düzeyde izinli alanlar.
    /// </summary>
    private static bool TryReadPlan(string? plan, JsonDocumentOptions options, out PlanInfo info, bool strict = false)
    {
        info = new PlanInfo(new List<string>(), new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal), new HashSet<string>(StringComparer.Ordinal));
        if (string.IsNullOrWhiteSpace(plan))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(plan, options);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

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

            if (!root.TryGetProperty("left", out var left) || left.ValueKind != JsonValueKind.Array
                || !root.TryGetProperty("right", out var right) || right.ValueKind != JsonValueKind.Array)
                return false;

            var leftOrder = new List<string>();
            var leftIds = new HashSet<string>(StringComparer.Ordinal);
            if (!ReadItems(left, strict, leftIds, leftOrder))
                return false;
            var rightIds = new HashSet<string>(StringComparer.Ordinal);
            if (!ReadItems(right, strict, rightIds, null))
                return false;

            if (strict)
            {
                if (leftIds.Count is < MinItems or > MaxItems || rightIds.Count is < MinItems or > MaxItems)
                    return false;
                // Sol ve sağ id'leri ayrık (UI tek haritada tutabilir).
                if (leftIds.Overlaps(rightIds))
                    return false;
            }

            var prelinkedLeft = new HashSet<string>(StringComparer.Ordinal);
            var prelinkedRight = new HashSet<string>(StringComparer.Ordinal);
            if (root.TryGetProperty("prelinked", out var prelinked) && prelinked.ValueKind != JsonValueKind.Null)
            {
                if (prelinked.ValueKind != JsonValueKind.Array)
                {
                    if (strict) return false;
                }
                else
                {
                    foreach (var pair in prelinked.EnumerateArray())
                    {
                        if (!TryReadPair(pair, out var leftId, out var rightId) || (strict && !HasOnlyKeys(pair, PairKeys)))
                        {
                            if (strict) return false;
                            continue;
                        }
                        if (strict && (!leftIds.Contains(leftId) || !rightIds.Contains(rightId)))
                            return false;
                        var addedLeft = prelinkedLeft.Add(leftId);
                        var addedRight = prelinkedRight.Add(rightId);
                        if (strict && (!addedLeft || !addedRight))
                            return false;
                    }
                }
            }

            // Kayıtta en az bir puanlanacak sol (hepsi örnekse etkinlik yok).
            if (strict && leftIds.Count(l => !prelinkedLeft.Contains(l)) == 0)
                return false;

            info = new PlanInfo(leftOrder, leftIds, rightIds, prelinkedLeft, prelinkedRight);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool ReadItems(JsonElement items, bool strict, HashSet<string> ids, List<string>? order)
    {
        foreach (var item in items.EnumerateArray())
        {
            if (!TryReadItem(item, strict, out var id))
            {
                if (strict) return false;
                continue;
            }
            if (ids.Add(id))
                order?.Add(id);
            else if (strict)
                return false;
        }
        return true;
    }

    /// <summary>Öğe: nesne, string <c>id</c>; katıda id deseni, izinli alanlar, etiket ≤ 200, boş olmayan etiket ya da geçerli kutu.</summary>
    private static bool TryReadItem(JsonElement item, bool strict, out string id)
    {
        id = string.Empty;
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var idProp)
            || idProp.ValueKind != JsonValueKind.String)
            return false;
        id = idProp.GetString() ?? string.Empty;
        if (id.Length == 0)
            return false;
        if (!strict)
            return true;
        if (!IsValidId(id) || !HasOnlyKeys(item, ItemKeys))
            return false;

        var hasLabel = false;
        if (item.TryGetProperty("label", out var label) && label.ValueKind != JsonValueKind.Null)
        {
            if (label.ValueKind != JsonValueKind.String || (label.GetString()?.Length ?? 0) > MaxLabelLength)
                return false;
            hasLabel = !string.IsNullOrWhiteSpace(label.GetString());
        }
        var hasBox = item.TryGetProperty("box", out var box) && box.ValueKind != JsonValueKind.Null;
        if (hasBox && !IsValidBox(box))
            return false;
        return hasLabel || hasBox;
    }

    private static bool TryReadPair(JsonElement pair, out string leftId, out string rightId)
    {
        leftId = rightId = string.Empty;
        if (pair.ValueKind != JsonValueKind.Object
            || !pair.TryGetProperty("leftId", out var l) || l.ValueKind != JsonValueKind.String
            || !pair.TryGetProperty("rightId", out var r) || r.ValueKind != JsonValueKind.String)
            return false;
        leftId = l.GetString() ?? string.Empty;
        rightId = r.GetString() ?? string.Empty;
        return IsValidId(leftId) && IsValidId(rightId);
    }

    private static bool IsValidBox(JsonElement box) => InteractionBoxes.IsValid(box);

    /// <summary>
    /// Katı payload ayrıştırma: nesne, <c>type</c> = <see cref="Type"/>, varsa <c>version</c> = 1, <c>pairs</c> dizisi (yalnız
    /// <c>leftId</c>/<c>rightId</c> string alanlı nesneler, id deseni).
    /// </summary>
    private static bool TryParsePayload(string payload, out List<(string LeftId, string RightId)> pairs)
    {
        pairs = new List<(string, string)>();
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
            if (!root.TryGetProperty("pairs", out var list) || list.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var item in list.EnumerateArray())
            {
                if (!HasOnlyKeysSafe(item) || !TryReadPair(item, out var leftId, out var rightId))
                {
                    pairs.Clear();
                    return false;
                }
                pairs.Add((leftId, rightId));
            }

            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            pairs.Clear();
            return false;
        }
    }

    private static bool HasOnlyKeysSafe(JsonElement item) => item.ValueKind == JsonValueKind.Object && HasOnlyKeys(item, PairKeys);

    /// <summary>Çözüm: sol id → sağ id. Toleranslı: string olmayan değer atlanır; bozuk çözüm = boş (puanlanamaz).</summary>
    private static Dictionary<string, string> ReadSolution(string? solution)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(solution))
            return result;
        try
        {
            using var doc = JsonDocument.Parse(solution, ReadOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return result;

            foreach (var left in root.EnumerateObject())
            {
                if (left.Value.ValueKind == JsonValueKind.String && left.Value.GetString() is { Length: > 0 } rightId)
                    result[left.Name] = rightId;
            }

            return result;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
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
