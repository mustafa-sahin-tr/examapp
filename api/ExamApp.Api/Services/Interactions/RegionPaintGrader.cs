using System.IO;
using System.Text;
using System.Text.Json;
using ExamApp.Api.Helpers;

namespace ExamApp.Api.Services.Interactions;

/// <summary>
/// issue #587: <c>regionPaint</c> kontrolcüsü — bölge seçme ve boyama (bileşen E). Dilim a: ızgara modu ("eş karelere ayrılmış şeklin
/// 2/5'ini siyaha, 0,35'ini maviye boyayalım"). Dilim b: görsel üzerinde bölgeler (<c>mode:"regions"</c>; "çift sayıları maviye
/// boyayalım", "doğru olanları daire içine alalım") — aşağıdaki ikinci listeye bakın.
/// <list type="bullet">
/// <item>Plan v1: <c>{version:1,type:"regionPaint",mode:"grid",rows,cols,palette:[{id,color,label?}]}</c>. Izgara 2..20 × 2..20;
/// palet 1..8 renk, <c>color</c> sabit adlı renklerden (<see cref="Colors"/>, istemci CSS token'ına çevirir), palette tekil; <c>label</c>
/// isteğe bağlı düz metin (≤ 40).</item>
/// <item>Çözüm: <c>{"counts":{"p1":40,"p2":35}}</c> — her palet rengine kaç hücre boyanacağı (0 = bu renk kullanılmamalı, çeldirici);
/// hücrelerin yeri serbest. Toplam ≤ hücre sayısı, en az bir renk &gt; 0.</item>
/// <item>Öğrenci payload'u: <c>{version:1,type:"regionPaint",grid:["..1..",...]}</c> — satır başına <c>cols</c> karakter; <c>.</c> boş,
/// <c>1</c>..<c>8</c> palet sırası (1 tabanlı). Boyasız ızgara = cevap yok.</item>
/// </list>
/// Kontrol: satır = palet rengi (plan sırası); doğru ⇔ o renkte boyanan hücre sayısı çözümdekine eşit. Geri bildirim
/// <c>{"rows":[{id,correct}]}</c>; beklenen sayı yazılmaz. Çözümü olmayan soru puanlanamaz (0/0).
/// <para>Bölge modu:</para>
/// <list type="bullet">
/// <item>Plan v1: <c>{version:1,type:"regionPaint",mode:"regions",style?,palette,regions:[{id,points:[[x,y],...],label?}]}</c>.
/// <c>style</c> (<c>fill</c> | <c>circle</c>, varsayılan fill) yalnız görünüm. Bölge = soru görseline göreli piksel çokgen (3..64 köşe,
/// koordinat 0..100000, alan ≥ 1); 1..100 bölge, id'ler tekil (palet id'leriyle de çakışmaz).</item>
/// <item>Çözüm: <c>{"fills":{"r1":"p1"}}</c> — boyanması gereken bölge → renk; yazılmayan bölge boş kalmalı (çeldirici). En az bir
/// bölge; renk palette olmalı (kullanılmayan palet rengi çeldiricidir).</item>
/// <item>Öğrenci payload'u: <c>{version:1,type:"regionPaint",fills:{"r1":"p1"}}</c>; boş <c>fills</c> = cevap yok.</item>
/// </list>
/// Kontrol <see cref="TextSelectGrader"/> gibi: hedef = çözümdeki bölge; bulundu ⇔ doğru renkle boyalı. Fazla = hedef olmayan boyalı
/// bölge. <c>CorrectCount</c> = bulunan, <c>TotalCount</c> = hedef + fazla. Geri bildirim yalnız BOYALI bölgeler (plan sırası;
/// <c>reason</c>: <c>extra</c> | <c>color</c>) ve sayılar (<c>found</c>, <c>missing</c>, <c>extra</c>); boyanmamış hedef yazılmaz.
/// Fazla boya <see cref="TextSelectGrader.TooManyExtraMin"/> eşiğini aşarsa satırlar yazılmaz (<c>tooMany</c>).
/// <para>Dilim c — numaralı boyama (bölge modunda isteğe bağlı <c>groups</c>; "her sorunun doğru cevabının rengiyle aynı numaralı
/// bölümleri boyayınız", mandala):</para>
/// <list type="bullet">
/// <item>Plan: <c>groups:[{id,label?}]</c> (1..40; id'ler planda tekil) ve her bölgede <c>group</c> (plandaki bir grup; grup varken
/// zorunlu, yokken yasak). Her grubun en az bir bölgesi olur. Grup = soru numarası; bir grubun bütün bölgeleri birlikte boyanır.</item>
/// <item>Çözüm: <c>{"fills":{"g1":"p1"}}</c> — her grup → doğru cevabın rengi (bütün gruplar).</item>
/// <item>Öğrenci payload'u: <c>{fills:{"g1":"p1"}}</c> (anahtar grup id'si).</item>
/// </list>
/// Kontrol grup başına (MCQ gibi): satır = grup (plan sırası), doğru ⇔ doğru renk; boyanmamış grup yanlış. Geri bildirim
/// <c>{"rows":[{id,correct}]}</c>; beklenen renk yazılmaz.
/// <para>Dilim d (issue #588) — daire grafiği (<c>mode:"pie"</c>; "16 eşit dilimin 4'ünü maviye boyayınız"): plan
/// <c>{version:1,type:"regionPaint",mode:"pie",slices,palette}</c> (2..36 eş dilim). Tek satırlık ızgara gibi okunur: çözüm
/// <c>counts</c>, payload <c>grid:["..1.2"]</c> (bir satır, <c>slices</c> karakter; dilim 1 tepeden saat yönünde), kontrol ve analiz
/// ızgarayla aynı.</para>
/// </summary>
public sealed class RegionPaintGrader : IInteractionGrader
{
    public const string Type = "regionPaint";

