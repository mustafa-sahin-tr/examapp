using System.IO;
using System.Text;
using System.Text.Json;

namespace ExamApp.Api.Services.Interactions;

/// <summary>
/// issue #582: <c>textSelect</c> kontrolcüsü (bileşen D, metin üzerinde işaretleme) — metin token'larına dokunarak altını çiz /
/// yuvarlak içine al / yanlışı bul (<c>select</c>), <c>( )</c> boşluklarına paletten noktalama koy (<c>punctuation</c>), yanlışı
/// seçip düzeltmesini yaz (<c>correct</c>).
/// <list type="bullet">
/// <item>Plan v1: <c>{version:1,type:"textSelect",mode,style?,palette?,tokens:[{id,text,line,boxes:[box],gap?}]}</c>. <c>style</c>
/// (<c>underline</c> | <c>circle</c> | <c>highlight</c>) yalnız görünüm; <c>palette</c> yalnız <c>punctuation</c>'da ve zorunlu.
/// Token sırası = okuma sırası; <c>line</c> azalmaz; kutular <see cref="InteractionBoxes"/> kuralında (1..3; tirelenmiş kelime
/// birden çok kutu). <c>punctuation</c>'da 1..100 gap token zorunlu, diğer modlarda gap yok.</item>
/// <item>Çözüm (#570, öğrenciye gitmez): select <c>{targets:[{tokenIds}]}</c>; correct
/// <c>{targets:[{tokenIds,accept,optionalSuffixes?}]}</c>; punctuation <c>{gaps:{"g1":{accept:["."]}}}</c> (her gap için, kabul
/// palette'ten). Bir token en çok bir hedefte; hedef 1..8 token (çok kelimeli hata: "Sonun da").</item>
/// <item>Öğrenci payload'u: <c>{version:1,type:"textSelect",selected:["t12"],values:{"t12":"Sonunda"}}</c> — select/correct
/// <c>selected</c> (gap olmayan token'lar); correct'te <c>values</c> anahtarları seçili token'lar; punctuation'da yalnız
/// <c>values</c> (gap id → palet sembolü ya da boş).</item>
/// </list>
/// Kontrol: select'te hedef "bulundu" = token'larından biri seçili; correct'te ayrıca o token'ın değeri hedefin kabulüyle
/// eşleşir (<see cref="SlotAnswerNormalizer.TextMatches"/>). Fazla = hiçbir hedefte olmayan seçili token. <c>CorrectCount</c> =
/// bulunan hedef, <c>TotalCount</c> = hedef + fazla. Geri bildirim yalnız SEÇİLİ token'lar (plan sırası; <c>reason</c>:
/// <c>extra</c> | <c>value</c>) ve sayılar (<c>found</c>, <c>missing</c>, <c>extra</c>); hangi token'ın eksik olduğu yazılmaz.
/// Fazla seçim <see cref="TooManyExtraMin"/> eşiğini aşarsa satırlar yazılmaz (<c>tooMany</c>). punctuation'da gap başına satır
/// (plan sırası). Çözümü olmayan (ya da çözümü planın gap'leriyle kesişmeyen) soru puanlanamaz (0/0).
/// </summary>
public sealed class TextSelectGrader : IInteractionGrader
{
    public const string Type = "textSelect";

    public const string ModeSelect = "select";
    public const string ModePunctuation = "punctuation";
    public const string ModeCorrect = "correct";

    public const string StyleUnderline = "underline";
    public const string StyleCircle = "circle";
    public const string StyleHighlight = "highlight";

    public const int MinTokens = 1;
    public const int MaxTokens = 600;
    public const int MaxTokenTextLength = 64;
    public const int MaxLines = 300;
    public const int MinBoxes = 1;
    public const int MaxBoxes = TextSelectTokenizer.MaxBoxes;
    public const int MinGaps = 1;
    public const int MaxGaps = 100;
    public const int MinSymbols = 1;
    public const int MaxSymbols = 20;
    public const int MaxSymbolLength = 8;
    public const int MinTargets = 1;
    public const int MaxTargets = 100;
    public const int MaxTargetTokens = 8;
    public const int MaxAcceptEntries = 20;
    public const int MaxAcceptLength = 100;
    public const int MaxSuffixEntries = 10;

    /// <summary>Öğrencinin tek token'a yazabileceği en uzun düzeltme / sembol (karakter).</summary>
    public const int MaxValueLength = 200;

