using System.Text.Json;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #588 (dilim b): <see cref="BarChartGrader"/> — kayıt kuralları (eksen, kategori, sabit sütun, çözüm), payload doğrulama,
/// kategori başına kontrol, çizilmeyen sütun 0, çözüm sızıntısı yok, analiz, çözümsüz soru.
/// </summary>
public class BarChartGraderTests
{
    // Süper Combo Matematik s.187: sevilen meyveler, eksen 0–10 (2'şer), 1'er adım; elma sütunu hazır.
    private const string Plan =
        """{"version":1,"type":"barChart","title":"Sevilen meyveler","xLabel":"Meyve","yLabel":"Öğrenci","max":10,"step":2,"unit":1,"categories":[{"id":"c1","label":"Elma"},{"id":"c2","label":"Armut"},{"id":"c3","label":"Muz"},{"id":"c4","label":"Kiraz"}],"fixed":{"c1":6}}""";

    private const string Solution = """{"values":{"c2":4,"c3":7,"c4":0}}""";

    private static readonly BarChartGrader Grader = new();

    private static string Payload(object values) => JsonSerializer.Serialize(new { version = 1, type = "barChart", values });

    private static InteractionGradeInput Input(string? solution, string? payload, string plan = Plan)
        => new(plan, solution, payload, SelectedAnswerId: null, CorrectAnswerId: null);

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

    [Fact]
    public void ValidateDefinition_LineStyleWithoutUnit_Accepted()
    {
        const string plan =
            """{"version":1,"type":"barChart","style":"line","max":50,"step":10,"categories":[{"id":"c1","label":"Pzt"},{"id":"c2","label":"Sal"}]}""";
        Grader.ValidateDefinition(plan, """{"values":{"c1":20,"c2":40}}""").ShouldBeTrue();
        // unit yoksa adım step: 25 step katı değil.
        Grader.ValidateDefinition(plan, """{"values":{"c1":25,"c2":40}}""").ShouldBeFalse();
    }

    [Fact]
    public void ValidateDefinition_DotStyle_LimitsDots()
    {
        // Nokta grafiği (dilim d): her unit bir nokta, en çok 20.
        const string plan =
            """{"version":1,"type":"barChart","style":"dot","max":8,"step":2,"unit":1,"categories":[{"id":"c1","label":"1"},{"id":"c2","label":"2"}]}""";
        Grader.ValidateDefinition(plan, """{"values":{"c1":3,"c2":5}}""").ShouldBeTrue();
        Grader.ValidateDefinition(
            """{"version":1,"type":"barChart","style":"dot","max":30,"step":5,"unit":1,"categories":[{"id":"c1","label":"1"},{"id":"c2","label":"2"}]}""",
            null).ShouldBeFalse();
        // Aynı eksen sütun grafiğinde geçerli.
        Grader.ValidateDefinition(
            """{"version":1,"type":"barChart","max":30,"step":5,"unit":1,"categories":[{"id":"c1","label":"1"},{"id":"c2","label":"2"}]}""",
            null).ShouldBeTrue();
    }

