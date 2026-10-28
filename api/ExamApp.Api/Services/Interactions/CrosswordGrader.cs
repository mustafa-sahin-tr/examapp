using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using ExamApp.Api.Helpers;

namespace ExamApp.Api.Services.Interactions;

/// <summary>
/// issue #586: <c>crossword</c> kontrolcüsü — çengel bulmaca (bileşen G).
/// <list type="bullet">
/// <item>Plan v1: <c>{version:1,type:"crossword",rows,cols,clues:[{id,dir:"across"|"down",row,col,length,number?,text}]}</c>.
/// Beyaz hücreler ipuçlarının birleşimidir (ipucu dışı hücre siyah). <c>number</c> ipucunun ızgarada görünen numarası
/// (yoksa oynatıcı sırayla numaralar); <c>text</c> ipucu metni. Harfler planda YOK (#570).</item>
/// <item>Çözüm: <c>{"answers":{"k1":"KALP",...}}</c> — ipucu başına cevap (büyük harf, tr-TR); kesişen hücrelerde harfler aynı.</item>
/// <item>Öğrenci payload'u: <c>{version:1,type:"crossword",grid:["KA  ",...]}</c> — satır başına <c>cols</c> karakterlik metin, boş
/// hücre boşluk; siyah hücre yalnız boşluk.</item>
/// </list>
/// Kontrol: satır = ipucu (plan sırası), doğru ⇔ ipucunun bütün hücreleri cevapla aynı (tr-TR büyük harfe çevrilerek). Geri bildirim
/// <c>{"rows":[{id,correct}]}</c> hangi hücrenin yanlış olduğunu ve harfleri yazmaz. Çözümü olmayan soru puanlanamaz (0/0).
/// </summary>
public sealed class CrosswordGrader : IInteractionGrader
{
    public const string Type = "crossword";

    public const string Across = "across";
    public const string Down = "down";

    public const int MinSize = 2;
    public const int MaxSize = 20;
    public const int MaxClues = 80;
    public const int MinLength = 2;
    public const int MaxClueTextLength = 300;
    public const int MaxNumber = 999;

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly HashSet<string> RootKeys = new(StringComparer.Ordinal) { "version", "type", "rows", "cols", "clues" };
    private static readonly HashSet<string> ClueKeys = new(StringComparer.Ordinal) { "id", "dir", "row", "col", "length", "number", "text" };

    // Payload (istemci girdisi) katı: yinelenen anahtar yasak.
    private static readonly JsonDocumentOptions StrictOptions = new() { MaxDepth = 8, AllowDuplicateProperties = false };

    // Plan/çözüm (kayıtlı veri) okuması toleranslı: okuma istisna atmamalı.
    private static readonly JsonDocumentOptions ReadOptions = new() { MaxDepth = 256 };

    // Yazma yolu (ValidateDefinition) katı.
    private static readonly JsonDocumentOptions DefinitionOptions = new() { MaxDepth = 32, AllowDuplicateProperties = false };

    public string InteractionType => Type;

    public bool Validate(string? plan, string? payload)
    {
        if (payload == null)
            return true;
        if (payload.Length > InteractionPayload.MaxLength)
            return false;
        if (!TryReadPlan(plan, ReadOptions, strict: false, out var puzzle))
            return false;
        return TryParsePayload(payload, puzzle, out _);
    }

    /// <summary>Hiç harf yazılmamış ızgara = cevap yok → null.</summary>
    public string? Normalize(string? payload)
    {
        if (payload == null || !TryParseGrid(payload, out var grid))
            return payload;
        return grid.All(line => line.All(c => c == ' ')) ? null : payload;
    }

    public InteractionGradeResult Grade(InteractionGradeInput input)
    {
        if (!TryReadPlan(input.Plan, ReadOptions, strict: false, out var puzzle) || puzzle.Clues.Count == 0
            || !TryReadAnswers(input.Solution, puzzle, out var answers))
            return InteractionGradeResult.Ungradable;
        var grid = ReadGrid(input.Payload, puzzle);
        var rows = puzzle.Clues.Select(clue => (clue.Id, IsSolved(clue, grid, answers))).ToList();
        return InteractionGradeResult.Of(rows.Count(r => r.Item2), rows.Count, WriteFeedback(rows));
    }