    /// <summary>Geri bildirim nedenleri: hiçbir hedefte olmayan seçim / hedef token'ı ama düzeltme yanlış ya da boş.</summary>
    public const string ReasonExtra = "extra";
    public const string ReasonValue = "value";

    /// <summary>
    /// review #582 (D1): "hepsini seç" ile tek kontrolde tüm hedeflerin öğrenilmesini engeller. select/correct'te fazla seçim
    /// sayısı <c>Math.Max(TooManyExtraMin, hedef sayısı)</c>'nı aşarsa geri bildirimde satır yazılmaz (<c>rows: []</c>) ve
    /// <c>tooMany: true</c> eklenir; <c>found</c>/<c>missing</c>/<c>extra</c> sayıları ile <c>CorrectCount</c>/<c>TotalCount</c>
    /// değişmez. Alt sınır 3: az hedefli metinde birkaç yanlış seçim satır bilgisini kaybettirmesin.
    /// </summary>
    public const int TooManyExtraMin = 3;

    private static readonly HashSet<string> Modes = new(StringComparer.Ordinal) { ModeSelect, ModePunctuation, ModeCorrect };
    private static readonly HashSet<string> Styles = new(StringComparer.Ordinal) { StyleUnderline, StyleCircle, StyleHighlight };

    // Kayıtta planın / çözümün her düzeyinde izinli alanlar (allowlist; #574 M1 ile aynı gerekçe).
    private static readonly HashSet<string> RootKeys = new(StringComparer.Ordinal) { "version", "type", "mode", "style", "palette", "tokens" };
    private static readonly HashSet<string> TokenKeys = new(StringComparer.Ordinal) { "id", "text", "line", "boxes", "gap" };
    private static readonly HashSet<string> SelectSolutionKeys = new(StringComparer.Ordinal) { "targets" };
    private static readonly HashSet<string> PunctuationSolutionKeys = new(StringComparer.Ordinal) { "gaps" };
    private static readonly HashSet<string> SelectTargetKeys = new(StringComparer.Ordinal) { "tokenIds" };
    private static readonly HashSet<string> CorrectTargetKeys = new(StringComparer.Ordinal) { "tokenIds", "accept", "optionalSuffixes" };
    private static readonly HashSet<string> GapSolutionKeys = new(StringComparer.Ordinal) { "accept" };
    private static readonly HashSet<string> PayloadKeys = new(StringComparer.Ordinal) { "version", "type", "selected", "values" };

    // Payload (istemci girdisi) katı: yinelenen anahtar yasak.
    private static readonly JsonDocumentOptions StrictOptions = new() { MaxDepth = 16, AllowDuplicateProperties = false };

    // Plan/çözüm (kayıtlı veri) okuması toleranslı: okuma istisna atmamalı.
    private static readonly JsonDocumentOptions ReadOptions = new() { MaxDepth = 256 };

    // Yazma yolu (ValidateDefinition) katı.
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

    private static bool IsAbsent(JsonElement obj, string name)
        => !obj.TryGetProperty(name, out var p) || p.ValueKind == JsonValueKind.Null;

    // ================================================================== IInteractionGrader

    public bool Validate(string? plan, string? payload)
    {
        if (payload == null)
            return true;
        if (payload.Length > InteractionPayload.MaxLength)
            return false;
        if (!TryParsePayload(payload, out var answer))
            return false;
        if (answer.IsEmpty)
            return true;
        if (!TryReadPlan(plan, ReadOptions, out var info))
            return false;

        if (info.Mode == ModePunctuation)
        {
            if (answer.Selected.Count > 0)
                return false;
            foreach (var (id, value) in answer.Values)
            {
                if (!info.ById.TryGetValue(id, out var token) || !token.Gap)
                    return false;
                if (!SlotAnswerNormalizer.IsBlank(value) && !info.Palette.Contains(value!.Trim()))
                    return false;
            }
            return true;
        }

        foreach (var id in answer.Selected)
        {
            if (!info.ById.TryGetValue(id, out var token) || token.Gap)
                return false;
        }
        if (info.Mode == ModeSelect)
            return answer.Values.All(v => SlotAnswerNormalizer.IsBlank(v.Value));

        var selected = answer.Selected.ToHashSet(StringComparer.Ordinal);
        return answer.Values.All(v => selected.Contains(v.Id));
    }

