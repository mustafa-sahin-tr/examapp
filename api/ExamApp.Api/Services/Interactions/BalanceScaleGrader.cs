using System.IO;
using System.Text;
using System.Text.Json;
using ExamApp.Api.Helpers;

namespace ExamApp.Api.Services.Interactions;

/// <summary>
/// issue #632: <c>balanceScale</c> kontrolcüsü — terazi dengeleme ("Aşağıdaki terazileri ağırlıkları kullanarak dengeye getiriniz",
/// Süper Combo Mat s.203). Karar #589'da.
/// <list type="bullet">
/// <item>Plan v1: <c>{version:1,type:"balanceScale",unit?,left?:[{id,value,label?}],right?:[...],cards:[{id,value,label?}],hideTilt?}</c>.
/// <c>left</c>/<c>right</c> kefelerdeki hazır (oynatılamaz) yükler (kefe başına 0..6), <c>cards</c> öğrencinin kefelere koyacağı
/// ağırlıklar (1..12). <c>value</c> tam sayı 1..10000 (kitapta ağırlığın üstünde yazar; öğrenciye gider), <c>label</c> isteğe bağlı
/// görünen metin (düz metin ≤ 30; yoksa değer + birim), <c>unit</c> düz metin ≤ 10. Bütün id'ler tekil. <c>hideTilt</c> yalnız görünüm:
/// terazi kontrol edilene dek düz durur (öğrenci hesapla bulsun).</item>
/// <item>Çözüm: <c>{"useAll":true}</c> — kural tabanlı: kefeler eşitse denge (birden çok geçerli yerleşim). <c>useAll</c> bütün kartlar
/// kefelerde olmalı; değilse en az bir kart yeter. Kayıtta kurala uyan en az bir yerleşim bulunmalı.</item>
/// <item>Öğrenci payload'u: <c>{version:1,type:"balanceScale",left:[kart id],right:[kart id]}</c> — kart en çok bir kefede. İki kefe de
/// boş = cevap yok.</item>
/// </list>
/// Kontrol tek satır (<c>balance</c>): en az bir kart konmuş, kefe toplamları eşit ve (<c>useAll</c>'da) kart kalmamış. Geri bildirim
/// <c>{"rows":[{id:"balance",correct}],"tilt":"level"|"left"|"right","unused"?}</c> — <c>tilt</c> ağır basan kefe (terazinin kendisi
/// zaten gösterir), <c>unused</c> yalnız <c>useAll</c>'da kefeye konmamış kart sayısı.
/// </summary>
public sealed class BalanceScaleGrader : IInteractionGrader
{
    public const string Type = "balanceScale";
    public const string RowId = "balance";

    public const int MaxValue = 10000;
    public const int MaxFixedPerPan = 6;
    public const int MaxCards = 12;
    public const int MaxLabelLength = 30;
    public const int MaxUnitLength = 10;

