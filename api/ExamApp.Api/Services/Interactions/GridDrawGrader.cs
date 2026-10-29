using System.IO;
using System.Text;
using System.Text.Json;

namespace ExamApp.Api.Services.Interactions;

/// <summary>
/// issue #588 (dilim c): <c>gridDraw</c> kontrolcüsü — ızgara tuvali ailesinin (bileşen F) kareli / noktalı zeminde kural tabanlı çizim
/// dilimi ("çevre uzunluğu 14 cm olan dikdörtgenleri kareli zemine çiziniz", "alanı 20 m² olan bütün dikdörtgenler").
/// <list type="bullet">
/// <item>Plan v1: <c>{version:1,type:"gridDraw",rows,cols,grid?,shape?}</c> — zemin <c>rows</c>×<c>cols</c> birim kare (2..30),
/// <c>grid</c> <c>square</c> (kareli, varsayılan) | <c>dot</c> (noktalı) yalnız görünüm, <c>shape</c> <c>rectangle</c> (varsayılan) |
/// <c>square</c>. Kural planda yok: yönergede yazar, çözümde saklanır.</item>
/// <item>Çözüm: <c>{"area"?:20,"perimeter"?:18,"all":true}</c> — en az biri; geçerli boyut = kenarları birim cinsinden doğal sayı,
/// koşulları sağlayan ve zemine (döndürülerek de) sığan dikdörtgen (<c>shape:"square"</c>'de kare). <c>all</c>: bütün geçerli boyutlar
/// çizilmeli (en çok 12), değilse biri yeter. Döndürülmüş dikdörtgen aynı boyut sayılır; yer serbest (çoklu çözüm).</item>
/// <item>Öğrenci payload'u: <c>{version:1,type:"gridDraw",shapes:[{row,col,width,height}]}</c> — zemine sığan, en çok 30 dikdörtgen.
/// Boş <c>shapes</c> = cevap yok.</item>
/// </list>
/// Kontrol <c>textSelect</c> kalıbında: gereken = <c>all</c> ? geçerli boyut sayısı : 1; bulunan = çizilen farklı geçerli boyut sayısı
/// (en çok gereken), fazla = kurala uymayan çizim. <c>CorrectCount</c> = bulunan, <c>TotalCount</c> = gereken + fazla. Aynı boyutun ikinci
/// çizimi ceza değil (<c>duplicate</c>). Geri bildirim <c>{"shapes":["ok"|"wrong"|"duplicate"],"found","missing","extra"}</c>
/// (çizim sırasıyla); eksik boyutlar yazılmaz.
/// </summary>
public sealed class GridDrawGrader : IInteractionGrader
{
    public const string Type = "gridDraw";

    public const int MinSize = 2;
    public const int MaxSize = 30;
    public const int MaxShapes = 30;
    public const int MaxTargets = 12;

