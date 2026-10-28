using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ExamApp.Api.Services.Interactions;

/// <summary>
/// issue #576: <c>dragPlace</c> kontrolcüsü — kart (metin ya da soru görselinden kırpma) → hedef sürükle-yerleştir:
/// görsel üstünde bölge, kova (çok kart), sıralama (konum = hedef), çeldirici kart, havuzdan tekrar kullanım.
/// Mevcut <c>dragDropLabeling</c> soruları DEĞİŞMEZ (kendi kontrolcüsüyle okunur; migration yok).
/// <list type="bullet">
/// <item>Plan v1: <c>{version:1,type:"dragPlace",mode:"place"|"order",reuse:false,targets:[{id,label?,box?,capacity}],
/// cards:[{id,label?,box?}]}</c>. Kartta <c>label</c> ya da <c>box</c> (soru görselinden kırpma) zorunlu; hedefte <c>box</c>
/// isteğe bağlı (kutusuz hedef = görselin altında etiketli kova). <c>mode</c> yoksa <c>place</c>, <c>reuse</c> yoksa false,
/// <c>capacity</c> yoksa 1. <c>order</c> modunda her hedef bir konumdur, kapasite 1 olmalı.
/// issue #590 (dilim b): <c>layout:{kind:"text",text}</c> = boşluklu metin + kelime havuzu — yalnız <c>place</c>, her hedef
/// kutusuz, kapasite 1 ve metinde tam bir kez <c>[[id]]</c>; kartlar kutusuz metin (havuz; çözümde olmayan = çeldirici).</item>
/// <item>Çözüm (#570, <c>Question.InteractionSolution</c>): <c>{"t1":["c1","c3"],"t2":[]}</c> — her hedef için 0..kapasite kart;
/// <c>reuse</c> kapalıysa bir kart en çok bir hedefte. Çözümde olmayan kart = çeldirici.</item>
/// <item>Öğrenci payload'u: <c>{version:1,type:"dragPlace",placements:[{targetId,cardId}]}</c>.</item>
/// </list>
/// Satırlar (geri bildirim, tasarım — review #576 M2): çözüm sırasıyla her hedef için yerleştirilmiş doğru kartlar <c>"t:c"</c>
/// doğru; öğrencinin EN AZ BİR kart koyduğu ve beklenen kartı eksik olan hedefe tek bir <c>"t:"</c> yanlış satırı (hangi kartın
/// eksik olduğu YAZILMAZ); ardından çözümde olmayan her yerleşim <c>"t:c"</c> yanlış (payload sırası; UI yanlış kartı bununla
/// işaretler). Dokunulmamış hedef hiç satır üretmez — boş kalması gereken hedef ile hiç dokunulmamış hedef geri bildirimden
/// ayırt edilemez. <c>CorrectCount</c> = yerleştirilmiş beklenen çift, <c>TotalCount</c> = beklenen çift (dokunulmamış hedefin
/// beklenen kartları da sayılır); doğru ⇔ hepsi yerinde ve fazladan yanlış yerleşim yok. Çözümü olmayan soru puanlanamaz (0/0).
/// <para>
/// Sıralama modu: her konum kapasite 1, çözümde her konumun tam bir kartı, tekrar kullanım yok. Kayıtta
/// (<see cref="PrepareForStorage"/>) kartlar rastgele sıraya dizilir ve opak id alır (review #576 H1/M1); hedefler yazarlık
/// sırasında kalır. Kutular <see cref="InteractionBoxes"/> sınırına uyar.
/// </para>
/// </summary>
public sealed class DragPlaceGrader : IInteractionGrader
{
    public const string Type = "dragPlace";

    public const string ModePlace = "place";
    public const string ModeOrder = "order";

    public const int MinTargets = 1;
    public const int MaxTargets = 30;
    public const int MinCards = 1;
    public const int MaxCards = 60;
    public const int MinCapacity = 1;
    public const int MaxCapacity = 30;
    public const int MaxLabelLength = 200;