    private static readonly HashSet<string> RootKeys = new(StringComparer.Ordinal)
    {
        "version", "type", "unit", "left", "right", "cards", "hideTilt",
    };
    private static readonly HashSet<string> WeightKeys = new(StringComparer.Ordinal) { "id", "value", "label" };

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
        return TryReadPlan(plan, ReadOptions, strict: false, out var scale) && TryParsePayload(payload, scale, out _);
    }

    /// <summary>İki kefe de boş = cevap yok → null.</summary>
    public string? Normalize(string? payload)
    {
        if (payload == null || !TryParsePayload(payload, null, out var pans))
            return payload;
        return pans.Left.Count + pans.Right.Count == 0 ? null : payload;
    }

    public InteractionGradeResult Grade(InteractionGradeInput input)
    {
        if (!TryReadPlan(input.Plan, ReadOptions, strict: false, out var scale) || !TryReadSolution(input.Solution, strict: false, out var useAll))
            return InteractionGradeResult.Ungradable;
        var pans = Pans(scale, input.Payload);
        var (left, right) = Sums(scale, pans);
        var unused = scale.Cards.Count - pans.Left.Count - pans.Right.Count;
        var correct = IsBalanced(scale, pans, useAll);
        var tilt = left == right ? "level" : left > right ? "left" : "right";
        return InteractionGradeResult.Of(correct ? 1 : 0, 1, WriteFeedback(correct, tilt, useAll ? unused : null));
    }

    /// <summary>
    /// issue #584: tek öğe (terazi); cevap = yerleşim ("5 kg + 14 kg | 9 kg + 10 kg", kefe içinde plan sırası). Hazır yük yoksa iki kefe
    /// simetriktir: aynı yerleşimin aynası aynı cevap sayılır.
    /// </summary>
    public IReadOnlyList<InteractionItemResponse> Analyze(InteractionGradeInput input)
    {
        if (!TryReadPlan(input.Plan, ReadOptions, strict: false, out var scale) || !TryReadSolution(input.Solution, strict: false, out var useAll))
            return [];
        var pans = Pans(scale, input.Payload);
        if (pans.Left.Count + pans.Right.Count == 0)
            return [new InteractionItemResponse(RowId, "1", false, null, null)];
        var order = scale.Cards.Select((c, i) => (c.Id, i)).ToDictionary(x => x.Id, x => x.i, StringComparer.Ordinal);
        var labels = scale.Cards.ToDictionary(c => c.Id, c => c.Display(scale.Unit), StringComparer.Ordinal);
        string Side(IEnumerable<string> ids) => string.Join("+", ids.OrderBy(id => order[id]));
        string SideLabel(IEnumerable<string> ids) => ids.Any() ? string.Join(" + ", ids.OrderBy(id => order[id]).Select(id => labels[id])) : "—";
        var (first, second) = (pans.Left, pans.Right);
        if (scale.Left.Count == 0 && scale.Right.Count == 0 && string.CompareOrdinal(Side(first), Side(second)) > 0)
            (first, second) = (second, first);
        return
        [
            new InteractionItemResponse(RowId, "1", IsBalanced(scale, pans, useAll), $"{Side(first)}|{Side(second)}",
                InteractionAnalysis.TextLabel($"{SideLabel(first)} | {SideLabel(second)}")),
        ];
    }

    /// <summary>
    /// Kayıt kuralı (issue #632): değerler 1..10000, kefe başına en çok 6 hazır yük, 1..12 kart, id'ler tekil, metinler düz metin. Çözüm
    /// boşsa geçerli (taslak); doluysa <c>{"useAll":bool}</c> ve kurala uyan bir yerleşim var.
    /// </summary>
    public bool ValidateDefinition(string? plan, string? solution)
    {
        if (!TryReadPlan(plan, DefinitionOptions, strict: true, out var scale))
            return false;
        if (string.IsNullOrWhiteSpace(solution))
            return true;
        return TryReadSolution(solution, strict: true, out var useAll) && Solvable(scale, useAll);
    }

    // ---- Model ----

    private sealed record Weight(string Id, int Value, string? Label)
    {
        public string Display(string? unit) => Label ?? (unit == null ? Value.ToString(System.Globalization.CultureInfo.InvariantCulture)
            : $"{Value.ToString(System.Globalization.CultureInfo.InvariantCulture)} {unit}");
    }

    private sealed record Scale(string? Unit, List<Weight> Left, List<Weight> Right, List<Weight> Cards);

    private sealed record PanCards(List<string> Left, List<string> Right);

    private static (long Left, long Right) Sums(Scale scale, PanCards pans)
    {
        var values = scale.Cards.ToDictionary(c => c.Id, c => c.Value, StringComparer.Ordinal);
        return (scale.Left.Sum(w => (long)w.Value) + pans.Left.Sum(id => (long)values[id]),
            scale.Right.Sum(w => (long)w.Value) + pans.Right.Sum(id => (long)values[id]));
    }

    private static bool IsBalanced(Scale scale, PanCards pans, bool useAll)
    {
        var placed = pans.Left.Count + pans.Right.Count;
        if (placed == 0 || (useAll && placed < scale.Cards.Count))
            return false;
        var (left, right) = Sums(scale, pans);
        return left == right;
    }

    /// <summary>
    /// Kurala uyan bir yerleşim var mı: her kart sola (+), sağa (−) ya da (<c>useAll</c> değilse) dışarı; fark 0 ve en az bir kart konmuş.
    /// Ulaşılabilen farklar kümesi (12 kart × 10000 → en çok ~240 bin fark).
    /// </summary>
    private static bool Solvable(Scale scale, bool useAll)
    {
        var start = scale.Left.Sum(w => (long)w.Value) - scale.Right.Sum(w => (long)w.Value);
        var none = new HashSet<long> { start };
        var some = new HashSet<long>();
        foreach (var card in scale.Cards)
        {
            var nextSome = new HashSet<long>();
            foreach (var d in none.Concat(some))
            {
                nextSome.Add(d + card.Value);
                nextSome.Add(d - card.Value);
            }
            if (!useAll)
                nextSome.UnionWith(some);
            some = nextSome;
            if (useAll)
                none.Clear();
        }
        return some.Contains(0);
    }

    private static PanCards Pans(Scale scale, string? payload)
        => payload != null && TryParsePayload(payload, scale, out var pans) ? pans : new PanCards([], []);

    // ---- Okuma ----

    private static bool TryReadPlan(string? plan, JsonDocumentOptions options, bool strict, out Scale scale)
    {
        scale = new Scale(null, [], [], []);
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
                if (root.TryGetProperty("hideTilt", out var hide) && hide.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return false;
            }

            string? unit = null;
            if (root.TryGetProperty("unit", out var unitElement) && unitElement.ValueKind != JsonValueKind.Null)
            {
                if (unitElement.ValueKind != JsonValueKind.String)
                {
                    if (strict) return false;
                }
                else
                {
                    var text = unitElement.GetString()?.Trim() ?? string.Empty;
                    if (strict && (text.Length > MaxUnitLength || PlainTextAttribute.ContainsControlCharacters(text)))
                        return false;
                    unit = text.Length == 0 ? null : text;
                }
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (!TryReadWeights(root, "left", required: false, MaxFixedPerPan, ids, strict, out var left)
                || !TryReadWeights(root, "right", required: false, MaxFixedPerPan, ids, strict, out var right)
                || !TryReadWeights(root, "cards", required: true, MaxCards, ids, strict, out var cards)
                || cards.Count == 0)
                return false;
            scale = new Scale(unit, left, right, cards);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Ağırlık dizisi; katıda bozuk öğe planı reddeder, toleranslıda atlanır. Sınırı aşan dizi her iki modda da geçersiz.</summary>
    private static bool TryReadWeights(JsonElement root, string name, bool required, int max, HashSet<string> ids, bool strict,
        out List<Weight> weights)
    {
        weights = [];
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
            return !required;
        if (element.ValueKind != JsonValueKind.Array)
            return !strict && !required;
        foreach (var raw in element.EnumerateArray())
        {
            if (raw.ValueKind != JsonValueKind.Object || (strict && !HasOnlyKeys(raw, WeightKeys))
                || !raw.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.String
                || idElement.GetString() is not { } id || !InteractionIds.IsValid(id) || !ids.Add(id)
                || !raw.TryGetProperty("value", out var valueElement) || valueElement.ValueKind != JsonValueKind.Number
                || !valueElement.TryGetInt32(out var value) || value is < 1 or > MaxValue)
            {
                if (strict) return false;
                continue;
            }
            string? label = null;
            if (raw.TryGetProperty("label", out var labelElement) && labelElement.ValueKind != JsonValueKind.Null)
            {
                var text = labelElement.ValueKind == JsonValueKind.String ? labelElement.GetString()?.Trim() ?? string.Empty : null;
                if (text == null || (strict && (text.Length > MaxLabelLength || PlainTextAttribute.ContainsControlCharacters(text))))
                {
                    if (strict) return false;
                    text = null;
                }
                label = string.IsNullOrEmpty(text) ? null : text;
            }
            weights.Add(new Weight(id, value, label));
        }
        return weights.Count <= max;
    }

    private static bool TryReadSolution(string? solution, bool strict, out bool useAll)
    {
        useAll = false;
        if (string.IsNullOrWhiteSpace(solution))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(solution, strict ? DefinitionOptions : ReadOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || (strict && !HasOnlyKeys(root, ["useAll"])))
                return false;
            if (root.TryGetProperty("useAll", out var element) && element.ValueKind != JsonValueKind.Null)
            {
                if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return false;
                useAll = element.GetBoolean();
            }
            else if (strict)
            {
                return false;
            }
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
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
    /// Katı payload: <c>left</c> ve <c>right</c> kart id dizileri, bir kart en çok bir kez. Plan verilmişse id'ler planın kartları;
    /// verilmezse (Normalize) yalnız biçim, toplam en çok 12.
    /// </summary>
    private static bool TryParsePayload(string payload, Scale? scale, out PanCards pans)
    {
        pans = new PanCards([], []);
        try
        {
            using var doc = JsonDocument.Parse(payload, StrictOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasOnlyKeys(root, ["version", "type", "left", "right"]))
                return false;
            if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                || !string.Equals(type.GetString(), Type, StringComparison.Ordinal))
                return false;
            if (root.TryGetProperty("version", out var version)
                && (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1))
                return false;
            var cardIds = scale?.Cards.Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var result = new PanCards([], []);
            foreach (var (name, target) in new[] { ("left", result.Left), ("right", result.Right) })
            {
                if (!root.TryGetProperty(name, out var element) || element.ValueKind != JsonValueKind.Array)
                    return false;
                foreach (var raw in element.EnumerateArray())
                {
                    if (raw.ValueKind != JsonValueKind.String || raw.GetString() is not { } id || !InteractionIds.IsValid(id)
                        || (cardIds != null && !cardIds.Contains(id)) || !seen.Add(id) || seen.Count > MaxCards)
                        return false;
                    target.Add(id);
                }
            }
            pans = result;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static string WriteFeedback(bool correct, string tilt, int? unused)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("rows");
            writer.WriteStartObject();
            writer.WriteString("id", RowId);
            writer.WriteBoolean("correct", correct);
            writer.WriteEndObject();
            writer.WriteEndArray();
            writer.WriteString("tilt", tilt);
            if (unused is { } n)
                writer.WriteNumber("unused", n);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