    private static readonly HashSet<string> RootKeys = new(StringComparer.Ordinal) { "version", "type", "rows", "cols", "grid", "shape" };
    private static readonly HashSet<string> SolutionKeys = new(StringComparer.Ordinal) { "area", "perimeter", "all" };
    private static readonly HashSet<string> ShapeKeys = new(StringComparer.Ordinal) { "row", "col", "width", "height" };

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
        return TryReadPlan(plan, ReadOptions, strict: false, out var board) && TryParsePayload(payload, board, out _);
    }

    /// <summary>Çizimsiz cevap = cevap yok → null.</summary>
    public string? Normalize(string? payload)
    {
        if (payload == null || !TryParsePayload(payload, null, out var shapes))
            return payload;
        return shapes.Count == 0 ? null : payload;
    }

    public InteractionGradeResult Grade(InteractionGradeInput input)
    {
        if (!TryReadPlan(input.Plan, ReadOptions, strict: false, out var board) || !TryReadSolution(input.Solution, board, strict: false, out var rule))
            return InteractionGradeResult.Ungradable;
        var targets = Targets(board, rule);
        if (targets.Count == 0)
            return InteractionGradeResult.Ungradable;
        var required = rule.All ? targets.Count : 1;
        var shapes = Shapes(board, input.Payload);

        var statuses = new List<string>(shapes.Count);
        var found = new HashSet<Size>();
        var extra = 0;
        foreach (var shape in shapes)
        {
            var size = Size.Of(shape.Width, shape.Height);
            if (!targets.Contains(size))
            {
                statuses.Add("wrong");
                extra++;
            }
            else
            {
                statuses.Add(found.Add(size) ? "ok" : "duplicate");
            }
        }
        var foundCount = Math.Min(found.Count, required);
        return InteractionGradeResult.Of(foundCount, required + extra,
            WriteFeedback(statuses, foundCount, required - foundCount, extra));
    }

    /// <summary>
    /// issue #584: <c>all</c>'da öğe = geçerli boyut ("3 × 4"), doğru ⇔ çizilmiş (cevap = çizildi). Tek çizimde tek öğe; cevap = ilk
    /// geçerli çizimin boyutu, yoksa ilk çizimin boyutu (yanlış).
    /// </summary>
    public IReadOnlyList<InteractionItemResponse> Analyze(InteractionGradeInput input)
    {
        if (!TryReadPlan(input.Plan, ReadOptions, strict: false, out var board) || !TryReadSolution(input.Solution, board, strict: false, out var rule))
            return [];
        var targets = Targets(board, rule);
        if (targets.Count == 0)
            return [];
        var drawn = Shapes(board, input.Payload).Select(s => Size.Of(s.Width, s.Height)).ToList();
        if (rule.All)
        {
            var set = drawn.ToHashSet();
            return targets.OrderBy(t => t.Short).ThenBy(t => t.Long).Select(t =>
            {
                var hit = set.Contains(t);
                return new InteractionItemResponse(t.Key, t.Label, hit, hit ? t.Key : null, hit ? t.Label : null);
            }).ToList();
        }
        var answer = drawn.Where(targets.Contains).Cast<Size?>().FirstOrDefault() ?? drawn.Cast<Size?>().FirstOrDefault();
        return [new InteractionItemResponse("shape", "1", answer is { } a && targets.Contains(a), answer?.Key, answer?.Label)];
    }

    /// <summary>
    /// Kayıt kuralı (issue #588): zemin 2..30 × 2..30, <c>grid</c>/<c>shape</c> izinli değer. Çözüm boşsa geçerli (taslak); doluysa
    /// <c>area</c> (1..900) ve/veya <c>perimeter</c> (4..240) tam sayı, <c>all</c> bool, en az bir geçerli boyut zemine sığar ve
    /// <c>all</c>'da en çok 12 boyut.
    /// </summary>
    public bool ValidateDefinition(string? plan, string? solution)
    {
        if (!TryReadPlan(plan, DefinitionOptions, strict: true, out var board))
            return false;
        if (string.IsNullOrWhiteSpace(solution))
            return true;
        if (!TryReadSolution(solution, board, strict: true, out var rule))
            return false;
        var targets = Targets(board, rule);
        return targets.Count > 0 && (!rule.All || targets.Count <= MaxTargets);
    }

    // ---- Model ----

    private sealed record Board(int Rows, int Cols, bool SquareOnly);

    private sealed record Rule(int? Area, int? Perimeter, bool All);

    private sealed record Shape(int Row, int Col, int Width, int Height);

    /// <summary>Döndürmeden bağımsız boyut: kısa ≤ uzun kenar.</summary>
    private readonly record struct Size(int Short, int Long)
    {
        public static Size Of(int a, int b) => a <= b ? new Size(a, b) : new Size(b, a);
        public string Key => $"s{Short}x{Long}";
        public string Label => $"{Short} × {Long}";
    }

    /// <summary>Kurala uyan ve zemine (döndürülerek de) sığan bütün boyutlar.</summary>
    private static HashSet<Size> Targets(Board board, Rule rule)
    {
        var targets = new HashSet<Size>();
        var limit = Math.Max(board.Rows, board.Cols);
        for (var a = 1; a <= limit; a++)
        {
            for (var b = a; b <= limit; b++)
            {
                if (board.SquareOnly && a != b)
                    continue;
                if (rule.Area is { } area && a * b != area)
                    continue;
                if (rule.Perimeter is { } perimeter && 2 * (a + b) != perimeter)
                    continue;
                if ((a <= board.Cols && b <= board.Rows) || (b <= board.Cols && a <= board.Rows))
                    targets.Add(new Size(a, b));
            }
        }
        return targets;
    }

    private static List<Shape> Shapes(Board board, string? payload)
        => payload != null && TryParsePayload(payload, board, out var shapes) ? shapes : [];

    // ---- Okuma ----

    private static bool TryReadPlan(string? plan, JsonDocumentOptions options, bool strict, out Board board)
    {
        board = new Board(0, 0, false);
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
                if (root.TryGetProperty("grid", out var grid)
                    && (grid.ValueKind != JsonValueKind.String || grid.GetString() is not ("square" or "dot")))
                    return false;
            }
            var squareOnly = false;
            if (root.TryGetProperty("shape", out var shape) && shape.ValueKind != JsonValueKind.Null)
            {
                if (shape.ValueKind != JsonValueKind.String || shape.GetString() is not ("rectangle" or "square"))
                    return false;
                squareOnly = shape.GetString() == "square";
            }
            if (!TryReadInt(root, "rows", out var rows) || rows is < MinSize or > MaxSize
                || !TryReadInt(root, "cols", out var cols) || cols is < MinSize or > MaxSize)
                return false;
            board = new Board(rows, cols, squareOnly);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryReadSolution(string? solution, Board board, bool strict, out Rule rule)
    {
        rule = new Rule(null, null, false);
        if (string.IsNullOrWhiteSpace(solution))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(solution, strict ? DefinitionOptions : ReadOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || (strict && !HasOnlyKeys(root, SolutionKeys)))
                return false;
            if (!TryReadOptionalInt(root, "area", 1, board.Rows * board.Cols, out var area)
                || !TryReadOptionalInt(root, "perimeter", 4, 2 * (board.Rows + board.Cols), out var perimeter))
                return false;
            if (area == null && perimeter == null)
                return false;
            var all = false;
            if (root.TryGetProperty("all", out var allElement) && allElement.ValueKind != JsonValueKind.Null)
            {
                if (allElement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                    return false;
                all = allElement.GetBoolean();
            }
            rule = new Rule(area, perimeter, all);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryReadOptionalInt(JsonElement root, string name, int min, int max, out int? value)
    {
        value = null;
        if (!root.TryGetProperty(name, out var element) || element.ValueKind == JsonValueKind.Null)
            return true;
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out var parsed) || parsed < min || parsed > max)
            return false;
        value = parsed;
        return true;
    }

    private static bool TryReadInt(JsonElement item, string name, out int value)
    {
        value = 0;
        return item.TryGetProperty(name, out var element) && element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out value);
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
    /// Katı payload: <c>shapes</c> dizisi, en çok 30 dikdörtgen <c>{row,col,width,height}</c> (tam sayı, kenar ≥ 1). Plan verilmişse
    /// dikdörtgen zemine sığar; verilmezse (Normalize) yalnız biçim, 0..30 aralığı.
    /// </summary>
    private static bool TryParsePayload(string payload, Board? board, out List<Shape> shapes)
    {
        shapes = [];
        try
        {
            using var doc = JsonDocument.Parse(payload, StrictOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasOnlyKeys(root, ["version", "type", "shapes"]))
                return false;
            if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                || !string.Equals(type.GetString(), Type, StringComparison.Ordinal))
                return false;
            if (root.TryGetProperty("version", out var version)
                && (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1))
                return false;
            if (!root.TryGetProperty("shapes", out var element) || element.ValueKind != JsonValueKind.Array
                || element.GetArrayLength() > MaxShapes)
                return false;
            var rows = board?.Rows ?? MaxSize;
            var cols = board?.Cols ?? MaxSize;
            foreach (var raw in element.EnumerateArray())
            {
                if (raw.ValueKind != JsonValueKind.Object || !HasOnlyKeys(raw, ShapeKeys)
                    || !TryReadInt(raw, "row", out var row) || !TryReadInt(raw, "col", out var col)
                    || !TryReadInt(raw, "width", out var width) || !TryReadInt(raw, "height", out var height)
                    || row < 0 || col < 0 || width < 1 || height < 1 || row + height > rows || col + width > cols)
                {
                    shapes.Clear();
                    return false;
                }
                shapes.Add(new Shape(row, col, width, height));
            }
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            shapes.Clear();
            return false;
        }
    }

    private static string WriteFeedback(IReadOnlyList<string> statuses, int found, int missing, int extra)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteStartArray("shapes");
            foreach (var status in statuses)
                writer.WriteStringValue(status);
            writer.WriteEndArray();
            writer.WriteNumber("found", found);
            writer.WriteNumber("missing", missing);
            writer.WriteNumber("extra", extra);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