    /// <summary>Geri bildirim satır id'sinde hedef ile kart arasındaki ayraç (id deseninde yok).</summary>
    public const char RowSeparator = ':';

    private static readonly HashSet<string> RootKeys = new(StringComparer.Ordinal) { "version", "type", "mode", "reuse", "targets", "cards", "layout" };
    private static readonly HashSet<string> TargetKeys = new(StringComparer.Ordinal) { "id", "label", "box", "capacity" };
    private static readonly HashSet<string> CardKeys = new(StringComparer.Ordinal) { "id", "label", "box" };
    private static readonly HashSet<string> PlacementKeys = new(StringComparer.Ordinal) { "targetId", "cardId" };

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
    public DragPlaceGrader(IInteractionRandom? random = null)
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
        if (!TryParsePayload(payload, out var placements))
            return false;
        if (placements.Count == 0)
            return true;
        if (!TryReadPlan(plan, ReadOptions, out var planInfo))
            return false;

        var perTarget = new Dictionary<string, int>(StringComparer.Ordinal);
        var usedCards = new HashSet<string>(StringComparer.Ordinal);
        var pairs = new HashSet<(string, string)>();
        foreach (var (targetId, cardId) in placements)
        {
            if (!planInfo.Capacity.TryGetValue(targetId, out var capacity) || !planInfo.CardIds.Contains(cardId))
                return false;
            if (!pairs.Add((targetId, cardId)))
                return false;
            if (!planInfo.Reuse && !usedCards.Add(cardId))
                return false;
            perTarget[targetId] = perTarget.GetValueOrDefault(targetId) + 1;
            if (perTarget[targetId] > capacity)
                return false;
        }

