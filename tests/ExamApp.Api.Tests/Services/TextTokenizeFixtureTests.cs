using System.Text.Json;
using ExamApp.Api.Services.Interactions;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #582: paylaşılan satır → token bölme fixture'ı (<c>shared/fixtures/text-tokenize.v1.json</c>) — UI'daki TS testi de
/// aynı dosyayı koşar. Her vaka <see cref="TextSelectTokenizer.Tokenize"/> ile birebir (id, metin, satır, gap, kutular) doğrulanır.
/// </summary>
public class TextTokenizeFixtureTests
{
    private const string FixtureRelativePath = "shared/fixtures/text-tokenize.v1.json";

    private static readonly Lazy<JsonDocument> Fixture = new(() => JsonDocument.Parse(File.ReadAllText(FixturePath())));

    /// <summary>Test çıktı dizininden yukarı yürüyerek repo kökünü (ExamApp.slnx ya da .git) bulur.</summary>
    private static string FixturePath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ExamApp.slnx")) || Directory.Exists(Path.Combine(dir.FullName, ".git"))
                || File.Exists(Path.Combine(dir.FullName, ".git")))
                return Path.Combine(dir.FullName, FixtureRelativePath);
            dir = dir.Parent;
        }
        throw new InvalidOperationException("Repo root (ExamApp.slnx / .git) not found above " + AppContext.BaseDirectory);
    }

    private static JsonElement Cases => Fixture.Value.RootElement.GetProperty("cases");

    public static IEnumerable<object[]> CaseIndexes()
        => Enumerable.Range(0, Cases.GetArrayLength()).Select(i => new object[] { i });

    private static TextSelectTokenizer.TokenBox ReadBox(JsonElement box) => new(
        box.GetProperty("x").GetInt32(), box.GetProperty("y").GetInt32(),
        box.GetProperty("width").GetInt32(), box.GetProperty("height").GetInt32());

    private static string Describe(TextSelectTokenizer.Token t)
        => $"{t.Id}|{t.Text}|{t.Line}|{t.Gap}|" + string.Join(";", t.Boxes.Select(b => $"{b.X},{b.Y},{b.Width},{b.Height}"));

    [Fact]
    public void Fixture_HasStableV1FormatAndCoversTheRequiredCases()
    {
        var root = Fixture.Value.RootElement;
        root.GetProperty("version").GetInt32().ShouldBe(1);
        Cases.GetArrayLength().ShouldBeGreaterThanOrEqualTo(6);

        var names = Cases.EnumerateArray().Select(c => c.GetProperty("name").GetString()).ToHashSet();
        foreach (var required in new[] { "normal", "punctuation-attached", "gaps", "line-break-hyphen", "empty-and-single-word-lines", "nfc-composition" })
            names.ShouldContain(required);
        foreach (var c in Cases.EnumerateArray())
        {
            c.GetProperty("note").GetString().ShouldNotBeNullOrWhiteSpace();
            c.GetProperty("lines").ValueKind.ShouldBe(JsonValueKind.Array);
            c.GetProperty("expected").ValueKind.ShouldBe(JsonValueKind.Array);
        }
    }

    [Fact]
    public void FixtureComment_StatesTheExactPiecePatternAndWhitespaceClass()
    {
        var comment = string.Join("\n", Fixture.Value.RootElement.GetProperty("$comment").EnumerateArray().Select(x => x.GetString()));
        comment.ShouldContain(TextSelectTokenizer.PiecePatternSource);
        comment.ShouldContain(SlotAnswerNormalizer.WhitespaceClass);
    }

    [Theory]
    [MemberData(nameof(CaseIndexes))]
    public void Case_TokenizesAsExpected(int index)
    {
        var c = Cases[index];
        var lines = c.GetProperty("lines").EnumerateArray()
            .Select(l => new TextSelectTokenizer.Line(l.GetProperty("text").GetString(), ReadBox(l.GetProperty("box"))))
            .ToList();
        var expected = c.GetProperty("expected").EnumerateArray()
            .Select(e => new TextSelectTokenizer.Token(
                e.GetProperty("id").GetString()!,
                e.GetProperty("text").GetString()!,
                e.GetProperty("line").GetInt32(),
                e.TryGetProperty("gap", out var gap) && gap.GetBoolean(),
                e.GetProperty("boxes").EnumerateArray().Select(ReadBox).ToList()))
            .Select(Describe)
            .ToList();

        var actual = TextSelectTokenizer.Tokenize(lines).Select(Describe).ToList();

        actual.ShouldBe(expected, $"#{index} {c.GetProperty("name").GetString()} — {c.GetProperty("note").GetString()}");
    }
}