    /// <summary>issue #584: öğe = ipucu (plan sırası), cevap = öğrencinin yazdığı kelime (boş hücre <c>·</c>); hiç harf yoksa cevapsız.</summary>
    public IReadOnlyList<InteractionItemResponse> Analyze(InteractionGradeInput input)
    {
        if (!TryReadPlan(input.Plan, ReadOptions, strict: false, out var puzzle) || !TryReadAnswers(input.Solution, puzzle, out var answers))
            return [];
        var grid = ReadGrid(input.Payload, puzzle);
        return puzzle.Clues.Select((clue, i) =>
        {
            var word = Word(clue, grid);
            var blank = word.All(c => c == ' ');
            var shown = blank ? null : new string(word.Select(c => c == ' ' ? '·' : c).ToArray());
            return new InteractionItemResponse(clue.Id, ClueLabel(clue, i), IsSolved(clue, grid, answers), shown, shown);
        }).ToList();
    }

    /// <summary>
    /// Kayıt kuralı (issue #586): plan şemaya ve ızgara kurallarına uygun (bkz. <see cref="ValidateStructure"/>); çözüm boşsa geçerli
    /// (taslak). Doluysa her ipucunun cevabı var, uzunluğu tutar, yalnız harf/rakam ve kesişen hücrelerde harfler aynı.
    /// </summary>
    public bool ValidateDefinition(string? plan, string? solution)
    {
        if (!TryReadPlan(plan, DefinitionOptions, strict: true, out var puzzle))
            return false;
        if (string.IsNullOrWhiteSpace(solution))
            return true;

        try
        {
            using var doc = JsonDocument.Parse(solution, DefinitionOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasOnlyKeys(root, ["answers"])
                || !root.TryGetProperty("answers", out var answersElement) || answersElement.ValueKind != JsonValueKind.Object)
                return false;

            var answers = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in answersElement.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String || property.Value.GetString() is not { } word)
                    return false;
                answers[property.Name] = word;
            }
            if (answers.Count != puzzle.Clues.Count)
                return false;