    public const string GridMode = "grid";
    public const string RegionsMode = "regions";
    public const string PieMode = "pie";

    public const int MinSlices = 2;
    public const int MaxSlices = 36;

    public const string StyleFill = "fill";
    public const string StyleCircle = "circle";

    public const int MaxRegions = 100;
    public const int MaxGroups = 40;
    public const int MinPoints = 3;
    public const int MaxPoints = 64;
    public const double MaxCoordinate = InteractionBoxes.MaxExtent;

    public const string ReasonExtra = "extra";
    public const string ReasonColor = "color";

    public const int MinSize = 2;
    public const int MaxSize = 20;
    public const int MaxPalette = 8;
    public const int MaxLabelLength = 40;

    public const char Empty = '.';

    /// <summary>İzinli palet renkleri (istemcide <c>--paint-&lt;ad&gt;</c> token'ı). Serbest CSS rengi alınmaz.</summary>
    public static readonly IReadOnlySet<string> Colors = new HashSet<string>(StringComparer.Ordinal)
    {
        "red", "orange", "yellow", "green", "blue", "purple", "pink", "brown", "black", "gray",
    };

    private static readonly HashSet<string> RootKeys = new(StringComparer.Ordinal) { "version", "type", "mode", "rows", "cols", "palette" };
    private static readonly HashSet<string> PieRootKeys = new(StringComparer.Ordinal) { "version", "type", "mode", "slices", "palette" };
    private static readonly HashSet<string> RegionsRootKeys = new(StringComparer.Ordinal) { "version", "type", "mode", "style", "palette", "groups", "regions" };
    private static readonly HashSet<string> RegionKeys = new(StringComparer.Ordinal) { "id", "points", "label", "group" };
    private static readonly HashSet<string> GroupKeys = new(StringComparer.Ordinal) { "id", "label" };
    private static readonly HashSet<string> PaletteKeys = new(StringComparer.Ordinal) { "id", "color", "label" };

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
        if (!TryReadPlan(plan, ReadOptions, strict: false, out var model))
            return false;
        return model.IsRegions ? TryParseFills(payload, model, out _) : TryParsePayload(payload, model, out _);
    }

    /// <summary>Boyasız ızgara / boş <c>fills</c> = cevap yok → null.</summary>
    public string? Normalize(string? payload)
    {
        if (payload == null)
            return null;
        if (TryParsePayload(payload, null, out var cells))
            return cells.All(row => row.All(c => c == Empty)) ? null : payload;
        if (TryParseFills(payload, null, out var fills))
            return fills.Count == 0 ? null : payload;
        return payload;
    }

    public InteractionGradeResult Grade(InteractionGradeInput input)
    {
        if (!TryReadPlan(input.Plan, ReadOptions, strict: false, out var model))
            return InteractionGradeResult.Ungradable;
        if (model.IsRegions)
            return GradeRegions(model, input);
        if (!TryReadSolution(input.Solution, model, out var counts))
            return InteractionGradeResult.Ungradable;
        var painted = Painted(model, input.Payload);
        var rows = model.Palette.Where(p => counts.ContainsKey(p.Id))
            .Select(p => (p.Id, painted.GetValueOrDefault(p.Id) == counts[p.Id])).ToList();
        return InteractionGradeResult.Of(rows.Count(r => r.Item2), rows.Count, WriteFeedback(rows));
    }

    /// <summary>
    /// issue #584: ızgarada öğe = palet rengi, cevap = o renkte boyanan hücre sayısı (0 = cevapsız). Bölge modunda öğe = hedef bölge,
    /// cevap = boyandığı renk (etiket, yoksa renk adı).
    /// </summary>
    public IReadOnlyList<InteractionItemResponse> Analyze(InteractionGradeInput input)
    {
        if (!TryReadPlan(input.Plan, ReadOptions, strict: false, out var model))
            return [];
        if (model.IsRegions)
            return AnalyzeRegions(model, input);
        if (!TryReadSolution(input.Solution, model, out var counts))
            return [];
        var painted = Painted(model, input.Payload);
        return model.Palette.Where(p => counts.ContainsKey(p.Id)).Select(p =>
        {
            var count = painted.GetValueOrDefault(p.Id);
            var answer = count == 0 ? null : count.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return new InteractionItemResponse(p.Id, InteractionAnalysis.TextLabel(PaintName(p)), count == counts[p.Id], answer, answer);
        }).ToList();
    }

    /// <summary>
    /// Kayıt kuralı (issue #587): ızgarada 2..20 × 2..20, palet 1..8 tekil id ve tekil izinli renk, etiket isteğe bağlı düz metin
    /// (≤ 40); çözüm boşsa geçerli (taslak), doluysa her palet rengine 0..hücre sayısı tam sayı, toplam ≤ hücre sayısı, en az bir renk
    /// &gt; 0. Bölge modunda 1..100 çokgen bölge (3..64 köşe, alan ≥ 1); çözüm doluysa en az bir bölge, her biri plandaki bir bölgeden
    /// plandaki bir renge. Numaralı boyamada (gruplar) her bölge bir gruba bağlı, her grubun bölgesi var; çözüm her gruba bir renk.
    /// </summary>
    public bool ValidateDefinition(string? plan, string? solution)
    {
        if (!TryReadPlan(plan, DefinitionOptions, strict: true, out var model))
            return false;
        if (string.IsNullOrWhiteSpace(solution))
            return true;
        try
        {
            using var doc = JsonDocument.Parse(solution, DefinitionOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;
            var paletteIds = model.Palette.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            if (model.IsRegions)
            {
                if (!HasOnlyKeys(root, ["fills"]) || !root.TryGetProperty("fills", out var fillsElement)
                    || fillsElement.ValueKind != JsonValueKind.Object)
                    return false;
                var keyIds = model.FillKeys();
                var filled = new HashSet<string>(StringComparer.Ordinal);
                foreach (var entry in fillsElement.EnumerateObject())
                {
                    if (!keyIds.Contains(entry.Name) || !filled.Add(entry.Name) || entry.Value.ValueKind != JsonValueKind.String
                        || !paletteIds.Contains(entry.Value.GetString() ?? string.Empty))
                        return false;
                }
                // Numaralı boyamada her grubun (sorunun) bir doğru rengi var.
                return model.IsGrouped ? filled.Count == keyIds.Count : filled.Count > 0;
            }
            if (!HasOnlyKeys(root, ["counts"]) || !root.TryGetProperty("counts", out var countsElement)
                || countsElement.ValueKind != JsonValueKind.Object)
                return false;
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var total = 0;
            foreach (var entry in countsElement.EnumerateObject())
            {
                if (!paletteIds.Contains(entry.Name) || !seen.Add(entry.Name) || entry.Value.ValueKind != JsonValueKind.Number
                    || !entry.Value.TryGetInt32(out var count) || count < 0 || count > model.Cells)
                    return false;
                total += count;
            }
            return seen.Count == paletteIds.Count && total > 0 && total <= model.Cells;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    // ---- Model ----

    private sealed record Paint(string Id, string Color, string Label);

    private sealed record Area(string Id, string Label, string? Group);

    private sealed record Group(string Id, string Label);

    private sealed record PaintPlan(string Mode, int Rows, int Cols, List<Paint> Palette, List<Area> Regions, List<Group> Groups)
    {
        public bool IsRegions => Mode == RegionsMode;

        /// <summary>Numaralı boyama: boya grup başına.</summary>
        public bool IsGrouped => Groups.Count > 0;

        /// <summary><c>fills</c> anahtarları: gruplu planda grup id'leri, değilse bölge id'leri.</summary>
        public HashSet<string> FillKeys() => IsGrouped
            ? Groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal)
            : Regions.Select(r => r.Id).ToHashSet(StringComparer.Ordinal);

        public int Cells => Rows * Cols;
    }

    private static string PaintName(Paint paint) => string.IsNullOrWhiteSpace(paint.Label) ? paint.Color : paint.Label;

    /// <summary>Palet id'si → boyanan hücre sayısı.</summary>
    private static Dictionary<string, int> Painted(PaintPlan grid, string? payload)
    {
        var painted = new Dictionary<string, int>(StringComparer.Ordinal);
        if (payload == null || !TryParsePayload(payload, grid, out var cells))
            return painted;
        foreach (var row in cells)
            foreach (var c in row)
            {
                if (c == Empty)
                    continue;
                var id = grid.Palette[c - '1'].Id;
                painted[id] = painted.GetValueOrDefault(id) + 1;
            }
        return painted;
    }

    // ---- Bölge modu ----

    private static InteractionGradeResult GradeRegions(PaintPlan plan, InteractionGradeInput input)
    {
        if (!TryReadFillsSolution(input.Solution, plan, out var targets))
            return InteractionGradeResult.Ungradable;
        var fills = StudentFills(plan, input.Payload);
        if (plan.IsGrouped)
        {
            var groupRows = plan.Groups.Where(g => targets.ContainsKey(g.Id))
                .Select(g => (g.Id, fills.TryGetValue(g.Id, out var paint) && string.Equals(paint, targets[g.Id], StringComparison.Ordinal)))
                .ToList();
            return InteractionGradeResult.Of(groupRows.Count(r => r.Item2), groupRows.Count, WriteFeedback(groupRows));
        }
        var rows = new List<(string Id, bool Correct, string? Reason)>();
        var found = 0;
        var extra = 0;
        foreach (var region in plan.Regions)
        {
            if (!fills.TryGetValue(region.Id, out var paint))
                continue;
            if (!targets.TryGetValue(region.Id, out var expected))
            {
                extra++;
                rows.Add((region.Id, false, ReasonExtra));
            }
            else if (string.Equals(paint, expected, StringComparison.Ordinal))
            {
                found++;
                rows.Add((region.Id, true, null));
            }
            else
            {
                rows.Add((region.Id, false, ReasonColor));
            }
        }
        var tooMany = extra > Math.Max(TextSelectGrader.TooManyExtraMin, targets.Count);
        if (tooMany)
            rows.Clear();
        return InteractionGradeResult.Of(found, targets.Count + extra,
            WriteRegionsFeedback(rows, found, targets.Count - found, extra, tooMany));
    }

    private static IReadOnlyList<InteractionItemResponse> AnalyzeRegions(PaintPlan plan, InteractionGradeInput input)
    {
        if (!TryReadFillsSolution(input.Solution, plan, out var targets))
            return [];
        var fills = StudentFills(plan, input.Payload);
        var palette = plan.Palette.ToDictionary(p => p.Id, StringComparer.Ordinal);
        // Numaralı boyamada öğe = grup (soru), değilse hedef bölge.
        var items = plan.IsGrouped
            ? plan.Groups.Select(g => (g.Id, g.Label))
            : plan.Regions.Select(r => (r.Id, r.Label));
        return items.Select((item, index) => (item.Id, item.Label, index)).Where(x => targets.ContainsKey(x.Id)).Select(x =>
        {
            var label = InteractionAnalysis.TextLabel(string.IsNullOrWhiteSpace(x.Label)
                ? (x.index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture)
                : x.Label);
            if (!fills.TryGetValue(x.Id, out var paint) || !palette.TryGetValue(paint, out var entry))
                return new InteractionItemResponse(x.Id, label, false, null, null);
            return new InteractionItemResponse(x.Id, label, string.Equals(paint, targets[x.Id], StringComparison.Ordinal),
                paint, InteractionAnalysis.TextLabel(PaintName(entry)));
        }).ToList();
    }

    /// <summary>Öğrencinin boyadığı bölgeler (bölge id'si → palet id'si); payload bozuksa boş.</summary>
    private static Dictionary<string, string> StudentFills(PaintPlan plan, string? payload)
        => payload != null && TryParseFills(payload, plan, out var fills) ? fills : new Dictionary<string, string>(StringComparer.Ordinal);

    /// <summary>
    /// Katı bölge payload'u: <c>fills</c> nesnesi; plan verilmişse anahtarlar plandaki bölgeler (gruplu planda gruplar), değerler
    /// plandaki renkler. Plan
    /// verilmezse (Normalize) yalnız biçim: en çok <see cref="MaxRegions"/> geçerli id → geçerli id.
    /// </summary>
    private static bool TryParseFills(string payload, PaintPlan? plan, out Dictionary<string, string> fills)
    {
        fills = new Dictionary<string, string>(StringComparer.Ordinal);
        try
        {
            using var doc = JsonDocument.Parse(payload, StrictOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasOnlyKeys(root, ["version", "type", "fills"]))
                return false;
            if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                || !string.Equals(type.GetString(), Type, StringComparison.Ordinal))
                return false;
            if (root.TryGetProperty("version", out var version)
                && (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1))
                return false;
            if (!root.TryGetProperty("fills", out var element) || element.ValueKind != JsonValueKind.Object)
                return false;
            var regionIds = plan?.FillKeys();
            var paletteIds = plan?.Palette.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var entry in element.EnumerateObject())
            {
                if (entry.Value.ValueKind != JsonValueKind.String || entry.Value.GetString() is not { } paint
                    || !InteractionIds.IsValid(entry.Name) || !InteractionIds.IsValid(paint)
                    || (regionIds != null && !regionIds.Contains(entry.Name)) || (paletteIds != null && !paletteIds.Contains(paint))
                    || !fills.TryAdd(entry.Name, paint) || fills.Count > MaxRegions)
                {
                    fills.Clear();
                    return false;
                }
            }
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            fills.Clear();
            return false;
        }
    }

    /// <summary>Çözüm: hedef bölge (gruplu planda grup) → renk. Toleranslı: plana uymayan atlanır; okunamaz ya da boşsa false.</summary>
    private static bool TryReadFillsSolution(string? solution, PaintPlan plan, out Dictionary<string, string> targets)
    {
        targets = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(solution))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(solution, ReadOptions);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("fills", out var element)
                || element.ValueKind != JsonValueKind.Object)
                return false;
            var regionIds = plan.FillKeys();
            var paletteIds = plan.Palette.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var entry in element.EnumerateObject())
            {
                if (regionIds.Contains(entry.Name) && entry.Value.ValueKind == JsonValueKind.String
                    && entry.Value.GetString() is { } paint && paletteIds.Contains(paint))
                    targets[entry.Name] = paint;
            }
            return targets.Count > 0;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            targets.Clear();
            return false;
        }
    }

    /// <summary>Bölge: 3..64 köşe <c>[x,y]</c>, koordinatlar sonlu ve 0..<see cref="MaxCoordinate"/>, çokgen alanı ≥ 1.</summary>
    private static bool IsValidPolygon(JsonElement points)
    {
        if (points.ValueKind != JsonValueKind.Array || points.GetArrayLength() is < MinPoints or > MaxPoints)
            return false;
        var xs = new List<double>();
        var ys = new List<double>();
        foreach (var point in points.EnumerateArray())
        {
            if (point.ValueKind != JsonValueKind.Array || point.GetArrayLength() != 2)
                return false;
            var x = point[0];
            var y = point[1];
            if (x.ValueKind != JsonValueKind.Number || y.ValueKind != JsonValueKind.Number || !x.TryGetDouble(out var px)
                || !y.TryGetDouble(out var py) || !double.IsFinite(px) || !double.IsFinite(py)
                || px is < 0 or > MaxCoordinate || py is < 0 or > MaxCoordinate)
                return false;
            xs.Add(px);
            ys.Add(py);
        }
        var twiceArea = 0.0;
        for (var i = 0; i < xs.Count; i++)
        {
            var j = (i + 1) % xs.Count;
            twiceArea += xs[i] * ys[j] - xs[j] * ys[i];
        }
        return Math.Abs(twiceArea) / 2 >= 1;
    }

    // ---- Okuma ----

    private static bool TryReadPlan(string? plan, JsonDocumentOptions options, bool strict, out PaintPlan model)
    {
        model = new PaintPlan(GridMode, 0, 0, new List<Paint>(), new List<Area>(), new List<Group>());
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
            if (!TryReadString(root, "mode", out var mode) || (mode != GridMode && mode != RegionsMode && mode != PieMode))
                return false;
            var regionsMode = mode == RegionsMode;
            var pieMode = mode == PieMode;
            if (strict)
            {
                if (!HasOnlyKeys(root, regionsMode ? RegionsRootKeys : pieMode ? PieRootKeys : RootKeys))
                    return false;
                if (root.TryGetProperty("version", out var version)
                    && (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1))
                    return false;
            }
            int rows = 0, cols = 0;
            // Daire grafiği tek satırlık ızgaradır: dilim = hücre (payload tek satır, saat yönünde tepeden).
            if (pieMode)
            {
                if (!TryReadInt(root, "slices", out cols) || cols is < MinSlices or > MaxSlices)
                    return false;
                rows = 1;
            }
            else if (!regionsMode && (!TryReadInt(root, "rows", out rows) || !TryReadInt(root, "cols", out cols)
                || rows is < MinSize or > MaxSize || cols is < MinSize or > MaxSize))
                return false;
            if (regionsMode && strict && root.TryGetProperty("style", out var style)
                && (style.ValueKind != JsonValueKind.String || (style.GetString() != StyleFill && style.GetString() != StyleCircle)))
                return false;

            if (!root.TryGetProperty("palette", out var paletteElement) || paletteElement.ValueKind != JsonValueKind.Array)
                return false;
            var palette = new List<Paint>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var colors = new HashSet<string>(StringComparer.Ordinal);
            foreach (var raw in paletteElement.EnumerateArray())
            {
                if (raw.ValueKind != JsonValueKind.Object || (strict && !HasOnlyKeys(raw, PaletteKeys))
                    || !TryReadString(raw, "id", out var id) || !InteractionIds.IsValid(id) || !ids.Add(id)
                    || !TryReadString(raw, "color", out var color) || !Colors.Contains(color) || !colors.Add(color)
                    || !TryReadLabel(raw, strict, out var label))
                {
                    if (strict) return false;
                    continue;
                }
                palette.Add(new Paint(id, color, label));
            }
            // Payload palet sırasını tek karakterle yazar: okumada da en çok 8 renk.
            if (palette.Count is 0 or > MaxPalette)
                return false;

            var regions = new List<Area>();
            var groups = new List<Group>();
            if (regionsMode)
            {
                if (root.TryGetProperty("groups", out var groupsElement))
                {
                    if (groupsElement.ValueKind != JsonValueKind.Array)
                    {
                        if (strict) return false;
                    }
                    else
                    {
                        foreach (var raw in groupsElement.EnumerateArray())
                        {
                            if (raw.ValueKind != JsonValueKind.Object || (strict && !HasOnlyKeys(raw, GroupKeys))
                                || !TryReadString(raw, "id", out var id) || !InteractionIds.IsValid(id) || !ids.Add(id)
                                || !TryReadLabel(raw, strict, out var label))
                            {
                                if (strict) return false;
                                continue;
                            }
                            groups.Add(new Group(id, label));
                        }
                        if (groups.Count > MaxGroups || (strict && groups.Count == 0))
                            return false;
                    }
                }
                var groupIds = groups.Select(g => g.Id).ToHashSet(StringComparer.Ordinal);
                if (!root.TryGetProperty("regions", out var regionsElement) || regionsElement.ValueKind != JsonValueKind.Array)
                    return false;
                foreach (var raw in regionsElement.EnumerateArray())
                {
                    if (raw.ValueKind != JsonValueKind.Object || (strict && !HasOnlyKeys(raw, RegionKeys))
                        || !TryReadString(raw, "id", out var id) || !InteractionIds.IsValid(id) || !ids.Add(id)
                        || !raw.TryGetProperty("points", out var points) || !IsValidPolygon(points)
                        || !TryReadLabel(raw, strict, out var label) || !TryReadRegionGroup(raw, groupIds, strict, out var group))
                    {
                        if (strict) return false;
                        continue;
                    }
                    regions.Add(new Area(id, label, group));
                }
                if (regions.Count is 0 or > MaxRegions)
                    return false;
                // Her grubun (sorunun) boyanacak en az bir bölgesi olur; okumada bölgesiz grup atlanır.
                var used = regions.Select(r => r.Group).OfType<string>().ToHashSet(StringComparer.Ordinal);
                if (strict && groups.Any(g => !used.Contains(g.Id)))
                    return false;
                groups.RemoveAll(g => !used.Contains(g.Id));
            }
            model = new PaintPlan(mode, rows, cols, palette, regions, groups);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Bölgenin grubu: grup varken plandaki bir grup (zorunlu), yokken alan yasak. Okumada gruplu planda grupsuz / bilinmeyen gruplu bölge
    /// atlanır; grupsuz planda alan yok sayılır.
    /// </summary>
    private static bool TryReadRegionGroup(JsonElement raw, IReadOnlySet<string> groupIds, bool strict, out string? group)
    {
        group = null;
        var present = raw.TryGetProperty("group", out var value) && value.ValueKind != JsonValueKind.Null;
        if (groupIds.Count == 0)
            return !strict || !present;
        if (!present || value.ValueKind != JsonValueKind.String || !groupIds.Contains(value.GetString() ?? string.Empty))
            return false;
        group = value.GetString();
        return true;
    }

    /// <summary>Etiket isteğe bağlı; varsa düz metin, sınır içinde (kayıtta). Okumada eksik / bozuksa boş.</summary>
    private static bool TryReadLabel(JsonElement element, bool strict, out string label)
    {
        label = string.Empty;
        if (!element.TryGetProperty("label", out var value) || value.ValueKind == JsonValueKind.Null)
            return true;
        if (value.ValueKind != JsonValueKind.String)
            return !strict;
        label = value.GetString() ?? string.Empty;
        return !strict || (label.Length <= MaxLabelLength && !PlainTextAttribute.ContainsControlCharacters(label));
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
    /// Katı payload: <c>grid</c> satır dizisi; plan verilmişse satır sayısı / uzunluk planla aynı ve karakterler palet sırasında. Plan
    /// verilmezse (Normalize) yalnız biçim: en çok 20 satır × 20 karakter, <c>.</c> ya da <c>1</c>..<c>8</c>.
    /// </summary>
    private static bool TryParsePayload(string payload, PaintPlan? grid, out List<string> cells)
    {
        cells = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(payload, StrictOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasOnlyKeys(root, ["version", "type", "grid"]))
                return false;
            if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                || !string.Equals(type.GetString(), Type, StringComparison.Ordinal))
                return false;
            if (root.TryGetProperty("version", out var version)
                && (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1))
                return false;
            if (!root.TryGetProperty("grid", out var array) || array.ValueKind != JsonValueKind.Array
                || array.GetArrayLength() > MaxSize || (grid != null && array.GetArrayLength() != grid.Rows))
                return false;
            var maxPaint = (char)('0' + (grid?.Palette.Count ?? MaxPalette));
            foreach (var item in array.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String || item.GetString() is not { } row || row.Length > MaxSize
                    || (grid != null && row.Length != grid.Cols)
                    || row.Any(c => c != Empty && (c < '1' || c > maxPaint)))
                    return false;
                cells.Add(row);
            }
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            cells.Clear();
            return false;
        }
    }

    /// <summary>Çözüm: palet id'si → hücre sayısı. Toleranslı: plana uymayan / negatif atlanır; okunamaz ya da boşsa false.</summary>
    private static bool TryReadSolution(string? solution, PaintPlan grid, out Dictionary<string, int> counts)
    {
        counts = new Dictionary<string, int>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(solution))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(solution, ReadOptions);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("counts", out var element)
                || element.ValueKind != JsonValueKind.Object)
                return false;
            var ids = grid.Palette.Select(p => p.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var entry in element.EnumerateObject())
            {
                if (ids.Contains(entry.Name) && entry.Value.ValueKind == JsonValueKind.Number && entry.Value.TryGetInt32(out var count)
                    && count >= 0)
                    counts[entry.Name] = count;
            }
            return counts.Count > 0;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            counts.Clear();
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
    private static string WriteRegionsFeedback(
        IReadOnlyList<(string Id, bool Correct, string? Reason)> rows, int found, int missing, int extra, bool tooMany)
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
            writer.WriteNumber("found", found);
            writer.WriteNumber("missing", missing);
            writer.WriteNumber("extra", extra);
            if (tooMany)
                writer.WriteBoolean("tooMany", true);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