    /// <summary>Seçim yok ve tüm değerler boş = cevap yok → null.</summary>
    public string? Normalize(string? payload)
        => payload != null && TryParsePayload(payload, out var answer) && answer.IsEmpty ? null : payload;

    public InteractionGradeResult Grade(InteractionGradeInput input)
    {
        if (!TryReadPlan(input.Plan, ReadOptions, out var plan) || plan.Tokens.Count == 0)
            return InteractionGradeResult.Ungradable;

        var answer = input.Payload != null && TryParsePayload(input.Payload, out var parsed) ? parsed : Answer.Empty;
        var values = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var (id, value) in answer.Values)
            values.TryAdd(id, value);

        return plan.Mode == ModePunctuation
            ? GradePunctuation(plan, input.Solution, values)
            : GradeTargets(plan, input.Solution, answer, values);
    }

    private static InteractionGradeResult GradePunctuation(PlanInfo plan, string? solutionJson, Dictionary<string, string?> values)
    {
        using var doc = TryParseObject(solutionJson, ReadOptions);
        if (doc == null || !doc.RootElement.TryGetProperty("gaps", out var gaps) || gaps.ValueKind != JsonValueKind.Object
            || !gaps.EnumerateObject().Any())
            return InteractionGradeResult.Ungradable;

        // review #582: çözümün hiçbir gap'i planda yoksa puanlanamaz (TS ikiziyle aynı).
        if (!plan.Tokens.Any(t => t.Gap && gaps.TryGetProperty(t.Id, out _)))
            return InteractionGradeResult.Ungradable;

        var rows = new List<(string Id, bool Correct, string? Reason)>();
        foreach (var token in plan.Tokens.Where(t => t.Gap))
        {
            var correct = gaps.TryGetProperty(token.Id, out var gapSolution) && gapSolution.ValueKind == JsonValueKind.Object
                && values.TryGetValue(token.Id, out var value) && !SlotAnswerNormalizer.IsBlank(value)
                && ReadStrings(gapSolution, "accept").Any(a => string.Equals(a.Trim(), value!.Trim(), StringComparison.Ordinal));
            rows.Add((token.Id, correct, null));
        }
        if (rows.Count == 0)
            return InteractionGradeResult.Ungradable;

        return InteractionGradeResult.Of(rows.Count(r => r.Correct), rows.Count, WriteFeedback(rows, counts: null));
    }

    private static InteractionGradeResult GradeTargets(
        PlanInfo plan, string? solutionJson, Answer answer, Dictionary<string, string?> values)
    {
        var targets = ReadTargets(solutionJson, plan);
        if (targets.Count == 0)
            return InteractionGradeResult.Ungradable;

        var targetOf = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < targets.Count; i++)
        {
            foreach (var id in targets[i].TokenIds)
                targetOf[id] = i;
        }

        var selected = answer.Selected.Where(id => plan.ById.TryGetValue(id, out var t) && !t.Gap).ToHashSet(StringComparer.Ordinal);
        var found = new bool[targets.Count];
        foreach (var id in selected)
        {
            if (!targetOf.TryGetValue(id, out var index))
                continue;
            if (plan.Mode == ModeSelect)
                found[index] = true;
            else if (values.TryGetValue(id, out var value) && !SlotAnswerNormalizer.IsBlank(value)
                     && SlotAnswerNormalizer.TextMatches(targets[index].Accept, targets[index].Suffixes, value))
                found[index] = true;
        }

        var rows = new List<(string Id, bool Correct, string? Reason)>();
        var extra = 0;
        foreach (var token in plan.Tokens)
        {
            if (!selected.Contains(token.Id))
                continue;
            if (!targetOf.TryGetValue(token.Id, out var index))
            {
                extra++;
                rows.Add((token.Id, false, ReasonExtra));
            }
            else
                rows.Add(found[index] ? (token.Id, true, null) : (token.Id, false, ReasonValue));
        }

        var foundCount = found.Count(f => f);
        var tooMany = extra > Math.Max(TooManyExtraMin, targets.Count);
        if (tooMany)
            rows.Clear();
        return InteractionGradeResult.Of(foundCount, targets.Count + extra,
            WriteFeedback(rows, (foundCount, targets.Count - foundCount, extra), tooMany));
    }

    /// <summary>
    /// Kayıt kuralı: plan zorunlu ve şemaya uygun; çözüm boşsa geçerli (taslak, puanlanamaz), doluysa moduna uygun, plan dışı
    /// token yok, izinli alan listesi dışı alan yok.
    /// </summary>
    public bool ValidateDefinition(string? plan, string? solution)
    {
        if (!TryReadPlan(plan, DefinitionOptions, out var info, strict: true))
            return false;
        if (string.IsNullOrWhiteSpace(solution))
            return true;

        try
        {
            using var doc = JsonDocument.Parse(solution, DefinitionOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;
            return info.Mode == ModePunctuation ? IsValidPunctuationSolution(root, info) : IsValidTargetSolution(root, info);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    // ================================================================== plan

    private sealed record TokenInfo(string Id, bool Gap);

    private sealed record PlanInfo(string Mode, List<TokenInfo> Tokens, Dictionary<string, TokenInfo> ById, HashSet<string> Palette);

    /// <summary>
    /// Plan okuma. <paramref name="strict"/> false (okuma/kontrol): mod, token id'leri, gap bayrağı ve palet; bozuk token atlanır.
    /// true (kayıt): tam şema (tip, sürüm, mod, stil, palet, sınırlar, id deseni, metin, satır, kutular, izinli alanlar).
    /// </summary>
    private static bool TryReadPlan(string? plan, JsonDocumentOptions options, out PlanInfo info, bool strict = false)
    {
        info = new PlanInfo(ModeSelect, new List<TokenInfo>(), new Dictionary<string, TokenInfo>(StringComparer.Ordinal),
            new HashSet<string>(StringComparer.Ordinal));
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

            if (!root.TryGetProperty("mode", out var modeEl) || modeEl.ValueKind != JsonValueKind.String
                || !Modes.Contains(modeEl.GetString() ?? string.Empty))
                return false;
            var mode = modeEl.GetString()!;

            if (strict && !IsAbsent(root, "style")
                && (!root.TryGetProperty("style", out var style) || style.ValueKind != JsonValueKind.String
                    || !Styles.Contains(style.GetString() ?? string.Empty)))
                return false;

            var palette = new HashSet<string>(StringComparer.Ordinal);
            if (mode == ModePunctuation)
            {
                if (!TryReadPalette(root, strict, palette))
                    return false;
            }
            else if (strict && !IsAbsent(root, "palette"))
                return false;

            if (!root.TryGetProperty("tokens", out var tokens) || tokens.ValueKind != JsonValueKind.Array)
                return false;

            info = info with { Mode = mode, Palette = palette };
            var previousLine = 0;
            foreach (var tokenEl in tokens.EnumerateArray())
            {
                if (!TryReadToken(tokenEl, strict, ref previousLine, out var token))
                {
                    if (strict) return false;
                    continue;
                }
                if (info.ById.TryAdd(token.Id, token))
                    info.Tokens.Add(token);
                else if (strict)
                    return false;
            }

            if (!strict)
                return true;
            if (info.Tokens.Count is < MinTokens or > MaxTokens)
                return false;
            var gapCount = info.Tokens.Count(t => t.Gap);
            return mode == ModePunctuation ? gapCount is >= MinGaps and <= MaxGaps : gapCount == 0;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryReadPalette(JsonElement root, bool strict, HashSet<string> palette)
    {
        if (!root.TryGetProperty("palette", out var symbols) || symbols.ValueKind != JsonValueKind.Array)
            return !strict;
        foreach (var symbol in symbols.EnumerateArray())
        {
            if (symbol.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(symbol.GetString()))
            {
                if (strict) return false;
                continue;
            }
            var s = symbol.GetString()!;
            if (strict && (s.Length > MaxSymbolLength || s != s.Trim()))
                return false;
            if (!palette.Add(s.Trim()) && strict)
                return false;
        }
        return !strict || palette.Count is >= MinSymbols and <= MaxSymbols;
    }

    private static bool TryReadToken(JsonElement tokenEl, bool strict, ref int previousLine, out TokenInfo token)
    {
        token = null!;
        if (tokenEl.ValueKind != JsonValueKind.Object || !tokenEl.TryGetProperty("id", out var idProp)
            || idProp.ValueKind != JsonValueKind.String)
            return false;
        var id = idProp.GetString() ?? string.Empty;
        if (id.Length == 0 || (strict && !IsValidId(id)))
            return false;

        var gap = false;
        if (tokenEl.TryGetProperty("gap", out var gapEl))
        {
            if (gapEl.ValueKind is JsonValueKind.True or JsonValueKind.False)
                gap = gapEl.GetBoolean();
            else if (strict && gapEl.ValueKind != JsonValueKind.Null)
                return false;
        }

        if (strict)
        {
            if (!HasOnlyKeys(tokenEl, TokenKeys))
                return false;
            if (!tokenEl.TryGetProperty("text", out var text) || text.ValueKind != JsonValueKind.String)
                return false;
            var value = text.GetString() ?? string.Empty;
            if (value.Length > MaxTokenTextLength || (!gap && SlotAnswerNormalizer.IsBlank(value)))
                return false;
            if (!tokenEl.TryGetProperty("line", out var line) || line.ValueKind != JsonValueKind.Number
                || !line.TryGetInt32(out var lineNo) || lineNo < 0 || lineNo >= MaxLines || lineNo < previousLine)
                return false;
            previousLine = lineNo;
            if (!tokenEl.TryGetProperty("boxes", out var boxes) || boxes.ValueKind != JsonValueKind.Array
                || boxes.GetArrayLength() is < MinBoxes or > MaxBoxes)
                return false;
            foreach (var box in boxes.EnumerateArray())
            {
                if (!InteractionBoxes.IsValid(box))
                    return false;
            }
        }

        token = new TokenInfo(id, gap);
        return true;
    }

    // ================================================================== çözüm

    /// <param name="Accept">correct: kabul edilen düzeltmeler; select'te boş.</param>
    private sealed record TargetInfo(List<string> TokenIds, List<string> Accept, List<string> Suffixes);

    /// <summary>Toleranslı çözüm okuma (kontrol): plan dışı / gap token'lar ve başka hedefte kullanılmış token'lar atlanır.</summary>
    private static List<TargetInfo> ReadTargets(string? solutionJson, PlanInfo plan)
    {
        var result = new List<TargetInfo>();
        using var doc = TryParseObject(solutionJson, ReadOptions);
        if (doc == null || !doc.RootElement.TryGetProperty("targets", out var targets) || targets.ValueKind != JsonValueKind.Array)
            return result;

        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in targets.EnumerateArray())
        {
            if (target.ValueKind != JsonValueKind.Object)
                continue;
            var ids = ReadStrings(target, "tokenIds")
                .Where(id => plan.ById.TryGetValue(id, out var t) && !t.Gap && used.Add(id))
                .ToList();
            if (ids.Count == 0)
                continue;
            var accept = plan.Mode == ModeCorrect ? ReadStrings(target, "accept") : new List<string>();
            if (plan.Mode == ModeCorrect && accept.Count == 0)
                continue;
            result.Add(new TargetInfo(ids, accept, ReadStrings(target, "optionalSuffixes")));
        }
        return result;
    }

    private static bool IsValidTargetSolution(JsonElement root, PlanInfo plan)
    {
        if (!HasOnlyKeys(root, SelectSolutionKeys) || !root.TryGetProperty("targets", out var targets)
            || targets.ValueKind != JsonValueKind.Array || targets.GetArrayLength() is < MinTargets or > MaxTargets)
            return false;

        var correct = plan.Mode == ModeCorrect;
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var target in targets.EnumerateArray())
        {
            if (target.ValueKind != JsonValueKind.Object || !HasOnlyKeys(target, correct ? CorrectTargetKeys : SelectTargetKeys))
                return false;
            if (!target.TryGetProperty("tokenIds", out var ids) || ids.ValueKind != JsonValueKind.Array
                || ids.GetArrayLength() is < 1 or > MaxTargetTokens)
                return false;
            foreach (var idEl in ids.EnumerateArray())
            {
                if (idEl.ValueKind != JsonValueKind.String || idEl.GetString() is not { } id || !IsValidId(id)
                    || !plan.ById.TryGetValue(id, out var token) || token.Gap || !used.Add(id))
                    return false;
            }
            if (correct
                && (!TryReadStringList(target, "accept", 1, MaxAcceptEntries, MaxAcceptLength, SlotAnswerNormalizer.NormalizeText, required: true)
                    || !TryReadStringList(target, "optionalSuffixes", 0, MaxSuffixEntries, MaxAcceptLength, SlotAnswerNormalizer.NormalizeText)))
                return false;
        }
        return true;
    }

    private static bool IsValidPunctuationSolution(JsonElement root, PlanInfo plan)
    {
        if (!HasOnlyKeys(root, PunctuationSolutionKeys) || !root.TryGetProperty("gaps", out var gaps)
            || gaps.ValueKind != JsonValueKind.Object)
            return false;

        var covered = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in gaps.EnumerateObject())
        {
            if (!IsValidId(property.Name) || !plan.ById.TryGetValue(property.Name, out var token) || !token.Gap
                || !covered.Add(property.Name))
                return false;
            var gap = property.Value;
            if (gap.ValueKind != JsonValueKind.Object || !HasOnlyKeys(gap, GapSolutionKeys)
                || !gap.TryGetProperty("accept", out var accept) || accept.ValueKind != JsonValueKind.Array
                || accept.GetArrayLength() is < 1 or > MaxAcceptEntries)
                return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var symbol in accept.EnumerateArray())
            {
                if (symbol.ValueKind != JsonValueKind.String || symbol.GetString() is not { } s
                    || !plan.Palette.Contains(s) || !seen.Add(s))
                    return false;
            }
        }
        return covered.Count == plan.Tokens.Count(t => t.Gap);
    }

    /// <summary>
    /// Dizi alanı: string öğeler, her biri 1..<paramref name="maxLength"/> karakter ve normalize hâli boş değil, yinelenmez
    /// (normalize hâline göre). Alan yoksa <paramref name="required"/> değilse geçerli.
    /// </summary>
    private static bool TryReadStringList(JsonElement obj, string name, int min, int max, int maxLength,
        Func<string, string> normalize, bool required = false)
    {
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
        }
        return seen.Count >= min && seen.Count <= max;
    }

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

    // ================================================================== payload / yardımcılar

    private sealed record Answer(List<string> Selected, List<(string Id, string? Value)> Values)
    {
        public static Answer Empty { get; } = new(new List<string>(), new List<(string, string?)>());

        public bool IsEmpty => Selected.Count == 0 && Values.All(v => SlotAnswerNormalizer.IsBlank(v.Value));
    }

    /// <summary>
    /// Katı payload: nesne, yalnız izinli alanlar, <c>type</c> = <see cref="Type"/>, varsa <c>version</c> = 1; <c>selected</c>
    /// (yoksa boş) tekrarsız geçerli id dizisi (≤ <see cref="MaxTokens"/>); <c>values</c> (yoksa boş) id → string
    /// ≤ <see cref="MaxValueLength"/> ya da null.
    /// </summary>
    private static bool TryParsePayload(string payload, out Answer answer)
    {
        answer = Answer.Empty;
        try
        {
            using var doc = JsonDocument.Parse(payload, StrictOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasOnlyKeys(root, PayloadKeys))
                return false;
            if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                || !string.Equals(type.GetString(), Type, StringComparison.Ordinal))
                return false;
            if (root.TryGetProperty("version", out var version)
                && (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1))
                return false;

            var selected = new List<string>();
            if (!IsAbsent(root, "selected"))
            {
                var arr = root.GetProperty("selected");
                if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() > MaxTokens)
                    return false;
                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in arr.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } id || !IsValidId(id) || !seen.Add(id))
                        return false;
                    selected.Add(id);
                }
            }

            var values = new List<(string, string?)>();
            if (!IsAbsent(root, "values"))
            {
                var map = root.GetProperty("values");
                if (map.ValueKind != JsonValueKind.Object)
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
            }

            answer = new Answer(selected, values);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            answer = Answer.Empty;
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

    private static string WriteFeedback(
        IReadOnlyList<(string Id, bool Correct, string? Reason)> rows, (int Found, int Missing, int Extra)? counts, bool tooMany = false)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("rows");
            foreach (var (id, correct, reason) in rows)
            {
                writer.WriteStartObject();
                writer.WriteString("id", id);
                writer.WriteBoolean("correct", correct);
                if (reason != null)
                    writer.WriteString("reason", reason);
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            if (counts is { } c)
            {
                writer.WriteNumber("found", c.Found);
                writer.WriteNumber("missing", c.Missing);
                writer.WriteNumber("extra", c.Extra);
            }
            if (tooMany)
                writer.WriteBoolean("tooMany", true);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
