using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace ExamApp.Api.Services.Interactions;

/// <summary>
/// issue #575: <c>slotFill</c> kontrolcüsü — görselin üstüne konan cevap kutuları (kısa sayısal/yazılı cevap, boşluk, tablo,
/// etiketleme). Kutu başına doğru/yanlış.
/// <list type="bullet">
/// <item>Plan v1: <c>{version:1,type:"slotFill",slots:[{id,label?,box,input}]}</c>; <c>box</c> soru bölgesine göre göreli.
/// <c>input</c>: <c>{kind:"number",entry:"plain"|"fraction"|"mixed",unit?}</c> | <c>{kind:"text"}</c> |
/// <c>{kind:"select",options:[{id,label}]}</c> (2..12) | <c>{kind:"palette",symbols:[...]}</c> (1..20, her biri ≤ 8).
/// issue #590 (dilim b, görselsiz): <c>layout</c> (<see cref="InteractionLayout"/>: <c>list</c> | <c>text</c> | <c>table</c>)
/// verilirse kutularda <c>box</c> YOKTUR; <c>list</c>'te her kutunun boş olmayan <c>label</c>'ı (soru) zorunlu, <c>text</c> /
/// <c>table</c>'da her kutu düzende tam bir kez <c>[[id]]</c> olarak geçer. <c>layout</c> yoksa <c>box</c> zorunlu.</item>
/// <item>Çözüm (#570, <c>Question.InteractionSolution</c>, öğrenciye gitmez): kutu id → kutu çözümü — number
/// <c>{value,equivalence?,requireSimplest?,units?,unitMode?}</c> (varsayılan <c>true</c>, <c>false</c>, <c>[]</c>,
/// <c>"none"</c>), text <c>{accept,optionalSuffixes?}</c>, select <c>{optionIds}</c>, palette <c>{accept}</c>.</item>
/// <item>Öğrenci payload'u: <c>{version:1,type:"slotFill",values:{"s1":"3/4"}}</c> (string; boş/boşluk = cevapsız; select
/// seçenek id'si gönderir).</item>
/// </list>
/// Karşılaştırma kuralları <see cref="SlotAnswerNormalizer"/>'da (paylaşılan fixture). Satırlar = plandaki TÜM kutular, plan
/// sırasıyla (cevapsız kutu yanlış); geri bildirim yalnız kutu id'si + doğru/yanlış, çözüm ASLA yazılmaz. Çözümü olmayan soru
/// puanlanamaz (0/0).
/// </summary>
public sealed class SlotFillGrader : IInteractionGrader
{
    public const string Type = "slotFill";

    public const string KindNumber = "number";
    public const string KindText = "text";
    public const string KindSelect = "select";
    public const string KindPalette = "palette";

    public const int MinSlots = 1;
    public const int MaxSlots = 80;
    public const int MaxLabelLength = 200;
    public const int MinOptions = 2;
    public const int MaxOptions = 12;
    public const int MinSymbols = 1;
    public const int MaxSymbols = 20;
    public const int MaxSymbolLength = 8;
    public const int MaxUnitHintLength = 20;
    public const int MaxAcceptEntries = 20;
    public const int MaxAcceptLength = 100;
    public const int MaxSuffixEntries = 10;
    public const int MaxUnits = 10;
    public const int MaxUnitLength = 20;

    /// <summary>Öğrencinin tek kutuya yazabileceği en uzun değer (karakter).</summary>
    public const int MaxValueLength = 200;


