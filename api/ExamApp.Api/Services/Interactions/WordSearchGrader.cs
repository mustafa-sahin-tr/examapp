using System.Globalization;
using System.IO;
using System.Text;
using System.Text.Json;
using ExamApp.Api.Helpers;

namespace ExamApp.Api.Services.Interactions;

/// <summary>
/// issue #586: <c>wordSearch</c> kontrolcüsü — kelime avı (bileşen G).
/// <list type="bullet">
/// <item>Plan v1: <c>{version:1,type:"wordSearch",rows,cols,letters:["KALP",...],words:[{id,label}]}</c>. Harf ızgarası öğrenciye
/// gider (oyunun kendisi); <c>label</c> listede görünen metin (kelimenin kendisi ya da ipucu).</item>
/// <item>Çözüm: <c>{"lines":{"w1":{"from":[r,c],"to":[r,c]}}}</c> — kelimenin ızgaradaki düz çizgisi (yatay, dikey, çapraz; iki
/// yönde de okunabilir).</item>
/// <item>Öğrenci payload'u: <c>{version:1,type:"wordSearch",lines:[{"from":[r,c],"to":[r,c]},...]}</c> — işaretlediği düz çizgiler
/// (kelimeye bağlı değil, sırasız).</item>
/// </list>
/// Kontrol: satır = kelime (plan sırası), doğru ⇔ öğrencinin çizgilerinden birinin harfleri kelimenin harfleriyle (çözüm çizgisinden
/// okunan, tr-TR büyük harf) iki yönden biriyle aynı — kelime ızgarada başka yerde de geçiyorsa orası da sayılır. Geri bildirim
/// <c>{"rows":[{id,correct}],"lines":[bool,...]}</c>: kelime başına bulundu mu ve öğrencinin her çizgisi bir kelime mi; çözüm
/// çizgileri yazılmaz. Çözümü olmayan soru puanlanamaz (0/0).
/// </summary>
public sealed class WordSearchGrader : IInteractionGrader
{
    public const string Type = "wordSearch";

    public const int MinSize = 4;
    public const int MaxSize = 20;
    public const int MaxWords = 40;
    public const int MinWordLength = 2;
    public const int MaxLabelLength = 100;

    /// <summary>Öğrencinin işaretleyebileceği en çok çizgi (kelime sayısının iki katı, en az 10).</summary>
    public static int MaxLines(int words) => Math.Max(10, 2 * words);

    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");

