using System.Text;
using System.Text.Json;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #587: <see cref="RegionPaintGrader"/> ızgara modu — kayıt kuralları (boyut, palet, izinli renk, çözüm sayıları), payload
/// doğrulama, renk başına sayı kontrolü (yer serbest), çözüm sızıntısı yok, analiz, çözümsüz soru.
/// </summary>
public class RegionPaintGraderTests
{
    // Matematik Ustası s.167: 10×10 ızgarada 2/5 siyah, 0,35 mavi, %25 yeşil; kırmızı çeldirici (0).
    private const string Plan =
        """{"version":1,"type":"regionPaint","mode":"grid","rows":10,"cols":10,"palette":[{"id":"p1","color":"black","label":"2/5"},{"id":"p2","color":"blue","label":"0,35"},{"id":"p3","color":"green","label":"%25"},{"id":"p4","color":"red"}]}""";

    private const string Solution = """{"counts":{"p1":40,"p2":35,"p3":25,"p4":0}}""";

    private static readonly RegionPaintGrader Grader = new();

    /// <summary>Hücreleri satır öncelikli sırayla verilen sayılarda palet karakteriyle doldurur.</summary>
    private static string Paint(params (char Paint, int Count)[] runs)
    {
        var flat = new StringBuilder();
        foreach (var (paint, count) in runs)
            flat.Append(paint, count);
        flat.Append('.', 100 - flat.Length);
        var rows = Enumerable.Range(0, 10).Select(r => flat.ToString(r * 10, 10)).ToArray();
        return JsonSerializer.Serialize(new { version = 1, type = "regionPaint", grid = rows });
    }

    private static InteractionGradeInput Input(string? solution, string? payload)
        => new(Plan, solution, payload, SelectedAnswerId: null, CorrectAnswerId: null);

    private static List<(string Id, bool Correct)> Rows(string? feedback)
    {
        feedback.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(feedback!);
        return doc.RootElement.GetProperty("rows").EnumerateArray()
            .Select(r => (r.GetProperty("id").GetString()!, r.GetProperty("correct").GetBoolean())).ToList();
    }

    // ---- Kayıt ----

    [Fact]
    public void ValidateDefinition_Pilot_Accepted()
    {
        Grader.ValidateDefinition(Plan, Solution).ShouldBeTrue();
        Grader.ValidateDefinition(Plan, null).ShouldBeTrue();
    }

    [Theory]
    // Mod yok / bilinmiyor.
    [InlineData("""{"version":1,"type":"regionPaint","rows":10,"cols":10,"palette":[{"id":"p1","color":"black"}]}""")]
    [InlineData("""{"version":1,"type":"regionPaint","mode":"poly","rows":10,"cols":10,"palette":[{"id":"p1","color":"black"}]}""")]
    // Boyut sınır dışı.
    [InlineData("""{"version":1,"type":"regionPaint","mode":"grid","rows":1,"cols":10,"palette":[{"id":"p1","color":"black"}]}""")]
    [InlineData("""{"version":1,"type":"regionPaint","mode":"grid","rows":10,"cols":21,"palette":[{"id":"p1","color":"black"}]}""")]
    // Palet boş / izinsiz renk / aynı renk iki kez / aynı id.
    [InlineData("""{"version":1,"type":"regionPaint","mode":"grid","rows":10,"cols":10,"palette":[]}""")]
    [InlineData("""{"version":1,"type":"regionPaint","mode":"grid","rows":10,"cols":10,"palette":[{"id":"p1","color":"#ff0000"}]}""")]
    [InlineData("""{"version":1,"type":"regionPaint","mode":"grid","rows":10,"cols":10,"palette":[{"id":"p1","color":"red"},{"id":"p2","color":"red"}]}""")]
    [InlineData("""{"version":1,"type":"regionPaint","mode":"grid","rows":10,"cols":10,"palette":[{"id":"p1","color":"red"},{"id":"p1","color":"blue"}]}""")]
    // Bilinmeyen alan / kontrol karakterli etiket.
    [InlineData("""{"version":1,"type":"regionPaint","mode":"grid","rows":10,"cols":10,"palette":[{"id":"p1","color":"red"}],"x":1}""")]
    [InlineData("""{"version":1,"type":"regionPaint","mode":"grid","rows":10,"cols":10,"palette":[{"id":"p1","color":"red","label":"a‮b"}]}""")]
    public void ValidateDefinition_BadPlan_Rejected(string plan)
    {
        Grader.ValidateDefinition(plan, null).ShouldBeFalse();
    }

    [Fact]
    public void ValidateDefinition_NineColors_Rejected()
    {
        var colors = new[] { "red", "orange", "yellow", "green", "blue", "purple", "pink", "brown", "black" };
        var palette = colors.Select((c, i) => new { id = $"p{i + 1}", color = c });
        var plan = JsonSerializer.Serialize(new { version = 1, type = "regionPaint", mode = "grid", rows = 10, cols = 10, palette });
        Grader.ValidateDefinition(plan, null).ShouldBeFalse();
    }