    // Kayıtta planın / çözümün her düzeyinde izinli alanlar (allowlist; #574 M1 ile aynı gerekçe).
    private static readonly HashSet<string> RootKeys = new(StringComparer.Ordinal) { "version", "type", "slots", "layout" };
    private static readonly HashSet<string> SlotKeys = new(StringComparer.Ordinal) { "id", "label", "box", "input" };
    private static readonly HashSet<string> BoxKeys = new(StringComparer.Ordinal) { "x", "y", "width", "height" };
    private static readonly HashSet<string> NumberInputKeys = new(StringComparer.Ordinal) { "kind", "entry", "unit" };
    private static readonly HashSet<string> TextInputKeys = new(StringComparer.Ordinal) { "kind" };
    private static readonly HashSet<string> SelectInputKeys = new(StringComparer.Ordinal) { "kind", "options" };
    private static readonly HashSet<string> PaletteInputKeys = new(StringComparer.Ordinal) { "kind", "symbols" };
    private static readonly HashSet<string> OptionKeys = new(StringComparer.Ordinal) { "id", "label" };
    private static readonly HashSet<string> NumberSolutionKeys = new(StringComparer.Ordinal)
        { "value", "equivalence", "requireSimplest", "units", "unitMode" };
    private static readonly HashSet<string> TextSolutionKeys = new(StringComparer.Ordinal) { "accept", "optionalSuffixes" };
    private static readonly HashSet<string> SelectSolutionKeys = new(StringComparer.Ordinal) { "optionIds" };
    private static readonly HashSet<string> PaletteSolutionKeys = new(StringComparer.Ordinal) { "accept" };
    private static readonly HashSet<string> NumberEntries = new(StringComparer.Ordinal) { "plain", "fraction", "mixed" };
    private static readonly HashSet<string> UnitModes = new(StringComparer.Ordinal)
        { SlotAnswerNormalizer.UnitModeNone, SlotAnswerNormalizer.UnitModeOptional, SlotAnswerNormalizer.UnitModeRequired };

    private static readonly JsonDocumentOptions StrictOptions = new() { MaxDepth = 16, AllowDuplicateProperties = false };
    private static readonly JsonDocumentOptions ReadOptions = new() { MaxDepth = 256 };
    private static readonly JsonDocumentOptions DefinitionOptions = new() { MaxDepth = 64, AllowDuplicateProperties = false };

    public string InteractionType => Type;

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

    // ================================================================== IInteractionGrader

    public bool Validate(string? plan, string? payload)
    {
        if (payload == null)
            return true;
        if (payload.Length > InteractionPayload.MaxLength)
            return false;
        if (!TryParsePayload(payload, out var values))
            return false;
        if (values.All(v => SlotAnswerNormalizer.IsBlank(v.Value)))
            return true;
        if (!TryReadPlan(plan, ReadOptions, out var planInfo))
            return false;

        foreach (var (slotId, value) in values)
        {
            if (!planInfo.ById.TryGetValue(slotId, out var slot))
                return false;
            if (slot.Kind == KindSelect && !SlotAnswerNormalizer.IsBlank(value) && !slot.OptionIds.Contains(value!))
                return false;
        }
        return true;
    }

    /// <summary>Tüm değerler boş (ya da <c>values: {}</c>) = cevap yok → null.</summary>
    public string? Normalize(string? payload)
        => payload != null && TryParsePayload(payload, out var values) && values.All(v => SlotAnswerNormalizer.IsBlank(v.Value))
            ? null
            : payload;

    public InteractionGradeResult Grade(InteractionGradeInput input)
    {
        if (!TryReadPlan(input.Plan, ReadOptions, out var plan) || plan.Slots.Count == 0)
            return InteractionGradeResult.Ungradable;

        using var solutionDoc = TryParseObject(input.Solution, ReadOptions);
        if (solutionDoc == null || !solutionDoc.RootElement.EnumerateObject().Any())
            return InteractionGradeResult.Ungradable;
        var solution = solutionDoc.RootElement;

        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (input.Payload != null && TryParsePayload(input.Payload, out var parsed))
        {
            foreach (var (slotId, value) in parsed)
                values.TryAdd(slotId, value);
        }

        var rows = new List<(string Id, bool Correct)>(plan.Slots.Count);
        foreach (var slot in plan.Slots)
        {
            var correct = solution.TryGetProperty(slot.Id, out var slotSolution)
                && values.TryGetValue(slot.Id, out var value)
                && !SlotAnswerNormalizer.IsBlank(value)
                && IsSlotCorrect(slot.Kind, slotSolution, value);
            rows.Add((slot.Id, correct));
        }

        return InteractionGradeResult.Of(rows.Count(r => r.Correct), rows.Count, WriteFeedback(rows));
    }