    private static readonly HashSet<string> RootKeys = new(StringComparer.Ordinal) { "version", "type", "rows", "cols", "letters", "words" };
    private static readonly HashSet<string> WordKeys = new(StringComparer.Ordinal) { "id", "label" };

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
        if (!TryReadPlan(plan, ReadOptions, strict: false, out var puzzle))
            return false;
        return TryParsePayload(payload, out var lines) && lines.Count <= MaxLines(puzzle.Words.Count)
            && lines.All(l => l.InGrid(puzzle.Rows, puzzle.Cols));
    }

    /// <summary>Çizgisiz payload = cevap yok → null.</summary>
    public string? Normalize(string? payload)
        => payload != null && TryParsePayload(payload, out var lines) && lines.Count == 0 ? null : payload;

    public InteractionGradeResult Grade(InteractionGradeInput input)
    {
        if (!TryReadPlan(input.Plan, ReadOptions, strict: false, out var puzzle) || puzzle.Words.Count == 0
            || !TryReadSolution(input.Solution, puzzle, out var targets))
            return InteractionGradeResult.Ungradable;
        var lines = ReadLines(input.Payload, puzzle);
        var spelled = lines.Select(l => Spell(puzzle, l)).ToList();
        var rows = puzzle.Words.Select(w => (w.Id, Found(targets, w.Id, spelled))).ToList();
        var lineHits = spelled.Select(text => targets.Values.Any(t => Matches(t, text))).ToList();
        return InteractionGradeResult.Of(rows.Count(r => r.Item2), rows.Count, WriteFeedback(rows, lineHits));
    }

    /// <summary>issue #584: öğe = kelime, cevap = bulundu (<c>found</c>) ya da cevapsız.</summary>
    public IReadOnlyList<InteractionItemResponse> Analyze(InteractionGradeInput input)
    {
        if (!TryReadPlan(input.Plan, ReadOptions, strict: false, out var puzzle) || !TryReadSolution(input.Solution, puzzle, out var targets))
            return [];
        var spelled = ReadLines(input.Payload, puzzle).Select(l => Spell(puzzle, l)).ToList();
        return puzzle.Words.Select((w, i) =>
        {
            var found = Found(targets, w.Id, spelled);
            return new InteractionItemResponse(w.Id,
                string.IsNullOrWhiteSpace(w.Label) ? "#" + (i + 1) : InteractionAnalysis.TextLabel(w.Label),
                found, found ? "found" : null, found ? "✓" : null);
        }).ToList();
    }

    /// <summary>
    /// Kayıt kuralı (issue #586): ızgara 4..20, her hücre harf/rakam; 1..40 kelime, etiketi zorunlu düz metin. Çözüm boşsa geçerli
    /// (taslak); doluysa her kelimenin düz, ızgara içi, en az iki hücrelik çizgisi var ve iki kelime aynı çizgiyi paylaşmaz.
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
            if (root.ValueKind != JsonValueKind.Object || !HasOnlyKeys(root, ["lines"])
                || !root.TryGetProperty("lines", out var linesElement) || linesElement.ValueKind != JsonValueKind.Object)
                return false;
            var lines = new Dictionary<string, Line>(StringComparer.Ordinal);
            foreach (var property in linesElement.EnumerateObject())
            {
                if (!TryReadLine(property.Value, strict: true, out var line) || !line.InGrid(puzzle.Rows, puzzle.Cols))
                    return false;
                lines[property.Name] = line;
            }
            if (lines.Count != puzzle.Words.Count || puzzle.Words.Any(w => !lines.ContainsKey(w.Id)))
                return false;
            return lines.Values.Select(l => l.Canonical()).Distinct().Count() == lines.Count;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    // ---- Model ----

    /// <summary>Düz çizgi: başlangıç ve bitiş hücresi; yatay, dikey ya da 45° çapraz, en az iki hücre.</summary>
    private readonly record struct Line(int R1, int C1, int R2, int C2)
    {
        public int Length => Math.Max(Math.Abs(R2 - R1), Math.Abs(C2 - C1)) + 1;

        public bool IsStraight
        {
            get
            {
                int dr = Math.Abs(R2 - R1), dc = Math.Abs(C2 - C1);
                return (dr > 0 || dc > 0) && (dr == 0 || dc == 0 || dr == dc);
            }
        }

        public bool InGrid(int rows, int cols)
            => R1 >= 0 && R2 >= 0 && C1 >= 0 && C2 >= 0 && R1 < rows && R2 < rows && C1 < cols && C2 < cols;

        public IEnumerable<(int Row, int Col)> Cells()
        {
            int sr = Math.Sign(R2 - R1), sc = Math.Sign(C2 - C1);
            for (var i = 0; i < Length; i++)
                yield return (R1 + sr * i, C1 + sc * i);
        }

        /// <summary>Yönden bağımsız anahtar (aynı hücreler = aynı çizgi).</summary>
        public Line Canonical() => (R1, C1).CompareTo((R2, C2)) <= 0 ? this : new Line(R2, C2, R1, C1);
    }

    private sealed record Word(string Id, string? Label);

    private sealed record Puzzle(int Rows, int Cols, string[] Letters, List<Word> Words);

    private static string Spell(Puzzle puzzle, Line line)
        => new(line.Cells().Select(c => char.ToUpper(puzzle.Letters[c.Row][c.Col], Turkish)).ToArray());

    private static bool Matches(string target, string text)
        => text.Length == target.Length && (text == target || text.Reverse().SequenceEqual(target));

    private static bool Found(Dictionary<string, string> targets, string wordId, List<string> spelled)
        => targets.TryGetValue(wordId, out var target) && spelled.Any(text => Matches(target, text));

    private static bool IsCellChar(char c) => char.IsLetterOrDigit(c) && !char.IsSurrogate(c);

    // ---- Okuma ----

    private static bool TryReadPlan(string? plan, JsonDocumentOptions options, bool strict, out Puzzle puzzle)
    {
        puzzle = new Puzzle(0, 0, [], new List<Word>());
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

            // Izgara her durumda tam olmalı: eksik satır kontrolde hücre okumayı bozar.
            if (!root.TryGetProperty("letters", out var lettersElement) || lettersElement.ValueKind != JsonValueKind.Array
                || lettersElement.GetArrayLength() != rows)
                return false;
            var letters = new string[rows];
            var r = 0;
            foreach (var line in lettersElement.EnumerateArray())
            {
                if (line.ValueKind != JsonValueKind.String || line.GetString() is not { } text || text.Length != cols
                    || !text.All(IsCellChar))
                    return false;
                letters[r++] = text;
            }

            if (!root.TryGetProperty("words", out var wordsElement) || wordsElement.ValueKind != JsonValueKind.Array)
                return false;
            var words = new List<Word>();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var item in wordsElement.EnumerateArray())
            {
                if (!TryReadWord(item, strict, out var word) || !ids.Add(word.Id))
                {
                    if (strict) return false;
                    continue;
                }
                words.Add(word);
            }
            if (strict && words.Count is 0 or > MaxWords)
                return false;

            puzzle = new Puzzle(rows, cols, letters, words);
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            return false;
        }
    }

    private static bool TryReadWord(JsonElement item, bool strict, out Word word)
    {
        word = new Word(string.Empty, null);
        if (item.ValueKind != JsonValueKind.Object || !item.TryGetProperty("id", out var idElement)
            || idElement.ValueKind != JsonValueKind.String || idElement.GetString() is not { } id || !InteractionIds.IsValid(id))
            return false;
        if (strict && !HasOnlyKeys(item, WordKeys))
            return false;
        string? label = null;
        if (item.TryGetProperty("label", out var labelElement) && labelElement.ValueKind == JsonValueKind.String)
            label = labelElement.GetString();
        if (strict && (string.IsNullOrWhiteSpace(label) || label.Length > MaxLabelLength || PlainTextAttribute.ContainsControlCharacters(label)))
            return false;
        word = new Word(id, label);
        return true;
    }

    /// <summary><c>{"from":[r,c],"to":[r,c]}</c>; düz ve en az iki hücre olmalı.</summary>
    private static bool TryReadLine(JsonElement element, bool strict, out Line line)
    {
        line = default;
        if (element.ValueKind != JsonValueKind.Object || (strict && !HasOnlyKeys(element, ["from", "to"])))
            return false;
        if (!element.TryGetProperty("from", out var from) || !element.TryGetProperty("to", out var to)
            || !TryReadCell(from, out var r1, out var c1) || !TryReadCell(to, out var r2, out var c2))
            return false;
        line = new Line(r1, c1, r2, c2);
        return line.IsStraight && line.Length >= MinWordLength;
    }

    private static bool TryReadCell(JsonElement element, out int row, out int col)
    {
        row = col = 0;
        if (element.ValueKind != JsonValueKind.Array || element.GetArrayLength() != 2)
            return false;
        var a = element[0];
        var b = element[1];
        return a.ValueKind == JsonValueKind.Number && a.TryGetInt32(out row)
            && b.ValueKind == JsonValueKind.Number && b.TryGetInt32(out col);
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

    /// <summary>Katı payload: nesne, <c>type</c> = <see cref="Type"/>, varsa <c>version</c> = 1, <c>lines</c> düz çizgi dizisi (en çok 80).</summary>
    private static bool TryParsePayload(string payload, out List<Line> lines)
    {
        lines = new List<Line>();
        try
        {
            using var doc = JsonDocument.Parse(payload, StrictOptions);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object || !HasOnlyKeys(root, ["version", "type", "lines"]))
                return false;
            if (!root.TryGetProperty("type", out var type) || type.ValueKind != JsonValueKind.String
                || !string.Equals(type.GetString(), Type, StringComparison.Ordinal))
                return false;
            if (root.TryGetProperty("version", out var version)
                && (version.ValueKind != JsonValueKind.Number || !version.TryGetInt32(out var v) || v != 1))
                return false;
            if (!root.TryGetProperty("lines", out var array) || array.ValueKind != JsonValueKind.Array
                || array.GetArrayLength() > MaxLines(MaxWords))
                return false;
            foreach (var item in array.EnumerateArray())
            {
                if (!TryReadLine(item, strict: true, out var line))
                    return false;
                lines.Add(line);
            }
            return true;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            lines.Clear();
            return false;
        }
    }

    /// <summary>Kontrol için çizgiler: geçersiz / boş payload = çizgi yok; ızgara dışı çizgi atlanır.</summary>
    private static List<Line> ReadLines(string? payload, Puzzle puzzle)
        => payload != null && TryParsePayload(payload, out var lines)
            ? lines.Where(l => l.InGrid(puzzle.Rows, puzzle.Cols)).Take(MaxLines(puzzle.Words.Count)).ToList()
            : [];

    /// <summary>Çözüm: kelime → ızgaradan okunan harfler. Toleranslı: plana uymayan çizgi atlanır; okunamaz / boşsa false.</summary>
    private static bool TryReadSolution(string? solution, Puzzle puzzle, out Dictionary<string, string> targets)
    {
        targets = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(solution))
            return false;
        try
        {
            using var doc = JsonDocument.Parse(solution, ReadOptions);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("lines", out var element)
                || element.ValueKind != JsonValueKind.Object)
                return false;
            var ids = puzzle.Words.Select(w => w.Id).ToHashSet(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (ids.Contains(property.Name) && TryReadLine(property.Value, strict: false, out var line)
                    && line.InGrid(puzzle.Rows, puzzle.Cols))
                    targets[property.Name] = Spell(puzzle, line);
            }
            return targets.Count > 0;
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException or InvalidOperationException)
        {
            targets.Clear();
            return false;
        }
    }

    private static string WriteFeedback(IReadOnlyList<(string Id, bool Correct)> rows, IReadOnlyList<bool> lines)
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
            writer.WriteStartArray("lines");
            foreach (var hit in lines)
                writer.WriteBooleanValue(hit);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }
}
