using System.Text.Json;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #588 (dilim c): <see cref="GridDrawGrader"/> — kayıt kuralları (zemin, kural, sığan boyut), payload doğrulama, kural tabanlı
/// kontrol (çoklu çözüm, döndürme, yer serbest, tekrar, fazla), geri bildirimde eksik boyut yok, analiz, çözümsüz soru.
/// </summary>
public class GridDrawGraderTests
{
    // TT MatLab dosya 87: kenarları doğal sayı ve çevresi 14 cm olan dikdörtgenler → 1×6, 2×5, 3×4.
    private const string Plan = """{"version":1,"type":"gridDraw","rows":10,"cols":16}""";

    private const string AllPerimeter = """{"perimeter":14,"all":true}""";

    private static readonly GridDrawGrader Grader = new();

    private static string Payload(params (int Row, int Col, int Width, int Height)[] shapes)
        => JsonSerializer.Serialize(new
        {
            version = 1,
            type = "gridDraw",
            shapes = shapes.Select(s => new { row = s.Row, col = s.Col, width = s.Width, height = s.Height }),
        });

    private static InteractionGradeInput Input(string? solution, string? payload, string plan = Plan)
        => new(plan, solution, payload, SelectedAnswerId: null, CorrectAnswerId: null);

    private static (List<string> Shapes, int Found, int Missing, int Extra) Feedback(string? feedback)
    {
        feedback.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(feedback!);
        var root = doc.RootElement;
        return (root.GetProperty("shapes").EnumerateArray().Select(s => s.GetString()!).ToList(),
            root.GetProperty("found").GetInt32(), root.GetProperty("missing").GetInt32(), root.GetProperty("extra").GetInt32());
    }

    // ---- Kayıt ----

    [Theory]
    [InlineData("""{"perimeter":14,"all":true}""")]
    [InlineData("""{"area":20}""")]
    [InlineData("""{"area":12,"perimeter":14,"all":false}""")]
    public void ValidateDefinition_Pilot_Accepted(string solution)
    {
        Grader.ValidateDefinition(Plan, solution).ShouldBeTrue();
        Grader.ValidateDefinition(Plan, null).ShouldBeTrue();
    }

    [Theory]
    // Zemin sınır dışı, bilinmeyen alan, geçersiz grid / shape, sürüm.
    [InlineData("""{"version":1,"type":"gridDraw","rows":1,"cols":10}""")]
    [InlineData("""{"version":1,"type":"gridDraw","rows":10,"cols":31}""")]
    [InlineData("""{"version":1,"type":"gridDraw","rows":10,"cols":10,"extra":1}""")]
    [InlineData("""{"version":1,"type":"gridDraw","rows":10,"cols":10,"grid":"iso"}""")]
    [InlineData("""{"version":1,"type":"gridDraw","rows":10,"cols":10,"shape":"triangle"}""")]
    [InlineData("""{"version":2,"type":"gridDraw","rows":10,"cols":10}""")]
    public void ValidateDefinition_BadPlan_Rejected(string plan)
        => Grader.ValidateDefinition(plan, null).ShouldBeFalse();

    [Theory]
    // Kuralsız, sınır dışı, bilinmeyen alan, bool olmayan all, koşulları birlikte sağlayan boyut yok, sığan boyut yok (1×17 > 16 kolon).
    [InlineData("""{"all":true}""")]
    [InlineData("""{"area":0}""")]
    [InlineData("""{"perimeter":3}""")]
    [InlineData("""{"area":12,"color":"red"}""")]
    [InlineData("""{"area":12,"all":"yes"}""")]
    [InlineData("""{"area":12,"perimeter":12}""")]
    [InlineData("""{"area":17}""")]
    public void ValidateDefinition_BadSolution_Rejected(string solution)
        => Grader.ValidateDefinition(Plan, solution).ShouldBeFalse();

    [Fact]
    public void ValidateDefinition_NoTargetFits_Rejected()
    {
        // 4×4 zemine alanı 15 olan dikdörtgen (3×5, 1×15) sığmaz.
        const string plan = """{"version":1,"type":"gridDraw","rows":4,"cols":4}""";
        Grader.ValidateDefinition(plan, """{"area":15}""").ShouldBeFalse();
        Grader.ValidateDefinition(plan, """{"area":12}""").ShouldBeTrue();
    }

    [Fact]
    public void ValidateDefinition_SquareShape_NeedsSquareTarget()
    {
        const string plan = """{"version":1,"type":"gridDraw","rows":10,"cols":10,"shape":"square"}""";
        Grader.ValidateDefinition(plan, """{"perimeter":20}""").ShouldBeTrue();
        Grader.ValidateDefinition(plan, """{"perimeter":14}""").ShouldBeFalse();
    }

    // ---- Payload ----

    [Fact]
    public void Validate_Payload()
    {
        Grader.Validate(Plan, null).ShouldBeTrue();
        Grader.Validate(Plan, Payload((0, 0, 3, 4))).ShouldBeTrue();
        // Zemin dışına taşan, sıfır kenar, bilinmeyen alan, başka tip.
        Grader.Validate(Plan, Payload((8, 0, 3, 4))).ShouldBeFalse();
        Grader.Validate(Plan, Payload((0, 0, 0, 4))).ShouldBeFalse();
        Grader.Validate(Plan, """{"version":1,"type":"gridDraw","shapes":[{"row":0,"col":0,"width":1,"height":1,"x":1}]}""").ShouldBeFalse();
        Grader.Validate(Plan, """{"version":1,"type":"barChart","shapes":[]}""").ShouldBeFalse();
        Grader.Validate(Plan, Payload(Enumerable.Range(0, 31).Select(_ => (0, 0, 1, 1)).ToArray())).ShouldBeFalse();
    }

