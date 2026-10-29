using System.Text.Json;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #634: <see cref="ClockSetGrader"/> — kayıt kuralları (adım, sınırlar, sabit saat, çözüm adımın katı), payload doğrulama, saat
/// başına kontrol (yelkovan tam, akrep ±7,5°, 12 saatlik kadran), geri bildirim, analiz, çözümsüz soru.
/// </summary>
public class ClockSetGraderTests
{
    // "Başlangıç" sabit 09:20, öğrenci "Bitiş"i (40 dk sonra 10:00) ve 3.45'i kurar.
    private const string Plan = """
        {"version":1,"type":"clockSet","step":5,"clocks":[{"id":"c0","label":"Başlangıç","fixed":"09:20"},{"id":"c1","label":"Bitiş"},{"id":"c2"}]}
        """;

    private const string Solution = """{"times":{"c1":"10:00","c2":"15:45"}}""";

    private static readonly ClockSetGrader Grader = new();

    private static string Payload(object clocks) => JsonSerializer.Serialize(new { version = 1, type = "clockSet", clocks });

    private static InteractionGradeInput Input(string? solution, string? payload, string plan = Plan)
        => new(plan, solution, payload, SelectedAnswerId: null, CorrectAnswerId: null);

    private static Dictionary<string, bool> Rows(string? feedback)
    {
        feedback.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(feedback!);
        doc.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["rows"]);
        return doc.RootElement.GetProperty("rows").EnumerateArray()
            .ToDictionary(r => r.GetProperty("id").GetString()!, r => r.GetProperty("correct").GetBoolean());
    }

    // ---- Kayıt ----

    [Fact]
    public void ValidateDefinition_Pilot_Accepted()
    {
        Grader.ValidateDefinition(Plan, Solution).ShouldBeTrue();
        Grader.ValidateDefinition(Plan, null).ShouldBeTrue();
        Grader.ValidateDefinition("""{"type":"clockSet","clocks":[{"id":"c1"}]}""", """{"times":{"c1":"12:00"}}""").ShouldBeTrue();
    }

    [Theory]
    [InlineData("""{"version":1,"type":"clockSet","clocks":[{"id":"c1"}],"x":1}""")] // bilinmeyen alan
    [InlineData("""{"version":2,"type":"clockSet","clocks":[{"id":"c1"}]}""")] // sürüm
    [InlineData("""{"type":"clockSet","step":10,"clocks":[{"id":"c1"}]}""")] // adım 1/5/15 değil
    [InlineData("""{"type":"clockSet","clocks":[]}""")] // saatsiz
    [InlineData("""{"type":"clockSet","clocks":[{"id":"c1"},{"id":"c1"}]}""")] // yinelenen id
    [InlineData("""{"type":"clockSet","clocks":[{"id":"c1","fixed":"09:00"}]}""")] // serbest saat yok
    [InlineData("""{"type":"clockSet","clocks":[{"id":"c0","fixed":"9:00"},{"id":"c1"}]}""")] // sabit saat biçimi
    [InlineData("""{"type":"clockSet","clocks":[{"id":"c0","fixed":"24:00"},{"id":"c1"}]}""")]
    [InlineData("""{"type":"clockSet","numbers":"yes","clocks":[{"id":"c1"}]}""")] // görünüm bayrağı bool değil
    [InlineData("""{"type":"clockSet","clocks":[{"id":"c1"},{"id":"c2"},{"id":"c3"},{"id":"c4"},{"id":"c5"},{"id":"c6"},{"id":"c7"}]}""")]
    public void ValidateDefinition_InvalidPlan_Rejected(string plan)
        => Grader.ValidateDefinition(plan, null).ShouldBeFalse();

    [Fact]
    public void ValidateDefinition_LongLabel_Rejected()
        => Grader.ValidateDefinition($$"""{"type":"clockSet","clocks":[{"id":"c1","label":"{{new string('a', 41)}}"}]}""", null)
            .ShouldBeFalse();

    [Theory]
    [InlineData("""{"times":{"c1":"10:00"}}""")] // eksik serbest saat
    [InlineData("""{"times":{"c0":"09:20","c1":"10:00","c2":"03:45"}}""")] // sabit saate hedef
    [InlineData("""{"times":{"c1":"10:03","c2":"03:45"}}""")] // adımın katı değil
    [InlineData("""{"times":{"c1":"10:60","c2":"03:45"}}""")]
    [InlineData("""{"times":{"c1":"10:00","c2":"03:45"},"x":1}""")]
    [InlineData("""{"c1":"10:00"}""")]
    public void ValidateDefinition_InvalidSolution_Rejected(string solution)
        => Grader.ValidateDefinition(Plan, solution).ShouldBeFalse();

    // ---- Payload ----

    [Fact]
    public void Validate_Payload()
    {
        Grader.Validate(Plan, null).ShouldBeTrue();
        Grader.Validate(Plan, Payload(new { c1 = new { h = 0, m = 0 } })).ShouldBeTrue();
        Grader.Validate(Plan, Payload(new { c0 = new { h = 0, m = 0 } })).ShouldBeFalse(); // sabit saat oynatılamaz
        Grader.Validate(Plan, Payload(new { c9 = new { h = 0, m = 0 } })).ShouldBeFalse();
        Grader.Validate(Plan, Payload(new { c1 = new { h = 720, m = 0 } })).ShouldBeFalse();
        Grader.Validate(Plan, Payload(new { c1 = new { h = 0, m = 3 } })).ShouldBeFalse(); // adım 5
        Grader.Validate(Plan, Payload(new { c1 = new { h = 0 } })).ShouldBeFalse();
        Grader.Validate(Plan, """{"version":1,"type":"clockSet","clocks":{},"x":1}""").ShouldBeFalse();
    }

    [Fact]
    public void Normalize_EmptyClocks_IsNoAnswer()
    {
        Grader.Normalize(Payload(new { })).ShouldBeNull();
        var payload = Payload(new { c1 = new { h = 0, m = 0 } });
        Grader.Normalize(payload).ShouldBe(payload);
    }

    // ---- Kontrol ----

    [Fact]
    public void Grade_BothHandsRight_Correct_And_TwelveHourDial()
    {
        // 10:00 → akrep 600, yelkovan 0; 15:45 = 3:45 → akrep 225, yelkovan 45.
        var result = Grader.Grade(Input(Solution, Payload(new { c1 = new { h = 600, m = 0 }, c2 = new { h = 225, m = 45 } })));
        result.CorrectCount.ShouldBe(2);
        result.TotalCount.ShouldBe(2);
        Rows(result.Feedback).ShouldBe(new Dictionary<string, bool> { ["c1"] = true, ["c2"] = true });
    }

    [Theory]
    [InlineData(225, 45, true)]
    [InlineData(240, 45, true)] // akrep 4'ün üstünde: +15 birim (7,5°) kabul sınırı
    [InlineData(210, 45, true)]
    [InlineData(241, 45, false)]
    [InlineData(180, 45, false)] // akrep 3'te kalmış — öğretilen hata
    [InlineData(225, 40, false)] // yelkovan yanlış
    [InlineData(45, 15, false)] // akrep/yelkovan yer değiştirmiş
    public void Grade_HourHandTolerance(int hour, int minute, bool expected)
    {
        var result = Grader.Grade(Input(Solution, Payload(new { c2 = new { h = hour, m = minute } })));
        Rows(result.Feedback)["c2"].ShouldBe(expected);
        Rows(result.Feedback)["c1"].ShouldBeFalse(); // kurulmamış saat yanlış
    }

    [Fact]
    public void Grade_ToleranceWrapsAroundTwelve()
    {
        const string plan = """{"type":"clockSet","clocks":[{"id":"c1"}]}""";
        Rows(Grader.Grade(Input("""{"times":{"c1":"12:00"}}""", Payload(new { c1 = new { h = 710, m = 0 } }), plan)).Feedback)["c1"]
            .ShouldBeTrue();
        Rows(Grader.Grade(Input("""{"times":{"c1":"00:00"}}""", Payload(new { c1 = new { h = 5, m = 0 } }), plan)).Feedback)["c1"]
            .ShouldBeTrue();
    }

    [Fact]
    public void Grade_NoAnswer_AllWrong_And_Ungradable()
    {
        var result = Grader.Grade(Input(Solution, null));
        result.CorrectCount.ShouldBe(0);
        result.TotalCount.ShouldBe(2);
        Grader.Grade(Input(null, null)).ShouldBe(InteractionGradeResult.Ungradable);
    }

    // ---- Analiz ----

    [Fact]
    public void Analyze_ShowsTime_UnknownHourWhenHourHandOff()
    {
        var items = Grader.Analyze(Input(Solution, Payload(new { c1 = new { h = 600, m = 0 }, c2 = new { h = 195, m = 45 } })));
        items.Select(i => i.ItemId).ShouldBe(["c1", "c2"]); // sabit saat öğe değil
        items[0].ItemLabel.ShouldBe("Bitiş");
        items[0].AnswerKey.ShouldBe("10:00");
        items[0].Correct.ShouldBeTrue();
        items[1].ItemLabel.ShouldBe("#3");
        items[1].AnswerKey.ShouldBe("?:45"); // akrep 2.45 ile 3.45'in ortasında: ikisinin de aralığında değil
        items[1].Correct.ShouldBeFalse();

        var near = Grader.Analyze(Input(Solution, Payload(new { c2 = new { h = 5, m = 55 } })));
        near.Single(i => i.ItemId == "c2").AnswerKey.ShouldBe("11:55"); // akrep 12'nin hemen sağında, yelkovan 55 → 11.55
        near.Single(i => i.ItemId == "c1").AnswerKey.ShouldBeNull();

        // Akrep tam 3'te + yelkovan 45: 2.45'in kabul sınırında → "2:45" okunur (3.45 hedefi için yanlış).
        Grader.Analyze(Input(Solution, Payload(new { c2 = new { h = 180, m = 45 } }))).Single(i => i.ItemId == "c2").AnswerKey
            .ShouldBe("2:45");
    }
}
