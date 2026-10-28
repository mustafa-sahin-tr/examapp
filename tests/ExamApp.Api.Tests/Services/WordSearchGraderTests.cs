using System.Text.Json;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #586: <see cref="WordSearchGrader"/> — kayıt kuralları (ızgara, etiket, düz çizgi, tekil çizgi), payload doğrulama, harfle
/// karşılaştırma (ters yön, kelimenin başka yerde geçmesi, tr-TR), çizgi geri bildirimi, çözüm sızıntısı yok, analiz, çözümsüz soru.
/// </summary>
public class WordSearchGraderTests
{
    //   0 1 2 3 4
    // 0 K A L P X
    // 1 E Y Z İ M
    // 2 D T A R U
    // 3 İ N O K E
    // w1 KALP → (0,0)-(0,3); w2 KEDİ ↓ (0,0)-(3,0); w3 RAT ← (2,3)-(2,1); w4 YAK ↘ (1,1)-(3,3).
    private const string Plan =
        """{"version":1,"type":"wordSearch","rows":4,"cols":5,"letters":["KALPX","EYZİM","DTARU","İNOKE"],"words":[{"id":"w1","label":"KALP"},{"id":"w2","label":"KEDİ"},{"id":"w3","label":"RAT"},{"id":"w4","label":"YAK"}]}""";

    private const string Solution =
        """{"lines":{"w1":{"from":[0,0],"to":[0,3]},"w2":{"from":[0,0],"to":[3,0]},"w3":{"from":[2,3],"to":[2,1]},"w4":{"from":[1,1],"to":[3,3]}}}""";

    private static readonly WordSearchGrader Grader = new();

    private static string Lines(params int[][] lines)
        => JsonSerializer.Serialize(new
        {
            version = 1,
            type = "wordSearch",
            lines = lines.Select(l => new { from = new[] { l[0], l[1] }, to = new[] { l[2], l[3] } }),
        });

    private static InteractionGradeInput Input(string? solution, string? payload, string plan = Plan)
        => new(plan, solution, payload, SelectedAnswerId: null, CorrectAnswerId: null);

    private static (List<(string Id, bool Correct)> Rows, List<bool> Lines) Feedback(string? feedback)
    {
        feedback.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(feedback!);
        var rows = doc.RootElement.GetProperty("rows").EnumerateArray()
            .Select(r => (r.GetProperty("id").GetString()!, r.GetProperty("correct").GetBoolean())).ToList();
        var lines = doc.RootElement.GetProperty("lines").EnumerateArray().Select(l => l.GetBoolean()).ToList();
        return (rows, lines);
    }

    // ---- Kayıt ----

    [Fact]
    public void ValidateDefinition_Pilot_Accepted()
    {
        Grader.ValidateDefinition(Plan, Solution).ShouldBeTrue();
        Grader.ValidateDefinition(Plan, null).ShouldBeTrue();
    }

    [Theory]
    // Düz olmayan çizgi.
    [InlineData("""{"lines":{"w1":{"from":[0,0],"to":[1,3]},"w2":{"from":[0,0],"to":[3,0]},"w3":{"from":[2,3],"to":[2,1]},"w4":{"from":[1,1],"to":[3,3]}}}""")]
    // Izgara dışı.
    [InlineData("""{"lines":{"w1":{"from":[0,0],"to":[0,5]},"w2":{"from":[0,0],"to":[3,0]},"w3":{"from":[2,3],"to":[2,1]},"w4":{"from":[1,1],"to":[3,3]}}}""")]
    // Tek hücre.
    [InlineData("""{"lines":{"w1":{"from":[0,0],"to":[0,0]},"w2":{"from":[0,0],"to":[3,0]},"w3":{"from":[2,3],"to":[2,1]},"w4":{"from":[1,1],"to":[3,3]}}}""")]
    // Eksik kelime.
    [InlineData("""{"lines":{"w1":{"from":[0,0],"to":[0,3]},"w2":{"from":[0,0],"to":[3,0]},"w3":{"from":[2,3],"to":[2,1]}}}""")]
    // İki kelime aynı çizgi (ters yönle de).
    [InlineData("""{"lines":{"w1":{"from":[0,0],"to":[0,3]},"w2":{"from":[0,3],"to":[0,0]},"w3":{"from":[2,3],"to":[2,1]},"w4":{"from":[1,1],"to":[3,3]}}}""")]
    // Bilinmeyen anahtar.
    [InlineData("""{"lines":{"w1":{"from":[0,0],"to":[0,3],"x":1},"w2":{"from":[0,0],"to":[3,0]},"w3":{"from":[2,3],"to":[2,1]},"w4":{"from":[1,1],"to":[3,3]}}}""")]
    public void ValidateDefinition_BadSolution_Rejected(string solution)
        => Grader.ValidateDefinition(Plan, solution).ShouldBeFalse();

