using System.Text.Json;
using ExamApp.Api.Services.Interactions;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #575: paylaşılan normalleştirme fixture'ı (<c>shared/fixtures/slot-normalization.v1.json</c>) — UI'daki TS testi de
/// aynı dosyayı koşar. Her vektör kontrolcünün kutu karşılaştırıcısıyla (<see cref="SlotFillGrader.IsSlotCorrect"/>) doğrulanır.
/// </summary>
public class SlotNormalizationFixtureTests
{
    private const string FixtureRelativePath = "shared/fixtures/slot-normalization.v1.json";

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

    private static JsonElement Vectors => Fixture.Value.RootElement.GetProperty("vectors");

    public static IEnumerable<object[]> VectorIndexes()
        => Enumerable.Range(0, Vectors.GetArrayLength()).Select(i => new object[] { i });

    [Fact]
    public void Fixture_HasStableV1FormatAndEnoughVectors()
    {
        var root = Fixture.Value.RootElement;
        root.GetProperty("version").GetInt32().ShouldBe(1);
        root.GetProperty("$comment").ValueKind.ShouldBe(JsonValueKind.Array);
        Vectors.GetArrayLength().ShouldBeGreaterThanOrEqualTo(60);

        foreach (var v in Vectors.EnumerateArray())
        {
            v.GetProperty("kind").GetString().ShouldBeOneOf("number", "text", "palette");
            v.GetProperty("solution").ValueKind.ShouldBe(JsonValueKind.Object);
            v.GetProperty("answer").ValueKind.ShouldBe(JsonValueKind.String);
            v.GetProperty("expected").ValueKind.ShouldBeOneOf(JsonValueKind.True, JsonValueKind.False);
            v.GetProperty("note").GetString().ShouldNotBeNullOrWhiteSpace();
        }

        // Her tür hem doğru hem yanlış örnek taşır.
        foreach (var kind in new[] { "number", "text", "palette" })
        {
            var expected = Vectors.EnumerateArray().Where(v => v.GetProperty("kind").GetString() == kind)
                .Select(v => v.GetProperty("expected").GetBoolean()).ToHashSet();
            expected.ShouldBe(new HashSet<bool> { true, false }, ignoreOrder: true);
        }
    }

    [Fact]
    public void FixtureComment_StatesTheExactNumberPatternAndWhitespaceClass()
    {
        var comment = string.Join("\n", Fixture.Value.RootElement.GetProperty("$comment").EnumerateArray().Select(x => x.GetString()));
        comment.ShouldContain(SlotAnswerNormalizer.NumberPatternSource);
        comment.ShouldContain(SlotAnswerNormalizer.WhitespaceClass);
    }

    [Theory]
    [MemberData(nameof(VectorIndexes))]
    public void Vector_MatchesExpected(int index)
    {
        var v = Vectors[index];
        var kind = v.GetProperty("kind").GetString()!;
        var answer = v.GetProperty("answer").GetString();
        var expected = v.GetProperty("expected").GetBoolean();

        var actual = SlotFillGrader.IsSlotCorrect(kind, v.GetProperty("solution"), answer);

        actual.ShouldBe(expected,
            $"#{index} [{kind}] answer='{answer}' solution={v.GetProperty("solution").GetRawText()} — {v.GetProperty("note").GetString()}");
    }

    [Fact]
    public void Fixture_NumberSolutionsAreValidDefinitions()
    {
        // Fixture'daki her çözüm, kayıt yolunda da geçerli olmalı (aksi halde öğretmen o tanımı kaydedemezdi).
        var grader = new SlotFillGrader();
        foreach (var v in Vectors.EnumerateArray())
        {
            var kind = v.GetProperty("kind").GetString()!;
            var input = kind switch
            {
                "number" => """{"kind":"number","entry":"plain"}""",
                "text" => """{"kind":"text"}""",
                _ => """{"kind":"palette","symbols":["∠"]}""",
            };
            var plan = "{\"version\":1,\"type\":\"slotFill\",\"slots\":[{\"id\":\"s1\",\"box\":{\"x\":0,\"y\":0,\"width\":5,\"height\":2},\"input\":" + input + "}]}";
            var solution = "{\"s1\":" + v.GetProperty("solution").GetRawText() + "}";
            grader.ValidateDefinition(plan, solution).ShouldBeTrue(solution);
        }
    }
}