        return true;
    }

    /// <summary>Yerleşimsiz payload (<c>placements: []</c>) = cevap yok → null.</summary>
    public string? Normalize(string? payload)
        => payload != null && TryParsePayload(payload, out var placements) && placements.Count == 0 ? null : payload;

    public InteractionGradeResult Grade(InteractionGradeInput input)
    {
        if (!TryReadPlan(input.Plan, ReadOptions, out var plan) || plan.Targets.Count == 0)
            return InteractionGradeResult.Ungradable;
        var expected = ReadSolution(input.Solution, plan);
        var total = expected.Sum(e => e.Cards.Count);
        if (total == 0)
            return InteractionGradeResult.Ungradable;

        var placed = new List<(string TargetId, string CardId)>();
        var placedSet = new HashSet<(string, string)>();
        if (input.Payload != null && TryParsePayload(input.Payload, out var placements))
        {
            foreach (var pair in placements)
            {
                if (placedSet.Add(pair))
                    placed.Add(pair);
            }
        }

        var touched = placed.Select(p => p.TargetId).ToHashSet(StringComparer.Ordinal);
        var rows = new List<(string Id, bool Correct)>();
        var expectedSet = new HashSet<(string, string)>();
        var correct = 0;
        foreach (var (targetId, cards) in expected)
        {
            var missing = false;
            foreach (var cardId in cards)
            {
                expectedSet.Add((targetId, cardId));
                if (placedSet.Contains((targetId, cardId)))
                {
                    correct++;
                    rows.Add((RowId(targetId, cardId), true));
                }
                else
                {
                    missing = true;
                }
            }
            // Eksik kart(lar) hedef başına tek satır, YALNIZ öğrencinin kart koyduğu hedefte; kart id'si yazılmaz (çözüm sızmaz).
            // Dokunulmamış hedef satır üretmez: boş kalması gereken hedef ile dokunulmamış hedef ayırt edilemez (review #576 M2).
            if (missing && touched.Contains(targetId))
                rows.Add((RowId(targetId, string.Empty), false));
        }

        var wrongExtras = 0;
        foreach (var (targetId, cardId) in placed)
        {
            if (expectedSet.Contains((targetId, cardId)))
                continue;
            wrongExtras++;
            rows.Add((RowId(targetId, cardId), false));
        }

        var isCorrect = correct == total && wrongExtras == 0;
        return new InteractionGradeResult(isCorrect, correct, total, WriteFeedback(rows));
    }

    /// <summary>
    /// issue #584: öğe = kart (plan sırası; "öğrencilerin %40'ı kemiği kaslara koydu"), cevap = kartın konduğu hedef(ler) (plan
    /// hedef sırasıyla). Kart doğru ⇔ konduğu hedef kümesi == çözümdeki hedef kümesi. Çözümde yeri olmayan kart çeldiricidir:
    /// doğru ⇔ hiçbir yere konmamış. Sıralama modunda hedef = konum.
    /// </summary>
    public IReadOnlyList<InteractionItemResponse> Analyze(InteractionGradeInput input)
    {
        if (!TryReadPlan(input.Plan, ReadOptions, out var plan) || plan.Targets.Count == 0)
            return [];
        var expected = ReadSolution(input.Solution, plan);
        if (expected.Sum(e => e.Cards.Count) == 0)
            return [];

        var expectedTargets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (targetId, cards) in expected)
        {
            foreach (var cardId in cards)
            {
                if (!expectedTargets.TryGetValue(cardId, out var set))
                    expectedTargets[cardId] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(targetId);
            }
        }

        var placedTargets = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        if (input.Payload != null && TryParsePayload(input.Payload, out var placements))
        {
            foreach (var (targetId, cardId) in placements)
            {
                if (!placedTargets.TryGetValue(cardId, out var set))
                    placedTargets[cardId] = set = new HashSet<string>(StringComparer.Ordinal);
                set.Add(targetId);
            }
        }

        var targets = InteractionAnalysis.ReadEntries(input.Plan, "targets");
        var items = new List<InteractionItemResponse>();
        foreach (var (cardId, cardLabel) in InteractionAnalysis.ReadEntries(input.Plan, "cards"))
        {
            if (!plan.CardIds.Contains(cardId))
                continue;
            var placed = placedTargets.GetValueOrDefault(cardId);
            var chosen = placed == null ? [] : targets.Where(t => placed.Contains(t.Id)).ToList();
            var distractor = !expectedTargets.TryGetValue(cardId, out var want);
            var correct = distractor ? chosen.Count == 0 : placed != null && placed.SetEquals(want!);
            items.Add(new InteractionItemResponse(
                cardId,
                cardLabel,
                correct,
                chosen.Count == 0 ? null : string.Join(',', chosen.Select(t => t.Id)),
                chosen.Count == 0 ? null : string.Join(", ", chosen.Select(t => t.Label)),
                distractor));
        }
        return items;
    }

    /// <summary>
    /// Kayıt kuralı (issue #576): plan zorunlu ve şemaya uygun; çözüm boşsa geçerli (taslak, puanlanamaz), doluysa plandaki
    /// her hedef için tam bir anahtar (0..kapasite plan kartı, yinelenen kart yok), <c>reuse</c> kapalıysa kart en çok bir
    /// hedefte, en az bir beklenen yerleşim.
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
            var usedCards = new HashSet<string>(StringComparer.Ordinal);
            var pairCount = 0;
            foreach (var property in root.EnumerateObject())
            {
                if (!IsValidId(property.Name) || !planInfo.Capacity.TryGetValue(property.Name, out var capacity)
                    || !covered.Add(property.Name))
                    return false;
                if (property.Value.ValueKind != JsonValueKind.Array)
                    return false;
                var cards = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in property.Value.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String)
                        return false;
                    var cardId = item.GetString() ?? string.Empty;
                    if (!IsValidId(cardId) || !planInfo.CardIds.Contains(cardId) || !cards.Add(cardId))
                        return false;
                    if (!planInfo.Reuse && !usedCards.Add(cardId))
                        return false;
                }
                if (cards.Count > capacity)
                    return false;
                // review #576: sıralamada her konumun tam bir kartı var.
                if (planInfo.Order && cards.Count != 1)
                    return false;
                pairCount += cards.Count;
            }

            return covered.Count == planInfo.Capacity.Count && pairCount > 0;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// review #576 (H1/M1): kartları kriptografik rastgele sıraya dizer, her karta hedef id'lerinden ve birbirinden farklı 8
    /// karakterlik opak id verir ve çözümü yeni id'lere eşler. Hedefler (görsel / konum sırası) değişmez. Okunamayan tanım aynen döner.
    /// </summary>
    public (string? Plan, string? Solution) PrepareForStorage(string? plan, string? solution)
    {
        if (string.IsNullOrWhiteSpace(plan))
            return (plan, solution);
        try
        {
            if (JsonNode.Parse(plan, documentOptions: DefinitionOptions) is not JsonObject root
                || root["cards"] is not JsonArray cards || root["targets"] is not JsonArray targets)
                return (plan, solution);

            var taken = new HashSet<string>(StringComparer.Ordinal);
            foreach (var target in targets)
            {
                if (target?["id"]?.GetValue<string>() is { } targetId)
                    taken.Add(targetId);
            }

            var items = cards.Select(c => c!.DeepClone()).ToList();
            InteractionScramble.Shuffle(items, _random);
            var idMap = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var item in items)
            {
                var card = (JsonObject)item!;
                var oldId = card["id"]!.GetValue<string>();
                var newId = InteractionScramble.NewOpaqueId(taken, _random);
                idMap[oldId] = newId;
                card["id"] = newId;
            }
            cards.Clear();
            foreach (var item in items)
                cards.Add(item);

            string? newSolution = solution;
            if (!string.IsNullOrWhiteSpace(solution))
            {
                if (JsonNode.Parse(solution, documentOptions: DefinitionOptions) is not JsonObject solutionRoot)
                    return (plan, solution);
                foreach (var (_, value) in solutionRoot)
                {
                    if (value is not JsonArray list)
                        return (plan, solution);
                    for (var i = 0; i < list.Count; i++)
                    {
                        if (list[i]?.GetValue<string>() is not { } cardId || !idMap.TryGetValue(cardId, out var mapped))
                            return (plan, solution);
                        list[i] = mapped;
                    }
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

    private static string RowId(string targetId, string cardId) => targetId + RowSeparator + cardId;

    private sealed record PlanInfo(List<string> Targets, Dictionary<string, int> Capacity, HashSet<string> CardIds, bool Reuse, bool Order);

    /// <summary>
    /// Plan okuma. <paramref name="strict"/> false (okuma/kontrol): id'ler, kapasite (geçersizse 1) ve <c>reuse</c>; bozuk öğe
    /// atlanır. true (kayıt): tam şema — tip, sürüm, mod, sınırlar, id deseni, etiket/kutu, her düzeyde izinli alanlar.
    /// </summary>
    private static bool TryReadPlan(string? plan, JsonDocumentOptions options, out PlanInfo info, bool strict = false)
    {
        info = new PlanInfo(new List<string>(), new Dictionary<string, int>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal), false, false);
        if (string.IsNullOrWhiteSpace(plan))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(plan, options);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            var reuse = false;
            if (root.TryGetProperty("reuse", out var reuseProp))
            {
                if (reuseProp.ValueKind is JsonValueKind.True or JsonValueKind.False)
                    reuse = reuseProp.GetBoolean();
                else if (strict && reuseProp.ValueKind != JsonValueKind.Null)
                    return false;
            }

            var mode = ModePlace;
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
                if (root.TryGetProperty("mode", out var modeProp) && modeProp.ValueKind != JsonValueKind.Null)
                {
                    if (modeProp.ValueKind != JsonValueKind.String)
                        return false;
                    mode = modeProp.GetString() ?? string.Empty;
                    if (mode != ModePlace && mode != ModeOrder)
                        return false;
                }
                // review #576: sıralamada bir kart tek konuma (tekrar kullanım yok).
                if (mode == ModeOrder && reuse)
                    return false;
            }

            if (!root.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Array
                || !root.TryGetProperty("cards", out var cards) || cards.ValueKind != JsonValueKind.Array)
                return false;

            var targetOrder = new List<string>();
            var capacity = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var target in targets.EnumerateArray())
            {
                if (!TryReadEntry(target, strict, TargetKeys, out var id))
                {
                    if (strict) return false;
                    continue;
                }
                if (!TryReadCapacity(target, strict, out var cap))
                    return false;
                if (strict)
                {
                    if (mode == ModeOrder && cap != 1)
                        return false;
                    if (target.TryGetProperty("box", out var box) && box.ValueKind != JsonValueKind.Null && !IsValidBox(box))
                        return false;
                }
                if (capacity.TryAdd(id, cap))
                    targetOrder.Add(id);
                else if (strict)
                    return false;
            }

            var cardIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var card in cards.EnumerateArray())
            {
                if (!TryReadEntry(card, strict, CardKeys, out var id))
                {
                    if (strict) return false;
                    continue;
                }
                if (strict && !IsValidCardFace(card))
                    return false;
                if (!cardIds.Add(id) && strict)
                    return false;
                // Hedef ve kart id'leri ayrık (UI tek haritada tutabilir; satır id'si belirsiz kalmasın).
                if (strict && capacity.ContainsKey(id))
                    return false;
            }

            if (strict && (capacity.Count is < MinTargets or > MaxTargets || cardIds.Count is < MinCards or > MaxCards))
                return false;
            if (strict && root.TryGetProperty("layout", out var layout) && layout.ValueKind != JsonValueKind.Null
                && !IsValidTextLayout(layout, mode, targets, cards, capacity, targetOrder))
                return false;

            info = new PlanInfo(targetOrder, capacity, cardIds, reuse, mode == ModeOrder);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static readonly string[] LayoutKinds = { InteractionLayout.KindText };

    /// <summary>
    /// issue #590: boşluklu metin düzeni — yalnız <c>place</c>; hedefler kutusuz, kapasite 1, metinde tam bir kez; kartlar kutusuz.
    /// </summary>
    private static bool IsValidTextLayout(JsonElement layout, string mode, JsonElement targets, JsonElement cards,
        Dictionary<string, int> capacity, List<string> targetOrder)
    {
        if (mode != ModePlace || capacity.Values.Any(c => c != 1))
            return false;
        static bool HasBox(JsonElement e) => e.TryGetProperty("box", out var box) && box.ValueKind != JsonValueKind.Null;
        if (targets.EnumerateArray().Any(HasBox) || cards.EnumerateArray().Any(HasBox))
            return false;
        return InteractionLayout.TryValidate(layout, LayoutKinds, targetOrder, out _);
    }

    /// <summary>Hedef/kart öğesi: nesne, boş olmayan string <c>id</c> (katıda desen), varsa <c>label</c> string (katıda ≤ 200).</summary>
    private static bool TryReadEntry(JsonElement entry, bool strict, HashSet<string> allowedKeys, out string id)
    {
        id = string.Empty;
        if (entry.ValueKind != JsonValueKind.Object || !entry.TryGetProperty("id", out var idProp)
            || idProp.ValueKind != JsonValueKind.String)
            return false;
        id = idProp.GetString() ?? string.Empty;
        if (id.Length == 0 || (strict && !IsValidId(id)))
            return false;
        if (strict && !HasOnlyKeys(entry, allowedKeys))
            return false;
        if (strict && entry.TryGetProperty("label", out var label) && label.ValueKind != JsonValueKind.Null
            && (label.ValueKind != JsonValueKind.String || (label.GetString()?.Length ?? 0) > MaxLabelLength))
            return false;
        return true;
    }

    /// <summary>Kapasite: yoksa 1. Katıda tam sayı 1..30; toleranslı okumada geçersiz değer 1 sayılır.</summary>
    private static bool TryReadCapacity(JsonElement target, bool strict, out int capacity)
    {
        capacity = 1;
        if (!target.TryGetProperty("capacity", out var prop) || prop.ValueKind == JsonValueKind.Null)
            return true;
        if (prop.ValueKind == JsonValueKind.Number && prop.TryGetInt32(out var value) && value is >= MinCapacity and <= MaxCapacity)
        {
            capacity = value;
            return true;
        }
        return !strict;
    }

    /// <summary>Kart yüzü: boş olmayan etiket ya da geçerli kutu (kırpma); kutu verildiyse geçerli olmalı.</summary>
    private static bool IsValidCardFace(JsonElement card)
    {
        var hasBox = card.TryGetProperty("box", out var box) && box.ValueKind != JsonValueKind.Null;
        if (hasBox && !IsValidBox(box))
            return false;
        var hasLabel = card.TryGetProperty("label", out var label) && label.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(label.GetString());
        return hasBox || hasLabel;
    }

    private static bool IsValidBox(JsonElement box) => InteractionBoxes.IsValid(box);

    /// <summary>
    /// Katı payload ayrıştırma: nesne, <c>type</c> = <see cref="Type"/>, varsa <c>version</c> = 1, <c>placements</c> dizisi
    /// (yalnız <c>targetId</c>/<c>cardId</c> string alanlı nesneler, id deseni).
    /// </summary>
    private static bool TryParsePayload(string payload, out List<(string TargetId, string CardId)> placements)
    {
        placements = new List<(string, string)>();
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
            if (!root.TryGetProperty("placements", out var list) || list.ValueKind != JsonValueKind.Array)
                return false;

            foreach (var item in list.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object || !HasOnlyKeys(item, PlacementKeys)
                    || !item.TryGetProperty("targetId", out var t) || t.ValueKind != JsonValueKind.String
                    || !item.TryGetProperty("cardId", out var c) || c.ValueKind != JsonValueKind.String)
                {
                    placements.Clear();
                    return false;
                }
                var targetId = t.GetString() ?? string.Empty;
                var cardId = c.GetString() ?? string.Empty;
                if (!IsValidId(targetId) || !IsValidId(cardId))
                {
                    placements.Clear();
                    return false;
                }
                placements.Add((targetId, cardId));
            }

            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            placements.Clear();
            return false;
        }
    }

    /// <summary>
    /// Çözüm: hedef id → beklenen kartlar (çözüm sırası). Toleranslı: plan dışı hedef/kart ve yinelenenler atlanır; bozuk
    /// çözüm = boş (puanlanamaz).
    /// </summary>
    private static List<(string TargetId, List<string> Cards)> ReadSolution(string? solution, PlanInfo plan)
    {
        var result = new List<(string, List<string>)>();
        if (string.IsNullOrWhiteSpace(solution))
            return result;
        try
        {
            using var doc = JsonDocument.Parse(solution, ReadOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return result;

            var seenTargets = new HashSet<string>(StringComparer.Ordinal);
            foreach (var target in root.EnumerateObject())
            {
                if (target.Value.ValueKind != JsonValueKind.Array || !plan.Capacity.ContainsKey(target.Name)
                    || !seenTargets.Add(target.Name))
                    continue;
                var cards = new List<string>();
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in target.Value.EnumerateArray())
                {
                    if (item.ValueKind == JsonValueKind.String && item.GetString() is { Length: > 0 } cardId
                        && plan.CardIds.Contains(cardId) && seen.Add(cardId))
                        cards.Add(cardId);
                }
                result.Add((target.Name, cards));
            }

            return result;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return new List<(string, List<string>)>();
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