    /// <summary>
    /// issue #584: öğe = kutu (plan sırası). Cevap: <c>select</c>'te seçenek (id + etiket), diğerlerinde yazılan metin
    /// (<see cref="InteractionAnalysis.TextKey"/> ile gruplanır; "3/4" ile "3 / 4" ayrı grup kalabilir — doğruluk yine kontrolcünün).
    /// </summary>
    public IReadOnlyList<InteractionItemResponse> Analyze(InteractionGradeInput input)
    {
        if (!TryReadPlan(input.Plan, ReadOptions, out var plan) || plan.Slots.Count == 0)
            return [];

        using var solutionDoc = TryParseObject(input.Solution, ReadOptions);
        if (solutionDoc == null || !solutionDoc.RootElement.EnumerateObject().Any())
            return [];
        var solution = solutionDoc.RootElement;

        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        if (input.Payload != null && TryParsePayload(input.Payload, out var parsed))
        {
            foreach (var (slotId, value) in parsed)
                values.TryAdd(slotId, value);
        }

        var slotLabels = InteractionAnalysis.ToMap(InteractionAnalysis.ReadEntries(input.Plan, "slots"));
        var options = InteractionAnalysis.ReadSlotOptions(input.Plan);
        var items = new List<InteractionItemResponse>(plan.Slots.Count);
        for (var i = 0; i < plan.Slots.Count; i++)
        {
            var slot = plan.Slots[i];
            var value = values.GetValueOrDefault(slot.Id);
            var blank = SlotAnswerNormalizer.IsBlank(value);
            var correct = !blank && solution.TryGetProperty(slot.Id, out var slotSolution)
                && IsSlotCorrect(slot.Kind, slotSolution, value);
            string? key = null, label = null;
            if (!blank)
            {
                if (slot.Kind == KindSelect)
                {
                    key = value!.Trim();
                    label = options.TryGetValue(slot.Id, out var map) && map.TryGetValue(key, out var optionLabel) ? optionLabel : key;
                }
                else
                {
                    key = InteractionAnalysis.TextKey(value!);
                    label = InteractionAnalysis.TextLabel(value!);
                }
            }
            items.Add(new InteractionItemResponse(slot.Id, slotLabels.GetValueOrDefault(slot.Id) ?? "#" + (i + 1), correct, key, label));
        }
        return items;
    }