    [Theory]
    // Eksen: max sınır dışı, step max'ı bölmüyor, çok aralık, unit step'i bölmüyor, çok adım.
    [InlineData("""{"version":1,"type":"barChart","max":0,"step":1,"categories":[{"id":"c1","label":"a"},{"id":"c2","label":"b"}]}""")]
    [InlineData("""{"version":1,"type":"barChart","max":1001,"step":1001,"categories":[{"id":"c1","label":"a"},{"id":"c2","label":"b"}]}""")]
    [InlineData("""{"version":1,"type":"barChart","max":10,"step":3,"categories":[{"id":"c1","label":"a"},{"id":"c2","label":"b"}]}""")]
    [InlineData("""{"version":1,"type":"barChart","max":21,"step":1,"categories":[{"id":"c1","label":"a"},{"id":"c2","label":"b"}]}""")]
    [InlineData("""{"version":1,"type":"barChart","max":10,"step":2,"unit":3,"categories":[{"id":"c1","label":"a"},{"id":"c2","label":"b"}]}""")]
    [InlineData("""{"version":1,"type":"barChart","max":200,"step":10,"unit":1,"categories":[{"id":"c1","label":"a"},{"id":"c2","label":"b"}]}""")]
    // Kategori: tek / aynı id / boş etiket / uzun / kontrol karakteri.
    [InlineData("""{"version":1,"type":"barChart","max":10,"step":1,"categories":[{"id":"c1","label":"a"}]}""")]
    [InlineData("""{"version":1,"type":"barChart","max":10,"step":1,"categories":[{"id":"c1","label":"a"},{"id":"c1","label":"b"}]}""")]
    [InlineData("""{"version":1,"type":"barChart","max":10,"step":1,"categories":[{"id":"c1","label":"a"},{"id":"c2","label":" "}]}""")]
    [InlineData("""{"version":1,"type":"barChart","max":10,"step":1,"categories":[{"id":"c1","label":"a"},{"id":"c2","label":"0123456789012345678901234567890"}]}""")]
    [InlineData("""{"version":1,"type":"barChart","max":10,"step":1,"categories":[{"id":"c1","label":"a"},{"id":"c2","label":"a‮b"}]}""")]
    // Sabit: bilinmeyen kategori / unit katı değil / hepsi sabit.
    [InlineData("""{"version":1,"type":"barChart","max":10,"step":2,"categories":[{"id":"c1","label":"a"},{"id":"c2","label":"b"}],"fixed":{"c9":2}}""")]
    [InlineData("""{"version":1,"type":"barChart","max":10,"step":2,"categories":[{"id":"c1","label":"a"},{"id":"c2","label":"b"}],"fixed":{"c1":3}}""")]
    [InlineData("""{"version":1,"type":"barChart","max":10,"step":2,"categories":[{"id":"c1","label":"a"},{"id":"c2","label":"b"}],"fixed":{"c1":2,"c2":4}}""")]
    // Görünüm / başlık / bilinmeyen anahtar / sürüm.
    [InlineData("""{"version":1,"type":"barChart","style":"pie","max":10,"step":1,"categories":[{"id":"c1","label":"a"},{"id":"c2","label":"b"}]}""")]
    [InlineData("""{"version":1,"type":"barChart","title":"012345678901234567890123456789012345678901234567890123456789012345678901234567890","max":10,"step":1,"categories":[{"id":"c1","label":"a"},{"id":"c2","label":"b"}]}""")]
    [InlineData("""{"version":1,"type":"barChart","max":10,"step":1,"categories":[{"id":"c1","label":"a"},{"id":"c2","label":"b"}],"values":{}}""")]
    [InlineData("""{"version":2,"type":"barChart","max":10,"step":1,"categories":[{"id":"c1","label":"a"},{"id":"c2","label":"b"}]}""")]
    public void ValidateDefinition_BadPlan_Rejected(string plan)
        => Grader.ValidateDefinition(plan, null).ShouldBeFalse();

    [Theory]
    // Eksik kategori / sabit kategori yazılmış / bilinmeyen / sınır dışı / unit katı değil / sayı değil / fazla anahtar.
    [InlineData("""{"values":{"c2":4,"c3":7}}""")]
    [InlineData("""{"values":{"c1":6,"c2":4,"c3":7,"c4":0}}""")]
    [InlineData("""{"values":{"c2":4,"c3":7,"c4":0,"c9":1}}""")]
    [InlineData("""{"values":{"c2":12,"c3":7,"c4":0}}""")]
    [InlineData("""{"values":{"c2":-1,"c3":7,"c4":0}}""")]
    [InlineData("""{"values":{"c2":"4","c3":7,"c4":0}}""")]
    [InlineData("""{"values":{"c2":4,"c3":7,"c4":0},"x":1}""")]
    public void ValidateDefinition_BadSolution_Rejected(string solution)
        => Grader.ValidateDefinition(Plan, solution).ShouldBeFalse();

    [Fact]
    public void ValidateDefinition_SolutionNotUnitMultiple_Rejected()
    {
        const string plan = """{"version":1,"type":"barChart","max":10,"step":2,"categories":[{"id":"c1","label":"a"},{"id":"c2","label":"b"}]}""";
        Grader.ValidateDefinition(plan, """{"values":{"c1":2,"c2":3}}""").ShouldBeFalse();
    }

