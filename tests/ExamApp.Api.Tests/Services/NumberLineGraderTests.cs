using System.Text.Json;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #588: <see cref="NumberLineGrader"/> — kayıt kuralları (bölme, tolerans, etiket, kart, çözüm), payload doğrulama, çentik /
/// tolerans kontrolü, çeldirici kart, çözüm sızıntısı yok, analiz, çözümsüz soru.
/// </summary>
public class NumberLineGraderTests
{
    // Matematik Ustası s.106: 0–1 arası 8 bölme; 3/8 ve 1/2 kartı, 5/4 çeldirici.
    private const string Plan =
        """{"version":1,"type":"numberLine","divisions":8,"labels":[{"at":0,"text":"0"},{"at":8,"text":"1"}],"cards":[{"id":"k1","label":"3/8"},{"id":"k2","label":"1/2"},{"id":"k3","label":"5/4"}]}""";

    private const string Solution = """{"positions":{"k1":3,"k2":4,"k3":null}}""";

    private static readonly NumberLineGrader Grader = new();

    private static string Payload(object placed) => JsonSerializer.Serialize(new { version = 1, type = "numberLine", placed });

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
    public void ValidateDefinition_ProbabilitySpectrum_Accepted()
    {
        const string plan =
            """{"version":1,"type":"numberLine","divisions":4,"labels":[{"at":0,"text":"imkânsız"},{"at":2,"text":"eşit"},{"at":4,"text":"kesin"}],"cards":[{"id":"k1","label":"Zarın 7 gelmesi"},{"id":"k2","label":"Yazı gelmesi"}]}""";
        Grader.ValidateDefinition(plan, """{"positions":{"k1":0,"k2":2}}""").ShouldBeTrue();
    }

    [Fact]
    public void ValidateDefinition_EstimationWithTolerance_Accepted()
    {
        const string plan =
            """{"version":1,"type":"numberLine","divisions":100,"tolerance":3,"hideTicks":true,"labels":[{"at":0,"text":"0"},{"at":100,"text":"1"}],"cards":[{"id":"k1","label":"0,37"}]}""";
        Grader.ValidateDefinition(plan, """{"positions":{"k1":37}}""").ShouldBeTrue();
    }

    [Theory]
    // Bölme sınır dışı.
    [InlineData("""{"version":1,"type":"numberLine","divisions":0,"cards":[{"id":"k1","label":"a"}]}""")]
    [InlineData("""{"version":1,"type":"numberLine","divisions":101,"cards":[{"id":"k1","label":"a"}]}""")]
    // Tolerans sınır dışı ya da bölmeden büyük.
    [InlineData("""{"version":1,"type":"numberLine","divisions":8,"tolerance":11,"cards":[{"id":"k1","label":"a"}]}""")]
    [InlineData("""{"version":1,"type":"numberLine","divisions":2,"tolerance":2,"cards":[{"id":"k1","label":"a"}]}""")]
    // Etiket çentik dışında / aynı çentikte iki etiket / boş / kontrol karakteri / uzun.
    [InlineData("""{"version":1,"type":"numberLine","divisions":8,"labels":[{"at":9,"text":"x"}],"cards":[{"id":"k1","label":"a"}]}""")]
    [InlineData("""{"version":1,"type":"numberLine","divisions":8,"labels":[{"at":1,"text":"x"},{"at":1,"text":"y"}],"cards":[{"id":"k1","label":"a"}]}""")]
    [InlineData("""{"version":1,"type":"numberLine","divisions":8,"labels":[{"at":1,"text":""}],"cards":[{"id":"k1","label":"a"}]}""")]
    [InlineData("""{"version":1,"type":"numberLine","divisions":8,"labels":[{"at":1,"text":"a‮b"}],"cards":[{"id":"k1","label":"a"}]}""")]
    [InlineData("""{"version":1,"type":"numberLine","divisions":8,"labels":[{"at":1,"text":"0123456789012345678901234567890"}],"cards":[{"id":"k1","label":"a"}]}""")]
    // Kart yok / aynı id / boş etiket.
    [InlineData("""{"version":1,"type":"numberLine","divisions":8,"cards":[]}""")]
    [InlineData("""{"version":1,"type":"numberLine","divisions":8,"cards":[{"id":"k1","label":"a"},{"id":"k1","label":"b"}]}""")]
    [InlineData("""{"version":1,"type":"numberLine","divisions":8,"cards":[{"id":"k1","label":"  "}]}""")]
    // Bilinmeyen anahtar / hideTicks bool değil / sürüm.
    [InlineData("""{"version":1,"type":"numberLine","divisions":8,"cards":[{"id":"k1","label":"a"}],"start":0}""")]
    [InlineData("""{"version":1,"type":"numberLine","divisions":8,"hideTicks":1,"cards":[{"id":"k1","label":"a"}]}""")]
    [InlineData("""{"version":2,"type":"numberLine","divisions":8,"cards":[{"id":"k1","label":"a"}]}""")]
    public void ValidateDefinition_BadPlan_Rejected(string plan)
        => Grader.ValidateDefinition(plan, null).ShouldBeFalse();

    [Fact]
    public void ValidateDefinition_TooManyCards_Rejected()
    {
        var cards = string.Join(",", Enumerable.Range(1, 21).Select(i => $$"""{"id":"k{{i}}","label":"{{i}}"}"""));
        Grader.ValidateDefinition($$"""{"version":1,"type":"numberLine","divisions":8,"cards":[{{cards}}]}""", null).ShouldBeFalse();
    }