    [Fact]
    public void Normalize_EmptyShapes_IsNoAnswer()
    {
        Grader.Normalize(Payload()).ShouldBeNull();
        var payload = Payload((0, 0, 1, 6));
        Grader.Normalize(payload).ShouldBe(payload);
    }

    // ---- Kontrol ----

    [Fact]
    public void Grade_AllSizes_AnyPositionAndRotation_Correct()
    {
        // 6×1 yatay, 2×5 dikey, 4×3 (döndürülmüş 3×4) — yer serbest.
        var result = Grader.Grade(Input(AllPerimeter, Payload((0, 0, 6, 1), (2, 0, 2, 5), (5, 10, 4, 3))));
        result.IsCorrect.ShouldBeTrue();
        result.CorrectCount.ShouldBe(3);
        result.TotalCount.ShouldBe(3);
        var feedback = Feedback(result.Feedback);
        feedback.Shapes.ShouldBe(["ok", "ok", "ok"]);
        (feedback.Found, feedback.Missing, feedback.Extra).ShouldBe((3, 0, 0));
    }

    [Fact]
    public void Grade_MissingDuplicateAndWrong()
    {
        // 3×4 iki kez (ikincisi tekrar), 3×3 kurala uymuyor (çevre 12); 1×6 ve 2×5 eksik.
        var result = Grader.Grade(Input(AllPerimeter, Payload((0, 0, 3, 4), (0, 5, 4, 3), (5, 0, 3, 3))));
        result.IsCorrect.ShouldBeFalse();
        result.CorrectCount.ShouldBe(1);
        result.TotalCount.ShouldBe(4);
        var feedback = Feedback(result.Feedback);
        feedback.Shapes.ShouldBe(["ok", "duplicate", "wrong"]);
        (feedback.Found, feedback.Missing, feedback.Extra).ShouldBe((1, 2, 1));
        // Eksik boyutlar yazılmaz.
        using var doc = JsonDocument.Parse(result.Feedback!);
        doc.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["shapes", "found", "missing", "extra"]);
    }

    [Fact]
    public void Grade_SingleShape_AnyValidSizeIsEnough()
    {
        // Alanı 20: 1×20 sığmaz (16 kolon, 10 satır), 2×10, 4×5 sığar; biri yeter.
        var result = Grader.Grade(Input("""{"area":20}""", Payload((1, 1, 5, 4))));
        result.IsCorrect.ShouldBeTrue();
        (result.CorrectCount, result.TotalCount).ShouldBe((1, 1));
        // İki farklı geçerli boyut da gerekeni aşmaz.
        var two = Grader.Grade(Input("""{"area":20}""", Payload((0, 0, 5, 4), (5, 0, 10, 2))));
        (two.CorrectCount, two.TotalCount).ShouldBe((1, 1));
        // 1×20 zemine sığmadığından çizilemez; 4×4 yanlış.
        var wrong = Grader.Grade(Input("""{"area":20}""", Payload((0, 0, 4, 4))));
        (wrong.CorrectCount, wrong.TotalCount).ShouldBe((0, 2));
    }

    [Fact]
    public void Grade_SquareShape_RejectsRectangle()
    {
        const string plan = """{"version":1,"type":"gridDraw","rows":10,"cols":10,"shape":"square"}""";
        Grader.Grade(Input("""{"perimeter":20}""", Payload((0, 0, 5, 5)), plan)).IsCorrect.ShouldBeTrue();
        // Çevresi 20 ama kare değil.
        Grader.Grade(Input("""{"perimeter":20}""", Payload((0, 0, 4, 6)), plan)).IsCorrect.ShouldBeFalse();
    }

    [Fact]
    public void Grade_NoAnswer_AllMissing()
    {
        var result = Grader.Grade(Input(AllPerimeter, null));
        (result.CorrectCount, result.TotalCount).ShouldBe((0, 3));
    }

    [Fact]
    public void Grade_WithoutSolution_Ungradable()
        => Grader.Grade(Input(null, Payload((0, 0, 3, 4)))).ShouldBe(InteractionGradeResult.Ungradable);

    // ---- Analiz ----

    [Fact]
    public void Analyze_All_ItemPerSize()
    {
        var items = Grader.Analyze(Input(AllPerimeter, Payload((0, 0, 4, 3), (5, 0, 3, 3))));
        items.Select(i => (i.ItemId, i.ItemLabel, i.Correct)).ShouldBe([
            ("s1x6", "1 × 6", false), ("s2x5", "2 × 5", false), ("s3x4", "3 × 4", true)]);
    }

    [Fact]
    public void Analyze_Single_FirstValidDrawing()
    {
        var items = Grader.Analyze(Input("""{"area":20}""", Payload((0, 0, 3, 3), (0, 4, 5, 4))));
        items.Count.ShouldBe(1);
        items[0].Correct.ShouldBeTrue();
        items[0].AnswerLabel.ShouldBe("4 × 5");
        var none = Grader.Analyze(Input("""{"area":20}""", null));
        none[0].Correct.ShouldBeFalse();
        none[0].AnswerKey.ShouldBeNull();
    }

    [Fact]
    public void Registry_ResolvesGridDraw()
        => InteractionGraderRegistry.Default.Resolve(GridDrawGrader.Type).ShouldBeOfType<GridDrawGrader>();
}