    [Theory]
    // Renk eksik.
    [InlineData("""{"counts":{"p1":40,"p2":35,"p3":25}}""")]
    // Toplam hücre sayısını aşıyor.
    [InlineData("""{"counts":{"p1":40,"p2":35,"p3":26,"p4":0}}""")]
    // Hepsi 0.
    [InlineData("""{"counts":{"p1":0,"p2":0,"p3":0,"p4":0}}""")]
    // Negatif / ondalık / bilinmeyen renk.
    [InlineData("""{"counts":{"p1":-1,"p2":35,"p3":25,"p4":0}}""")]
    [InlineData("""{"counts":{"p1":40.5,"p2":35,"p3":25,"p4":0}}""")]
    [InlineData("""{"counts":{"p1":40,"p2":35,"p3":25,"p9":0}}""")]
    [InlineData("""{"counts":{"p1":40,"p2":35,"p3":25,"p4":0},"cells":[]}""")]
    public void ValidateDefinition_BadSolution_Rejected(string solution)
    {
        Grader.ValidateDefinition(Plan, solution).ShouldBeFalse();
    }

    // ---- Payload ----

    [Fact]
    public void Validate_Payload()
    {
        Grader.Validate(Plan, null).ShouldBeTrue();
        Grader.Validate(Plan, Paint(('1', 40))).ShouldBeTrue();
        // Palet dışı karakter (5. renk yok), eksik satır, kısa satır.
        Grader.Validate(Plan, Paint(('5', 1))).ShouldBeFalse();
        Grader.Validate(Plan, """{"version":1,"type":"regionPaint","grid":[".........."]}""").ShouldBeFalse();
        Grader.Validate(Plan, Paint().Replace("\"..........\"]", "\".........\"]")).ShouldBeFalse();
        Grader.Validate(Plan, """{"version":1,"type":"logicGrid","cells":[]}""").ShouldBeFalse();
    }

    [Fact]
    public void Normalize_EmptyGrid_IsNoAnswer()
    {
        Grader.Normalize(Paint()).ShouldBeNull();
        var painted = Paint(('2', 3));
        Grader.Normalize(painted).ShouldBe(painted);
    }

    // ---- Kontrol ----

    [Fact]
    public void Grade_CountsMatch_AnyPosition_AllCorrect()
    {
        var result = Grader.Grade(Input(Solution, Paint(('3', 25), ('1', 40), ('2', 35))));
        result.IsCorrect.ShouldBeTrue();
        result.CorrectCount.ShouldBe(4);
        result.TotalCount.ShouldBe(4);
    }

    [Fact]
    public void Grade_WrongCounts_PerColorRows_NoSolutionLeak()
    {
        // Siyah 40 doğru, mavi 34 eksik, yeşil yok, kırmızı çeldirici kullanılmış.
        var result = Grader.Grade(Input(Solution, Paint(('1', 40), ('2', 34), ('4', 1))));
        result.IsCorrect.ShouldBeFalse();
        Rows(result.Feedback).ShouldBe([("p1", true), ("p2", false), ("p3", false), ("p4", false)]);
        result.Feedback!.ShouldNotContain("35");
        result.Feedback.ShouldNotContain("counts");
    }

    [Fact]
    public void Grade_Blank_OnlyZeroColorsCorrect()
    {
        var result = Grader.Grade(Input(Solution, null));
        result.CorrectCount.ShouldBe(1);
        result.TotalCount.ShouldBe(4);
    }

    [Fact]
    public void Grade_NoSolution_Ungradable()
    {
        var result = Grader.Grade(Input(null, Paint(('1', 40))));
        result.TotalCount.ShouldBe(0);
        Grader.Analyze(Input(null, Paint(('1', 40)))).ShouldBeEmpty();
    }

    [Fact]
    public void Analyze_ColorItems_WithPaintedCount()
    {
        var items = Grader.Analyze(Input(Solution, Paint(('1', 40), ('2', 30))));
        items.Select(i => i.ItemId).ShouldBe(["p1", "p2", "p3", "p4"]);
        items[0].ItemLabel.ShouldBe("2/5");
        items[0].Correct.ShouldBeTrue();
        items[1].AnswerKey.ShouldBe("30");
        items[1].Correct.ShouldBeFalse();
        items[2].AnswerKey.ShouldBeNull();
        items[3].ItemLabel.ShouldBe("red");
        items[3].Correct.ShouldBeTrue();
    }

    [Fact]
    public void Registry_ResolvesRegionPaint()
    {
        InteractionGraderRegistry.Default.Resolve("regionpaint").ShouldBeOfType<RegionPaintGrader>();
    }
}