    [Theory]
    // Eksik kart / bilinmeyen kart / çentik dışı / hedefli kart yok / sayı değil.
    [InlineData("""{"positions":{"k1":3,"k2":4}}""")]
    [InlineData("""{"positions":{"k1":3,"k2":4,"k3":null,"k9":1}}""")]
    [InlineData("""{"positions":{"k1":9,"k2":4,"k3":null}}""")]
    [InlineData("""{"positions":{"k1":null,"k2":null,"k3":null}}""")]
    [InlineData("""{"positions":{"k1":"3","k2":4,"k3":null}}""")]
    [InlineData("""{"positions":{"k1":3,"k2":4,"k3":null},"x":1}""")]
    public void ValidateDefinition_BadSolution_Rejected(string solution)
        => Grader.ValidateDefinition(Plan, solution).ShouldBeFalse();

    // ---- Payload ----

    [Fact]
    public void Validate_Payload()
    {
        Grader.Validate(Plan, null).ShouldBeTrue();
        Grader.Validate(Plan, Payload(new { k1 = 3 })).ShouldBeTrue();
        Grader.Validate(Plan, Payload(new { k1 = 3, k2 = 3 })).ShouldBeTrue(); // aynı çentikte iki kart
        Grader.Validate(Plan, Payload(new { k9 = 3 })).ShouldBeFalse();
        Grader.Validate(Plan, Payload(new { k1 = 9 })).ShouldBeFalse();
        Grader.Validate(Plan, Payload(new { k1 = -1 })).ShouldBeFalse();
        Grader.Validate(Plan, """{"version":1,"type":"numberLine","placed":{"k1":"3"}}""").ShouldBeFalse();
        Grader.Validate(Plan, """{"version":1,"type":"numberLine","placed":{"k1":3},"x":1}""").ShouldBeFalse();
        Grader.Validate(Plan, """{"version":1,"type":"regionPaint","placed":{}}""").ShouldBeFalse();
    }

    [Fact]
    public void Normalize_EmptyPlacement_IsNoAnswer()
    {
        Grader.Normalize(Payload(new { })).ShouldBeNull();
        var payload = Payload(new { k1 = 3 });
        Grader.Normalize(payload).ShouldBe(payload);
    }

    // ---- Kontrol ----

    [Fact]
    public void Grade_AllCorrect()
    {
        var result = Grader.Grade(Input(Solution, Payload(new { k1 = 3, k2 = 4 })));
        result.CorrectCount.ShouldBe(3);
        result.TotalCount.ShouldBe(3);
        Rows(result.Feedback).ShouldBe([("k1", true), ("k2", true), ("k3", true)]);
    }

    [Fact]
    public void Grade_WrongTickAndPlacedDistractor()
    {
        var result = Grader.Grade(Input(Solution, Payload(new { k1 = 2, k2 = 4, k3 = 8 })));
        result.CorrectCount.ShouldBe(1);
        Rows(result.Feedback).ShouldBe([("k1", false), ("k2", true), ("k3", false)]);
    }

    [Fact]
    public void Grade_Blank_OnlyDistractorCorrect()
    {
        var result = Grader.Grade(Input(Solution, null));
        result.CorrectCount.ShouldBe(1);
        result.TotalCount.ShouldBe(3);
    }

    [Fact]
    public void Grade_Tolerance()
    {
        const string plan =
            """{"version":1,"type":"numberLine","divisions":100,"tolerance":3,"cards":[{"id":"k1","label":"0,37"}]}""";
        const string solution = """{"positions":{"k1":37}}""";
        Grader.Grade(Input(solution, Payload(new { k1 = 40 }), plan)).CorrectCount.ShouldBe(1);
        Grader.Grade(Input(solution, Payload(new { k1 = 34 }), plan)).CorrectCount.ShouldBe(1);
        Grader.Grade(Input(solution, Payload(new { k1 = 41 }), plan)).CorrectCount.ShouldBe(0);
    }

    [Fact]
    public void Grade_FeedbackDoesNotLeakTargets()
    {
        var feedback = Grader.Grade(Input(Solution, Payload(new { k1 = 2 }))).Feedback!;
        feedback.ShouldNotContain("positions");
        feedback.ShouldNotContain("\"at\"");
        feedback.ShouldNotContain(":3");
    }

    [Fact]
    public void Grade_NoSolution_Ungradable()
    {
        var result = Grader.Grade(Input(null, Payload(new { k1 = 3 })));
        result.TotalCount.ShouldBe(0);
        Grader.Grade(Input("""{"positions":{"k1":null}}""", null)).TotalCount.ShouldBe(0);
    }

    // ---- Analiz ----

    [Fact]
    public void Analyze_ItemsPerCard()
    {
        var items = Grader.Analyze(Input(Solution, Payload(new { k1 = 8, k2 = 4 })));
        items.Count.ShouldBe(3);
        items[0].ItemLabel.ShouldBe("3/8");
        items[0].Correct.ShouldBeFalse();
        items[0].AnswerKey.ShouldBe("8");
        items[0].AnswerLabel.ShouldBe("1"); // çentik etiketi
        items[1].AnswerLabel.ShouldBe("4/8"); // etiketsiz çentik
        items[1].Correct.ShouldBeTrue();
        items[2].AnswerKey.ShouldBeNull();
        items[2].Distractor.ShouldBeTrue();
        items[2].Correct.ShouldBeTrue();
    }

    [Fact]
    public void Registry_ResolvesNumberLine()
        => InteractionGraderRegistry.Default.Resolve("numberLine").ShouldBeOfType<NumberLineGrader>();
}
