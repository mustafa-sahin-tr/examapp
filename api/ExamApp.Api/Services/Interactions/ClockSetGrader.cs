using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using ExamApp.Api.Helpers;

namespace ExamApp.Api.Services.Interactions;

/// <summary>
/// issue #634: <c>clockSet</c> kontrolcüsü — analog saat kurma ("Saat 3.45'i gösteriniz", "Ders 40 dakika sonra bitiyor; bitiş saatini
/// gösteriniz"). Saati okuma ve süre hesabı <c>slotFill</c> ile kalır.
/// <list type="bullet">
/// <item>Plan v1: <c>{version:1,type:"clockSet",step?,numbers?,minuteTicks?,clocks:[{id,label?,fixed?}]}</c>. <c>step</c> yelkovan adımı
/// (1 | 5 | 15 dk, varsayılan 5), <c>numbers</c> / <c>minuteTicks</c> yalnız görünüm (varsayılan açık). 1..6 saat; <c>label</c> isteğe
/// bağlı düz metin ≤ 40 ("Başlangıç"), <c>fixed</c> önceden kurulmuş, oynatılamaz ve kontrol edilmeyen saat (<c>"HH:MM"</c>). En az bir
/// saat serbest.</item>
/// <item>Çözüm: <c>{"times":{"c1":"03:45"}}</c> — her serbest saate <c>"HH:MM"</c> (0..23 : 0..59, dakika adımın katı). 12 saatlik kadran:
/// 15:45 ile 03:45 aynı konum.</item>
/// <item>Öğrenci payload'u: <c>{version:1,type:"clockSet",clocks:{"c1":{"h":225,"m":45}}}</c> — <c>h</c> akrebin konumu 12 saatlik
/// kadranda dakika cinsinden (0..719; 3.45'te 225), <c>m</c> yelkovan (0..59). Akrep yelkovana bağlı değil (karar #634: akrebin yeri
/// öğretilen kazanım). Hiç kurulmamış saat yazılmaz; saatsiz payload = cevap yok.</item>
/// </list>
/// Kontrol saat başına: yelkovan hedef dakikada ve akrep hedef konumun ±<see cref="HourTolerance"/> birim (±7,5°, saat aralığının
/// çeyreği) içinde. Geri bildirim <c>{"rows":[{id,correct}]}</c> (hedef yazılmaz).
/// </summary>
public sealed class ClockSetGrader : IInteractionGrader
{
    public const string Type = "clockSet";

    public const int MaxClocks = 6;
    public const int MaxLabelLength = 40;
    public const int DialUnits = 720;
    /// <summary>Akrebin kabul aralığı (kadran birimi; 720 = 360°): ±15 = ±7,5° = saat aralığının çeyreği.</summary>
    public const int HourTolerance = 15;

    private static readonly int[] Steps = [1, 5, 15];