    [Theory]
    // Satır uzunluğu tutmuyor.
    [InlineData("""{"version":1,"type":"wordSearch","rows":4,"cols":5,"letters":["KALP","EYZİM","DTARU","İNOKE"],"words":[{"id":"w1","label":"x"}]}""")]
    // Satır sayısı tutmuyor.
    [InlineData("""{"version":1,"type":"wordSearch","rows":4,"cols":5,"letters":["KALPX","EYZİM","DTARU"],"words":[{"id":"w1","label":"x"}]}""")]
    // Harf/rakam dışı hücre.
    [InlineData("""{"version":1,"type":"wordSearch","rows":4,"cols":5,"letters":["KAL X","EYZİM","DTARU","İNOKE"],"words":[{"id":"w1","label":"x"}]}""")]
    // Etiketsiz kelime.
    [InlineData("""{"version":1,"type":"wordSearch","rows":4,"cols":5,"letters":["KALPX","EYZİM","DTARU","İNOKE"],"words":[{"id":"w1"}]}""")]
    // Kelime yok.
    [InlineData("""{"version":1,"type":"wordSearch","rows":4,"cols":5,"letters":["KALPX","EYZİM","DTARU","İNOKE"],"words":[]}""")]
    // Izgara çok küçük.
    [InlineData("""{"version":1,"type":"wordSearch","rows":3,"cols":5,"letters":["KALPX","EYZİM","DTARU"],"words":[{"id":"w1","label":"x"}]}""")]
    // Bilinmeyen anahtar.
    [InlineData("""{"version":1,"type":"wordSearch","rows":4,"cols":5,"letters":["KALPX","EYZİM","DTARU","İNOKE"],"words":[{"id":"w1","label":"x"}],"x":1}""")]
    public void ValidateDefinition_BadPlan_Rejected(string plan)
        => Grader.ValidateDefinition(plan, null).ShouldBeFalse();

    // ---- Payload ----

    [Fact]
    public void Validate_PayloadShape()
    {
        Grader.Validate(Plan, null).ShouldBeTrue();
        Grader.Validate(Plan, Lines([0, 0, 0, 3], [3, 3, 1, 1])).ShouldBeTrue();
        Grader.Validate(Plan, Lines()).ShouldBeTrue();
        Grader.Validate(Plan, Lines([0, 0, 1, 2])).ShouldBeFalse(); // düz değil
        Grader.Validate(Plan, Lines([0, 0, 0, 7])).ShouldBeFalse(); // ızgara dışı
        Grader.Validate(Plan, Lines([0, 0, 0, 0])).ShouldBeFalse(); // tek hücre
        Grader.Validate(Plan, """{"version":1,"type":"wordSearch","lines":[],"x":1}""").ShouldBeFalse();
        Grader.Validate(Plan, Lines(Enumerable.Range(0, 11).Select(_ => new[] { 0, 0, 0, 1 }).ToArray())).ShouldBeFalse(); // > 10 çizgi
    }

    [Fact]
    public void Normalize_NoLinesIsNoAnswer()
    {
        Grader.Normalize(Lines()).ShouldBeNull();
        var payload = Lines([0, 0, 0, 3]);
        Grader.Normalize(payload).ShouldBe(payload);
    }

    // ---- Kontrol ----

    [Fact]
    public void Grade_AllFound_ReverseDirectionsAccepted()
    {
        // w3 ve w4 çözümün tersi yönde işaretlendi.
        var result = Grader.Grade(Input(Solution, Lines([0, 0, 0, 3], [3, 0, 0, 0], [2, 1, 2, 3], [3, 3, 1, 1])));
        result.IsCorrect.ShouldBeTrue();
        result.CorrectCount.ShouldBe(4);
        var (rows, lines) = Feedback(result.Feedback);
        rows.ShouldAllBe(r => r.Correct);
        lines.ShouldBe([true, true, true, true]);
    }

    [Fact]
    public void Grade_PartialAndWrongLines()
    {
        // KALP doğru; (1,0)-(1,2) "EYZ" kelime değil; (0,0)-(2,0) "KED" eksik.
        var result = Grader.Grade(Input(Solution, Lines([0, 0, 0, 3], [1, 0, 1, 2], [0, 0, 2, 0])));
        result.CorrectCount.ShouldBe(1);
        result.TotalCount.ShouldBe(4);
        var (rows, lines) = Feedback(result.Feedback);
        rows.ShouldBe([("w1", true), ("w2", false), ("w3", false), ("w4", false)]);
        lines.ShouldBe([true, false, false]);
    }

    [Fact]
    public void Grade_WordFoundElsewhereInGrid_Counts()
    {
        // "AT" ızgarada iki yerde: çözüm (0,0)-(0,1), öğrenci (2,2)-(2,3) işaretledi.
        const string plan = """{"version":1,"type":"wordSearch","rows":4,"cols":4,"letters":["ATXX","XXXX","XXAT","XXXX"],"words":[{"id":"w1","label":"AT"}]}""";
        const string solution = """{"lines":{"w1":{"from":[0,0],"to":[0,1]}}}""";
        Grader.Grade(Input(solution, Lines([2, 2, 2, 3]), plan)).IsCorrect.ShouldBeTrue();
    }

    [Fact]
    public void Grade_FeedbackDoesNotLeakSolutionLines()
    {
        var feedback = Grader.Grade(Input(Solution, Lines([1, 0, 1, 2]))).Feedback!;
        feedback.ShouldNotContain("from");
        feedback.ShouldNotContain("KALP");
    }

    [Fact]
    public void Grade_NoSolution_Ungradable()
    {
        Grader.Grade(Input(null, Lines([0, 0, 0, 3]))).ShouldBe(InteractionGradeResult.Ungradable);
        Grader.Analyze(Input(null, Lines([0, 0, 0, 3]))).ShouldBeEmpty();
    }

    // ---- Analiz ----

    [Fact]
    public void Analyze_ItemPerWord()
    {
        var items = Grader.Analyze(Input(Solution, Lines([0, 0, 0, 3])));
        items.Select(i => i.ItemId).ShouldBe(["w1", "w2", "w3", "w4"]);
        items[0].Correct.ShouldBeTrue();
        items[0].AnswerKey.ShouldBe("found");
        items[1].Correct.ShouldBeFalse();
        items[1].AnswerKey.ShouldBeNull();
        items[0].ItemLabel.ShouldBe("KALP");
    }
}