            var letters = new Dictionary<(int, int), char>();
            foreach (var clue in puzzle.Clues)
            {
                if (!answers.TryGetValue(clue.Id, out var word) || word.Length != clue.Length || !word.All(IsCellChar))
                    return false;
                var cells = clue.Cells().ToList();
                for (var i = 0; i < cells.Count; i++)
                {
                    var letter = Upper(word[i]);
                    if (letters.TryGetValue(cells[i], out var existing) && existing != letter)
                        return false;
                    letters[cells[i]] = letter;
                }
            }
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Cevaplar tr-TR büyük harfe çevrilir (kontrol de büyük harfle karşılaştırır; saklanan biçim tek olsun).</summary>
    public (string? Plan, string? Solution) PrepareForStorage(string? plan, string? solution)
    {
        if (string.IsNullOrWhiteSpace(solution))
            return (plan, solution);
        try
        {
            using var doc = JsonDocument.Parse(solution, DefinitionOptions);
            if (!doc.RootElement.TryGetProperty("answers", out var answers) || answers.ValueKind != JsonValueKind.Object)
                return (plan, solution);
            var upper = new SortedDictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in answers.EnumerateObject())
            {
                if (property.Value.ValueKind != JsonValueKind.String)
                    return (plan, solution);
                upper[property.Name] = new string(property.Value.GetString()!.Select(Upper).ToArray());
            }
            return (plan, JsonSerializer.Serialize(new { answers = upper }));
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return (plan, solution);
        }
    }

    // ---- Model ----

    private sealed record Clue(string Id, string Dir, int Row, int Col, int Length, int? Number, string? Text)
    {
        public IEnumerable<(int Row, int Col)> Cells()
        {
            for (var i = 0; i < Length; i++)
                yield return Dir == Across ? (Row, Col + i) : (Row + i, Col);
        }
    }

    private sealed record Puzzle(int Rows, int Cols, List<Clue> Clues, HashSet<(int, int)> White);

    private static bool IsSolved(Clue clue, char[][] grid, Dictionary<string, string> answers)
    {
        if (!answers.TryGetValue(clue.Id, out var answer) || answer.Length != clue.Length)
            return false;
        var word = Word(clue, grid);
        for (var i = 0; i < word.Length; i++)
        {
            if (word[i] == ' ' || Upper(word[i]) != Upper(answer[i]))
                return false;
        }
        return true;
    }

    private static char[] Word(Clue clue, char[][] grid) => clue.Cells().Select(c => grid[c.Row][c.Col]).ToArray();

    private static char Upper(char c) => char.ToUpper(c, Turkish);

    private static bool IsCellChar(char c) => char.IsLetterOrDigit(c) && !char.IsSurrogate(c);

    // ---- Okuma ----

    /// <summary>
    /// Plan okuma. <paramref name="strict"/> false (okuma/kontrol): boyut + ızgaraya sığan ipuçları; bozuk ipucu atlanır. true (kayıt):
    /// tam şema ve <see cref="ValidateStructure"/>.
    /// </summary>
    private static bool TryReadPlan(string? plan, JsonDocumentOptions options, bool strict, out Puzzle puzzle)
    {
        puzzle = new Puzzle(0, 0, new List<Clue>(), new HashSet<(int, int)>());
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
            if (!TryReadInt(root, "rows", out var rows) || !TryReadInt(root, "cols", out var cols)
                || rows is < MinSize or > MaxSize || cols is < MinSize or > MaxSize)
                return false;
            if (!root.TryGetProperty("clues", out var cluesElement) || cluesElement.ValueKind != JsonValueKind.Array)
                return false;

            var clues = new List<Clue>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in cluesElement.EnumerateArray())
            {
                if (!TryReadClue(item, strict, rows, cols, out var clue) || !ids.Add(clue.Id))
                {
                    if (strict) return false;
                    continue;
                }
                clues.Add(clue);
            }

            var white = new HashSet<(int, int)>();
            foreach (var clue in clues)
                white.UnionWith(clue.Cells());
            puzzle = new Puzzle(rows, cols, clues, white);
            return !strict || ValidateStructure(puzzle);
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>
    /// Izgara kuralları (kayıt): 1..<see cref="MaxClues"/> ipucu; aynı yöndeki iki ipucu ne hücre paylaşır ne uç uca değer (tek kelime
    /// gibi görünürdü); aynı hücreden aynı yönde iki ipucu başlamaz; aynı başlangıç hücresindeki ipuçlarının numarası aynı.
    /// </summary>
    private static bool ValidateStructure(Puzzle puzzle)
    {
        if (puzzle.Clues.Count is 0 or > MaxClues)
            return false;
        foreach (var dir in new[] { Across, Down })
        {
            var owner = new Dictionary<(int, int), string>();
            foreach (var clue in puzzle.Clues.Where(c => c.Dir == dir))
            {
                foreach (var cell in clue.Cells())
                {
                    if (!owner.TryAdd(cell, clue.Id))
                        return false;
                }
            }
            foreach (var clue in puzzle.Clues.Where(c => c.Dir == dir))
            {
                var before = dir == Across ? (clue.Row, clue.Col - 1) : (clue.Row - 1, clue.Col);
                var after = dir == Across ? (clue.Row, clue.Col + clue.Length) : (clue.Row + clue.Length, clue.Col);
                if (owner.ContainsKey(before) || owner.ContainsKey(after))
                    return false;
            }
        }
        foreach (var group in puzzle.Clues.GroupBy(c => (c.Row, c.Col)))
        {
            if (group.Select(c => c.Number).Distinct().Count() > 1)
                return false;
        }
        return true;
    }

    private static bool TryReadClue(JsonElement item, bool strict, int rows, int cols, out Clue clue)
    {
        clue = new Clue(string.Empty, Across, 0, 0, 0, null, null);
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var idElement)
            || idElement.ValueKind != JsonValueKind.String || idElement.GetString() is not { } id || !InteractionIds.IsValid(id))
            return false;
        if (strict && !HasOnlyKeys(item, ClueKeys))
            return false;
        if (!item.TryGetProperty("dir", out var dirElement) || dirElement.ValueKind != JsonValueKind.String
            || dirElement.GetString() is not ({ } dir and (Across or Down)))
            return false;
        if (!TryReadInt(item, "row", out var row) || !TryReadInt(item, "col", out var col) || !TryReadInt(item, "length", out var length))
            return false;
        if (row < 0 || col < 0 || length < MinLength || row >= rows || col >= cols
            || (dir == Across ? col + length > cols : row + length > rows))
            return false;

        int? number = null;
        if (item.TryGetProperty("number", out var numberElement) && numberElement.ValueKind != JsonValueKind.Null)
        {
            if (numberElement.ValueKind == JsonValueKind.Number && numberElement.TryGetInt32(out var n) && n is >= 1 and <= MaxNumber)
                number = n;
            else if (strict)
                return false;
        }

        string? text = null;
        if (item.TryGetProperty("text", out var textElement) && textElement.ValueKind == JsonValueKind.String)
            text = textElement.GetString();
        // Kayıtta ipucu metni zorunlu, düz metin.
        if (strict && (string.IsNullOrWhiteSpace(text) || text.Length > MaxClueTextLength || PlainTextAttribute.ContainsControlCharacters(text)))
            return false;

        clue = new Clue(id, dir, row, col, length, number, text);
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

    /// <summary>Katı payload ayrıştırma: <see cref="TryParseGrid"/> + boyut plana uyar, siyah hücrede harf yok.</summary>
    private static bool TryParsePayload(string payload, Puzzle puzzle, out string[] grid)
    {
        if (!TryParseGrid(payload, out grid) || grid.Length != puzzle.Rows)
            return false;
        for (var r = 0; r < grid.Length; r++)
        {
            if (grid[r].Length != puzzle.Cols)
                return false;
            for (var c = 0; c < grid[r].Length; c++)
            {
                if (grid[r][c] != ' ' && !puzzle.White.Contains((r, c)))
                    return false;
            }
        }
        return true;
    }

    /// <summary>Nesne, <c>type</c> = <see cref="Type"/>, varsa <c>version</c> = 1, <c>grid</c> = en çok <see cref="MaxSize"/> satırlık metin dizisi; her karakter boşluk ya da harf/rakam.</summary>
    private static bool TryParseGrid(string payload, out string[] grid)
    {
        grid = [];
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
            if (!root.TryGetProperty("grid", out var gridElement) || gridElement.ValueKind != JsonValueKind.Array
                || gridElement.GetArrayLength() > MaxSize)
                return false;
            var lines = new List<string>();
            foreach (var line in gridElement.EnumerateArray())
            {
                if (line.ValueKind != JsonValueKind.String || line.GetString() is not { } text || text.Length > MaxSize
                    || !text.All(c => c == ' ' || IsCellChar(c)))
                    return false;
                lines.Add(text);
            }
            grid = lines.ToArray();
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Kontrol için ızgara: plan boyutunda, boş hücre boşluk. Geçersiz / boş payload = boş ızgara.</summary>
    private static char[][] ReadGrid(string? payload, Puzzle puzzle)
    {
        var grid = Enumerable.Range(0, puzzle.Rows).Select(_ => Enumerable.Repeat(' ', puzzle.Cols).ToArray()).ToArray();
        if (payload == null || !TryParsePayload(payload, puzzle, out var lines))
            return grid;
        for (var r = 0; r < puzzle.Rows; r++)
        {
            for (var c = 0; c < puzzle.Cols; c++)
                grid[r][c] = lines[r][c];
        }
        return grid;
    }

    /// <summary>Çözüm: ipucu → cevap. Toleranslı: plana uymayan cevap atlanır; okunamaz / boşsa false (puanlanamaz).</summary>
    private static bool TryReadAnswers(string? solution, Puzzle puzzle, out Dictionary<string, string> answers)
    {
        answers = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(solution))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(solution, ReadOptions);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("answers", out var element)
                || element.ValueKind != JsonValueKind.Object)
                return false;
            var lengths = puzzle.Clues.ToDictionary(c => c.Id, c => c.Length, StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.String && property.Value.GetString() is { } word
                    && lengths.TryGetValue(property.Name, out var length) && word.Length == length)
                    answers[property.Name] = word;
            }
            return answers.Count > 0;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            answers.Clear();
            return false;
        }
    }

    private static string ClueLabel(Clue clue, int index)
    {
        var arrow = clue.Dir == Across ? "→" : "↓";
        var prefix = (clue.Number?.ToString(CultureInfo.InvariantCulture) ?? "#" + (index + 1)) + " " + arrow;
        return string.IsNullOrWhiteSpace(clue.Text) ? prefix : prefix + " " + InteractionAnalysis.TextLabel(clue.Text);
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