    private static readonly HashSet<string> RootKeys = new(StringComparer.Ordinal)
    {
        "version", "type", "step", "numbers", "minuteTicks", "clocks",
    };
    private static readonly HashSet<string> ClockKeys = new(StringComparer.Ordinal) { "id", "label", "fixed" };

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
        return TryReadPlan(plan, ReadOptions, strict: false, out var dial) && TryParsePayload(payload, dial, out _);
    }

    /// <summary>Saatsiz payload = cevap yok → null.</summary>
    public string? Normalize(string? payload)
    {
        if (payload == null || !TryParsePayload(payload, null, out var hands))
            return payload;
        return hands.Count == 0 ? null : payload;
    }

    public InteractionGradeResult Grade(InteractionGradeInput input)
    {
        if (!TryReadPlan(input.Plan, ReadOptions, strict: false, out var dial)
            || !TryReadSolution(input.Solution, dial, strict: false, out var targets))
            return InteractionGradeResult.Ungradable;
        var hands = Hands(dial, input.Payload);
        var rows = new List<(string Id, bool Correct)>();
        foreach (var clock in dial.Clocks.Where(c => c.Fixed == null))
        {
            var correct = targets.TryGetValue(clock.Id, out var target) && hands.TryGetValue(clock.Id, out var hand) && Matches(hand, target);
            rows.Add((clock.Id, correct));
        }
        return InteractionGradeResult.Of(rows.Count(r => r.Correct), rows.Count, WriteFeedback(rows));
    }

    /// <summary>
    /// issue #584: öğe = serbest saat; cevap = gösterilen saat (<c>"3:45"</c>, saat akrebin yelkovana göre ait olduğu saat). Akrep bir saat
    /// konumunun kabul aralığında değilse saat kısmı <c>?</c> (<c>"?:45"</c>).
    /// </summary>
    public IReadOnlyList<InteractionItemResponse> Analyze(InteractionGradeInput input)
    {
        if (!TryReadPlan(input.Plan, ReadOptions, strict: false, out var dial)
            || !TryReadSolution(input.Solution, dial, strict: false, out var targets))
            return [];
        var hands = Hands(dial, input.Payload);
        var result = new List<InteractionItemResponse>();
        var index = 0;
        foreach (var clock in dial.Clocks)
        {
            index++;
            if (clock.Fixed != null)
                continue;
            var label = InteractionAnalysis.TextLabel(clock.Label ?? $"#{index}");
            if (!hands.TryGetValue(clock.Id, out var hand))
            {
                result.Add(new InteractionItemResponse(clock.Id, label, false, null, null));
                continue;
            }
            var key = Display(hand);
            result.Add(new InteractionItemResponse(clock.Id, label,
                targets.TryGetValue(clock.Id, out var target) && Matches(hand, target), key, key));
        }
        return result;
    }

    /// <summary>
    /// Kayıt kuralı (issue #634): adım 1 | 5 | 15, 1..6 saat, id'ler tekil, en az bir serbest saat, sabit saat <c>"HH:MM"</c>. Çözüm boşsa
    /// geçerli (taslak); doluysa her serbest saate (yalnız onlara) adımın katı bir saat.
    /// </summary>
    public bool ValidateDefinition(string? plan, string? solution)
    {
        if (!TryReadPlan(plan, DefinitionOptions, strict: true, out var dial))
            return false;
        if (string.IsNullOrWhiteSpace(solution))
            return true;
        return TryReadSolution(solution, dial, strict: true, out _);
    }

    // ---- Model ----

    /// <summary>Saat: 12 saatlik kadranda dakika (0..719).</summary>
    private sealed record Clock(string Id, string? Label, int? Fixed);

    private sealed record Dial(int Step, List<Clock> Clocks);

    private readonly record struct Hand(int Hour, int Minute);

    private static bool Matches(Hand hand, int target)
    {
        if (hand.Minute != target % 60)
            return false;
        var diff = Math.Abs(hand.Hour - target) % DialUnits;
        return Math.Min(diff, DialUnits - diff) <= HourTolerance;
    }

    /// <summary>
    /// Gösterilen saat <c>"3:45"</c>: saat, yelkovana göre akrebin en yakın olduğu tam konumdan (12 → 12). Akrep o konumun kabul
    /// aralığında değilse saat kısmı <c>?</c>.
    /// </summary>
    private static string Display(Hand hand)
    {
        var hour = (int)Math.Round((hand.Hour - hand.Minute) / 60.0, MidpointRounding.AwayFromZero);
        var exact = ((hour * 60 + hand.Minute) % DialUnits + DialUnits) % DialUnits;
        var diff = Math.Abs(hand.Hour - exact) % DialUnits;
        var ok = Math.Min(diff, DialUnits - diff) <= HourTolerance;
        var shown = ((hour % 12) + 12) % 12;
        var hourText = ok ? (shown == 0 ? 12 : shown).ToString(CultureInfo.InvariantCulture) : "?";
        return $"{hourText}:{hand.Minute.ToString("00", CultureInfo.InvariantCulture)}";
    }

    private static Dictionary<string, Hand> Hands(Dial dial, string? payload)
        => payload != null && TryParsePayload(payload, dial, out var hands) ? hands : new Dictionary<string, Hand>(StringComparer.Ordinal);

    /// <summary><c>"HH:MM"</c> (0..23 : 0..59) → 12 saatlik kadranda dakika.</summary>
    internal static bool TryParseTime(string? text, out int dialMinutes)
    {
        dialMinutes = 0;
        if (text is not { Length: 5 } || text[2] != ':'
            || !int.TryParse(text.AsSpan(0, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var h)
            || !int.TryParse(text.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m)
            || h > 23 || m > 59)
            return false;
        dialMinutes = (h % 12) * 60 + m;
        return true;
    }

    // ---- Okuma ----

    private static bool TryReadPlan(string? plan, JsonDocumentOptions options, bool strict, out Dial dial)
    {
        dial = new Dial(5, []);
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
                foreach (var flag in new[] { "numbers", "minuteTicks" })
                {
                    if (root.TryGetProperty(flag, out var element) && element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        return false;
                }
            }

            var step = 5;
            if (root.TryGetProperty("step", out var stepElement) && stepElement.ValueKind != JsonValueKind.Null)
            {
                if (stepElement.ValueKind != JsonValueKind.Number || !stepElement.TryGetInt32(out step) || !Steps.Contains(step))
                {
                    if (strict) return false;
                    step = 5;
                }
            }

            if (!root.TryGetProperty("clocks", out var clocksElement) || clocksElement.ValueKind != JsonValueKind.Array)
                return false;
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var clocks = new List<Clock>();
            foreach (var raw in clocksElement.EnumerateArray())
            {
                if (raw.ValueKind != JsonValueKind.Object || (strict && !HasOnlyKeys(raw, ClockKeys))
                    || !raw.TryGetProperty("id", out var idElement) || idElement.ValueKind != JsonValueKind.String
                    || idElement.GetString() is not { } id || !InteractionIds.IsValid(id) || !ids.Add(id))
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
                int? fixedTime = null;
                if (raw.TryGetProperty("fixed", out var fixedElement) && fixedElement.ValueKind != JsonValueKind.Null)
                {
                    if (fixedElement.ValueKind != JsonValueKind.String || !TryParseTime(fixedElement.GetString(), out var minutes))
                    {
                        if (strict) return false;
                        continue;
                    }
                    fixedTime = minutes;
                }
                clocks.Add(new Clock(id, label, fixedTime));
            }
            if (clocks.Count is 0 or > MaxClocks || clocks.All(c => c.Fixed != null))
                return false;
            dial = new Dial(step, clocks);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Hedefler: serbest saat id → kadran dakikası. Katıda her serbest saat (yalnız onlar) adımın katı bir saat taşır.</summary>
    private static bool TryReadSolution(string? solution, Dial dial, bool strict, out Dictionary<string, int> targets)
    {
        targets = new Dictionary<string, int>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(solution))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(solution, strict ? DefinitionOptions : ReadOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || (strict && !HasOnlyKeys(root, ["times"]))
                || !root.TryGetProperty("times", out var times) || times.ValueKind != JsonValueKind.Object)
                return false;
            var free = dial.Clocks.Where(c => c.Fixed == null).Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var property in times.EnumerateObject())
            {
                if (!free.Contains(property.Name) || property.Value.ValueKind != JsonValueKind.String
                    || !TryParseTime(property.Value.GetString(), out var minutes) || (strict && minutes % 60 % dial.Step != 0))
                {
                    if (strict) return false;
                    continue;
                }
                targets[property.Name] = minutes;
            }
            return !strict || targets.Count == free.Count;
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
    /// Katı payload: <c>clocks</c> nesnesi, anahtar saat id'si, değer <c>{h:0..719, m:0..59}</c>. Plan verilmişse anahtarlar planın
    /// serbest saatleri ve yelkovan adımın katı; verilmezse (Normalize) yalnız biçim, en çok 6 saat.
    /// </summary>
    private static bool TryParsePayload(string payload, Dial? dial, out Dictionary<string, Hand> hands)
    {
        hands = new Dictionary<string, Hand>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(payload, StrictOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasOnlyKeys(root, ["version", "type", "clocks"]))
                return false;
            if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                || !string.Equals(type.GetString(), Type, StringComparison.Ordinal))
                return false;
            if (root.TryGetProperty("version", out var version)
                && (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1))
                return false;
            if (!root.TryGetProperty("clocks", out var clocks) || clocks.ValueKind != JsonValueKind.Object)
                return false;
            var free = dial?.Clocks.Where(c => c.Fixed == null).Select(c => c.Id).ToHashSet(StringComparer.Ordinal);
            var result = new Dictionary<string, Hand>(StringComparer.Ordinal);
            foreach (var property in clocks.EnumerateObject())
            {
                var value = property.Value;
                if (!InteractionIds.IsValid(property.Name) || (free != null && !free.Contains(property.Name))
                    || value.ValueKind != JsonValueKind.Object || !HasOnlyKeys(value, ["h", "m"])
                    || !value.TryGetProperty("h", out var h) || h.ValueKind != JsonValueKind.Number || !h.TryGetInt32(out var hour)
                    || hour is < 0 or >= DialUnits
                    || !value.TryGetProperty("m", out var m) || m.ValueKind != JsonValueKind.Number || !m.TryGetInt32(out var minute)
                    || minute is < 0 or > 59 || (dial != null && minute % dial.Step != 0)
                    || !result.TryAdd(property.Name, new Hand(hour, minute)) || result.Count > MaxClocks)
                    return false;
            }
            hands = result;
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static string WriteFeedback(List<(string Id, bool Correct)> rows)
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