    // ---- Payload ----

    [Fact]
    public void Validate_Payload()
    {
        Grader.Validate(Plan, null).ShouldBeTrue();
        Grader.Validate(Plan, Payload(new { c2 = 4 })).ShouldBeTrue();
        Grader.Validate(Plan, Payload(new { c2 = 0, c3 = 10 })).ShouldBeTrue();
        Grader.Validate(Plan, Payload(new { c1 = 4 })).ShouldBeFalse(); // sabit sütun
        Grader.Validate(Plan, Payload(new { c9 = 4 })).ShouldBeFalse();
        Grader.Validate(Plan, Payload(new { c2 = 11 })).ShouldBeFalse();
        Grader.Validate(Plan, Payload(new { c2 = -1 })).ShouldBeFalse();
        Grader.Validate(Plan, """{"version":1,"type":"barChart","values":{"c2":"4"}}""").ShouldBeFalse();
        Grader.Validate(Plan, """{"version":1,"type":"barChart","values":{"c2":4},"x":1}""").ShouldBeFalse();
        Grader.Validate(Plan, """{"version":1,"type":"numberLine","values":{}}""").ShouldBeFalse();
    }

    [Fact]
    public void Normalize_EmptyValues_IsNoAnswer()
    {
        Grader.Normalize(Payload(new { })).ShouldBeNull();
        var payload = Payload(new { c2 = 0 });
        Grader.Normalize(payload).ShouldBe(payload);
    }

    // ---- Kontrol ----

    [Fact]
    public void Grade_AllCorrect_UndrawnColumnIsZero()
    {
        var result = Grader.Grade(Input(Solution, Payload(new { c2 = 4, c3 = 7 })));
        result.CorrectCount.ShouldBe(3);
        result.TotalCount.ShouldBe(3);
        Rows(result.Feedback).ShouldBe([("c2", true), ("c3", true), ("c4", true)]);
    }

    [Fact]
    public void Grade_WrongValues()
    {
        var result = Grader.Grade(Input(Solution, Payload(new { c2 = 4, c3 = 6, c4 = 1 })));
        result.CorrectCount.ShouldBe(1);
        Rows(result.Feedback).ShouldBe([("c2", true), ("c3", false), ("c4", false)]);
    }

    [Fact]
    public void Grade_FeedbackDoesNotLeakTargets()
    {
        var feedback = Grader.Grade(Input(Solution, Payload(new { c2 = 1 }))).Feedback!;
        feedback.ShouldNotContain("values");
        feedback.ShouldNotContain(":7");
        feedback.ShouldNotContain(":4");
    }

    [Fact]
    public void Grade_NoSolution_Ungradable()
    {
        Grader.Grade(Input(null, Payload(new { c2 = 4 }))).TotalCount.ShouldBe(0);
        Grader.Grade(Input("""{"values":{"c1":6}}""", Payload(new { c2 = 4 }))).TotalCount.ShouldBe(0); // yalnız sabit sütun
    }

    // ---- Analiz ----

    [Fact]
    public void Analyze_ItemsPerFreeCategory()
    {
        var items = Grader.Analyze(Input(Solution, Payload(new { c2 = 4, c3 = 8 })));
        items.Count.ShouldBe(3);
        items[0].ItemLabel.ShouldBe("Armut");
        items[0].Correct.ShouldBeTrue();
        items[0].AnswerKey.ShouldBe("4");
        items[1].Correct.ShouldBeFalse();
        items[1].AnswerLabel.ShouldBe("8");
        items[2].AnswerKey.ShouldBe("0"); // çizilmeyen sütun 0
        items[2].Correct.ShouldBeTrue();

        var blank = Grader.Analyze(Input(Solution, null));
        blank[0].AnswerKey.ShouldBeNull();
        blank[0].Correct.ShouldBeFalse();
    }

    [Fact]
    public void Registry_ResolvesBarChart()
        => InteractionGraderRegistry.Default.Resolve("barChart").ShouldBeOfType<BarChartGrader>();
}