    /// <summary>
    /// Kayıt kuralı: plan zorunlu ve şemaya uygun; çözüm boşsa geçerli (taslak, puanlanamaz), doluysa plandaki HER kutu için
    /// türüne uygun bir girdi, plan dışı kutu yok, izinli alan listesi dışı alan yok.
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
                if (!IsValidId(property.Name) || !planInfo.ById.TryGetValue(property.Name, out var slot) || !covered.Add(property.Name))
                    return false;
                if (!IsValidSlotSolution(slot, property.Value))
                    return false;
            }
            return covered.Count == planInfo.Slots.Count;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    // ================================================================== kutu karşılaştırma (fixture'ın da kullandığı)

    /// <summary>
    /// Tek kutunun cevabı doğru mu? <paramref name="slotSolution"/> = çözümdeki kutu girdisi. Toleranslı: bozuk çözüm alanı
    /// varsayılana düşer, bilinmeyen tür ya da okunamayan çözüm = yanlış. İstisna atmaz.
    /// </summary>
    public static bool IsSlotCorrect(string kind, JsonElement slotSolution, string? answer)
    {
        if (slotSolution.ValueKind != JsonValueKind.Object || SlotAnswerNormalizer.IsBlank(answer))
            return false;
        try
        {
            switch (kind)
            {
                case KindNumber:
                    if (!slotSolution.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.String)
                        return false;
                    var unitMode = ReadString(slotSolution, "unitMode") ?? SlotAnswerNormalizer.UnitModeNone;
                    return SlotAnswerNormalizer.NumberMatches(new SlotAnswerNormalizer.NumberSolution(
                        value.GetString() ?? string.Empty,
                        ReadBool(slotSolution, "equivalence", true),
                        ReadBool(slotSolution, "requireSimplest", false),
                        ReadStrings(slotSolution, "units"),
                        unitMode), answer);
                case KindText:
                    return SlotAnswerNormalizer.TextMatches(ReadStrings(slotSolution, "accept"), ReadStrings(slotSolution, "optionalSuffixes"), answer);
                case KindSelect:
                    return ReadStrings(slotSolution, "optionIds").Contains(answer, StringComparer.Ordinal);
                case KindPalette:
                    return SlotAnswerNormalizer.PaletteMatches(ReadStrings(slotSolution, "accept"), answer);
                default:
                    return false;
            }
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or FormatException)
        {
            return false;
        }
    }

    private static string? ReadString(JsonElement obj, string name)
        => obj.TryGetProperty(name, out var p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static bool ReadBool(JsonElement obj, string name, bool fallback)
        => obj.TryGetProperty(name, out var p) && p.ValueKind is JsonValueKind.True or JsonValueKind.False ? p.GetBoolean() : fallback;

    private static List<string> ReadStrings(JsonElement obj, string name)
    {
        var list = new List<string>();
        if (!obj.TryGetProperty(name, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return list;
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && item.GetString() is { } s)
                list.Add(s);
        }
        return list;
    }

    // ================================================================== plan

    /// <param name="Entry">Sayı kutusunun giriş bileşeni (<c>plain</c> | <c>fraction</c> | <c>mixed</c>); yoksa/diğer türlerde <c>plain</c>.</param>
    private sealed record SlotInfo(string Id, string Kind, HashSet<string> OptionIds, string Entry = "plain");

    private sealed record PlanInfo(List<SlotInfo> Slots, Dictionary<string, SlotInfo> ById);

    /// <summary>
    /// Plan okuma. <paramref name="strict"/> false (okuma/kontrol): yalnız kutu id'si, türü ve seçenek id'leri; bozuk kutu
    /// atlanır. true (kayıt): tam şema (tip, sürüm, sınırlar, id deseni, etiket, kutu, tür alanları, izinli alanlar).
    /// </summary>
    private static bool TryReadPlan(string? plan, JsonDocumentOptions options, out PlanInfo info, bool strict = false)
    {
        info = new PlanInfo(new List<SlotInfo>(), new Dictionary<string, SlotInfo>(StringComparer.Ordinal));
        using var doc = TryParseObject(plan, options);
        if (doc == null)
            return false;
        var root = doc.RootElement;
        try
        {
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

            if (!root.TryGetProperty("slots", out var slots) || slots.ValueKind != JsonValueKind.Array)
                return false;

            var hasLayout = root.TryGetProperty("layout", out var layout) && layout.ValueKind != JsonValueKind.Null;
            foreach (var slotEl in slots.EnumerateArray())
            {
                if (!TryReadSlot(slotEl, strict, hasLayout, out var slot))
                {
                    if (strict) return false;
                    continue;
                }
                if (info.ById.TryAdd(slot.Id, slot))
                    info.Slots.Add(slot);
                else if (strict)
                    return false;
            }

            if (!strict)
                return true;
            if (info.Slots.Count is < MinSlots or > MaxSlots)
                return false;
            return !hasLayout || IsValidLayout(layout, slots, info.Slots.Select(x => x.Id).ToList());
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static readonly string[] LayoutKinds = { InteractionLayout.KindList, InteractionLayout.KindText, InteractionLayout.KindTable };

    /// <summary>issue #590: düzen geçerli ve <c>list</c>'te her kutunun boş olmayan etiketi (soru metni) var.</summary>
    private static bool IsValidLayout(JsonElement layout, JsonElement slots, List<string> slotIds)
    {
        if (!InteractionLayout.TryValidate(layout, LayoutKinds, slotIds, out var kind))
            return false;
        if (kind != InteractionLayout.KindList)
            return true;
        return slots.EnumerateArray().All(slot => slot.TryGetProperty("label", out var label)
            && label.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(label.GetString()));
    }

    private static bool TryReadSlot(JsonElement slotEl, bool strict, bool hasLayout, out SlotInfo slot)
    {
        slot = null!;
        if (slotEl.ValueKind != JsonValueKind.Object || !slotEl.TryGetProperty("id", out var idProp)
            || idProp.ValueKind != JsonValueKind.String)
            return false;
        var id = idProp.GetString() ?? string.Empty;
        if (id.Length == 0 || (strict && !IsValidId(id)))
            return false;
        if (!slotEl.TryGetProperty("input", out var input) || input.ValueKind != JsonValueKind.Object
            || !input.TryGetProperty("kind", out var kindProp) || kindProp.ValueKind != JsonValueKind.String)
            return false;
        var kind = kindProp.GetString() ?? string.Empty;

        if (strict)
        {
            if (!HasOnlyKeys(slotEl, SlotKeys))
                return false;
            if (slotEl.TryGetProperty("label", out var label) && label.ValueKind != JsonValueKind.Null
                && (label.ValueKind != JsonValueKind.String || (label.GetString()?.Length ?? 0) > MaxLabelLength))
                return false;
            // issue #590: düzenli (görselsiz) planda kutu koordinatı yok; düzensiz planda zorunlu.
            var hasBox = slotEl.TryGetProperty("box", out var box) && box.ValueKind != JsonValueKind.Null;
            if (hasLayout ? hasBox : !hasBox || !IsValidBox(box))
                return false;
            if (!IsValidInput(kind, input))
                return false;
        }

        var optionIds = new HashSet<string>(StringComparer.Ordinal);
        if (kind == KindSelect && input.TryGetProperty("options", out var options) && options.ValueKind == JsonValueKind.Array)
        {
            foreach (var option in options.EnumerateArray())
            {
                if (option.ValueKind == JsonValueKind.Object && option.TryGetProperty("id", out var oid)
                    && oid.ValueKind == JsonValueKind.String && oid.GetString() is { Length: > 0 } optionId)
                    optionIds.Add(optionId);
            }
        }

        var entry = kind == KindNumber && input.TryGetProperty("entry", out var entryProp) && entryProp.ValueKind == JsonValueKind.String
            && entryProp.GetString() is { } entryValue && NumberEntries.Contains(entryValue)
            ? entryValue
            : "plain";
        slot = new SlotInfo(id, kind, optionIds, entry);
        return true;
    }

    private static bool IsValidInput(string kind, JsonElement input)
    {
        switch (kind)
        {
            case KindNumber:
                if (!HasOnlyKeys(input, NumberInputKeys))
                    return false;
                if (input.TryGetProperty("entry", out var entry) && entry.ValueKind != JsonValueKind.Null
                    && (entry.ValueKind != JsonValueKind.String || !NumberEntries.Contains(entry.GetString() ?? string.Empty)))
                    return false;
                if (input.TryGetProperty("unit", out var unit) && unit.ValueKind != JsonValueKind.Null
                    && (unit.ValueKind != JsonValueKind.String || (unit.GetString()?.Length ?? 0) > MaxUnitHintLength))
                    return false;
                return true;
            case KindText:
                return HasOnlyKeys(input, TextInputKeys);
            case KindSelect:
            {
                if (!HasOnlyKeys(input, SelectInputKeys) || !input.TryGetProperty("options", out var options)
                    || options.ValueKind != JsonValueKind.Array)
                    return false;
                var ids = new HashSet<string>(StringComparer.Ordinal);
                foreach (var option in options.EnumerateArray())
                {
                    if (option.ValueKind != JsonValueKind.Object || !HasOnlyKeys(option, OptionKeys)
                        || !option.TryGetProperty("id", out var oid) || oid.ValueKind != JsonValueKind.String
                        || !IsValidId(oid.GetString()) || !ids.Add(oid.GetString()!))
                        return false;
                    if (!option.TryGetProperty("label", out var label) || label.ValueKind != JsonValueKind.String
                        || string.IsNullOrWhiteSpace(label.GetString()) || label.GetString()!.Length > MaxLabelLength)
                        return false;
                }
                return ids.Count is >= MinOptions and <= MaxOptions;
            }
            case KindPalette:
            {
                if (!HasOnlyKeys(input, PaletteInputKeys) || !input.TryGetProperty("symbols", out var symbols)
                    || symbols.ValueKind != JsonValueKind.Array)
                    return false;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var symbol in symbols.EnumerateArray())
                {
                    if (symbol.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(symbol.GetString())
                        || symbol.GetString()!.Length > MaxSymbolLength || !seen.Add(symbol.GetString()!))
                        return false;
                }
                return seen.Count is >= MinSymbols and <= MaxSymbols;
            }
            default:
                return false;
        }
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

    // ================================================================== çözüm (kayıt)

    private static bool IsValidSlotSolution(SlotInfo slot, JsonElement solution)
    {
        if (solution.ValueKind != JsonValueKind.Object)
            return false;
        switch (slot.Kind)
        {
            case KindNumber:
            {
                if (!HasOnlyKeys(solution, NumberSolutionKeys))
                    return false;
                if (!solution.TryGetProperty("value", out var valueEl) || valueEl.ValueKind != JsonValueKind.String)
                    return false;
                if (!SlotAnswerNormalizer.TryParseNumber(valueEl.GetString(), out var value) || value.Unit != null)
                    return false;
                if (!IsOptionalBool(solution, "equivalence") || !IsOptionalBool(solution, "requireSimplest"))
                    return false;
                var unitMode = SlotAnswerNormalizer.UnitModeNone;
                if (solution.TryGetProperty("unitMode", out var modeEl) && modeEl.ValueKind != JsonValueKind.Null)
                {
                    if (modeEl.ValueKind != JsonValueKind.String || !UnitModes.Contains(modeEl.GetString() ?? string.Empty))
                        return false;
                    unitMode = modeEl.GetString()!;
                }
                if (!TryReadStringList(solution, "units", 0, MaxUnits, MaxUnitLength, out var units, SlotAnswerNormalizer.NormalizeUnit))
                    return false;
                if (unitMode != SlotAnswerNormalizer.UnitModeNone && units.Count == 0)
                    return false;
                // review #575: her kabul edilen birim öğrencinin yazabileceği bir birim olmalı.
                if (!units.All(SlotAnswerNormalizer.IsParseableUnit))
                    return false;
                // review #575: kesir bileşenleri birim almaz → birim zorunlu + plain olmayan giriş çözülemez.
                if (unitMode == SlotAnswerNormalizer.UnitModeRequired && slot.Entry != "plain")
                    return false;
                // Yüzdeye birim yazılamaz: birim zorunlu yüzde çözülemez.
                if (unitMode == SlotAnswerNormalizer.UnitModeRequired && value.Form == SlotAnswerNormalizer.NumberForm.Percent)
                    return false;
                // Biçim birebir istenip en sade kesir de isteniyorsa sade olmayan çözüm hiçbir cevapla doğru olmaz.
                var equivalence = ReadBool(solution, "equivalence", true);
                if (!equivalence && ReadBool(solution, "requireSimplest", false)
                    && value.Form is SlotAnswerNormalizer.NumberForm.Fraction or SlotAnswerNormalizer.NumberForm.Mixed
                    && !value.IsSimplest)
                    return false;
                // review #575: biçim birebir isteniyorsa giriş bileşeni o biçimi üretebilmeli — pay/payda bileşeni yalnız kesir,
                // tam+pay+payda bileşeni tam sayılı kesir, kesir ya da tam sayı gönderir. plain her biçimi yazabilir.
                if (!equivalence && slot.Entry == "fraction" && value.Form != SlotAnswerNormalizer.NumberForm.Fraction)
                    return false;
                // Tam+pay+payda bileşeni: tam kısım boşsa "13/5", kesir boşsa "2" gönderir → Mixed | Fraction | Integer.
                if (!equivalence && slot.Entry == "mixed"
                    && value.Form is not (SlotAnswerNormalizer.NumberForm.Mixed or SlotAnswerNormalizer.NumberForm.Fraction or SlotAnswerNormalizer.NumberForm.Integer))
                    return false;
                return true;
            }
            case KindText:
                return HasOnlyKeys(solution, TextSolutionKeys)
                    && TryReadStringList(solution, "accept", 1, MaxAcceptEntries, MaxAcceptLength, out _, SlotAnswerNormalizer.NormalizeText, required: true)
                    && TryReadStringList(solution, "optionalSuffixes", 0, MaxSuffixEntries, MaxAcceptLength, out _, SlotAnswerNormalizer.NormalizeText);
            case KindSelect:
            {
                if (!HasOnlyKeys(solution, SelectSolutionKeys)
                    || !TryReadStringList(solution, "optionIds", 1, MaxOptions, 32, out var ids, x => x, required: true))
                    return false;
                return ids.All(id => IsValidId(id) && slot.OptionIds.Contains(id));
            }
            case KindPalette:
                return HasOnlyKeys(solution, PaletteSolutionKeys)
                    && TryReadStringList(solution, "accept", 1, MaxAcceptEntries, MaxAcceptLength, out _, SlotAnswerNormalizer.NormalizePalette, required: true);
            default:
                return false;
        }
    }

    private static bool IsOptionalBool(JsonElement obj, string name)
        => !obj.TryGetProperty(name, out var p) || p.ValueKind is JsonValueKind.True or JsonValueKind.False or JsonValueKind.Null;

    /// <summary>
    /// Dizi alanı: string öğeler, her biri 1..<paramref name="maxLength"/> karakter ve normalize hâli boş değil, yinelenmez
    /// (normalize hâline göre). Alan yoksa <paramref name="required"/> değilse geçerli (boş liste).
    /// </summary>
    private static bool TryReadStringList(JsonElement obj, string name, int min, int max, int maxLength,
        out List<string> values, Func<string, string> normalize, bool required = false)
    {
        values = new List<string>();
        if (!obj.TryGetProperty(name, out var arr) || arr.ValueKind == JsonValueKind.Null)
            return !required && min == 0;
        if (arr.ValueKind != JsonValueKind.Array)
            return false;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
                return false;
            var s = item.GetString() ?? string.Empty;
            if (s.Length == 0 || s.Length > maxLength)
                return false;
            var key = normalize(s);
            if (key.Length == 0 || !seen.Add(key))
                return false;
            values.Add(s);
        }
        return values.Count >= min && values.Count <= max;
    }

    // ================================================================== payload / yardımcılar

    /// <summary>
    /// Katı payload: nesne, <c>type</c> = <see cref="Type"/>, varsa <c>version</c> = 1, <c>values</c> nesnesi (kutu id → string
    /// ≤ <see cref="MaxValueLength"/> ya da null).
    /// </summary>
    private static bool TryParsePayload(string payload, out List<(string SlotId, string? Value)> values)
    {
        values = new List<(string, string?)>();
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
            if (!root.TryGetProperty("values", out var map) || map.ValueKind != JsonValueKind.Object)
                return false;

            foreach (var entry in map.EnumerateObject())
            {
                if (!IsValidId(entry.Name))
                    return false;
                string? value;
                if (entry.Value.ValueKind == JsonValueKind.Null)
                    value = null;
                else if (entry.Value.ValueKind == JsonValueKind.String)
                    value = entry.Value.GetString();
                else
                    return false;
                if (value is { Length: > MaxValueLength })
                    return false;
                values.Add((entry.Name, value));
            }
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            values.Clear();
            return false;
        }
    }

    private static JsonDocument? TryParseObject(string? json, JsonDocumentOptions options)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            var doc = JsonDocument.Parse(json, options);
            if (doc.RootElement.ValueKind == JsonValueKind.Object)
                return doc;
            doc.Dispose();
            return null;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return null;
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
