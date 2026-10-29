using System.Text.Json;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #632: <see cref="BalanceScaleGrader"/> — kayıt kuralları (değer, sınırlar, tekil id, çözülebilirlik), payload doğrulama, kural
/// tabanlı denge kontrolü (çoklu çözüm, hazır yükler, "hepsini kullan"), geri bildirim (eğim, kalan), analiz, çözümsüz soru.
/// </summary>
public class BalanceScaleGraderTests
{
    // Süper Combo Mat s.203 2a: 5, 9, 10, 14 kg → 5 + 14 = 9 + 10.
    private const string Plan = """
        {"version":1,"type":"balanceScale","unit":"kg","cards":[{"id":"k1","value":5},{"id":"k2","value":9},{"id":"k3","value":10},{"id":"k4","value":14}]}
        """;

    // Hazır yük: solda 7, sağda boş; kartlar 3, 4, 6.
    private const string FixedPlan = """
        {"version":1,"type":"balanceScale","left":[{"id":"f1","value":7,"label":"7 kg"}],"cards":[{"id":"k1","value":3},{"id":"k2","value":4},{"id":"k3","value":6}]}
        """;

    private const string UseAll = """{"useAll":true}""";
    private const string Any = """{"useAll":false}""";

    private static readonly BalanceScaleGrader Grader = new();

    private static string Payload(string[] left, string[] right)
        => JsonSerializer.Serialize(new { version = 1, type = "balanceScale", left, right });

    private static InteractionGradeInput Input(string? solution, string? payload, string plan = Plan)
        => new(plan, solution, payload, SelectedAnswerId: null, CorrectAnswerId: null);

    private static (bool Correct, string Tilt, int? Unused) Feedback(string? feedback)
    {
        feedback.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(feedback!);
        var root = doc.RootElement;
        var row = root.GetProperty("rows").EnumerateArray().Single();
        row.GetProperty("id").GetString().ShouldBe("balance");
        return (row.GetProperty("correct").GetBoolean(), root.GetProperty("tilt").GetString()!,
            root.TryGetProperty("unused", out var unused) ? unused.GetInt32() : null);
    }

    // ---- Kayıt ----

    [Fact]
    public void ValidateDefinition_Pilot_Accepted()
    {
        Grader.ValidateDefinition(Plan, UseAll).ShouldBeTrue();
        Grader.ValidateDefinition(Plan, Any).ShouldBeTrue();
        Grader.ValidateDefinition(Plan, null).ShouldBeTrue();
        Grader.ValidateDefinition(FixedPlan, UseAll).ShouldBeTrue(); // 7 + 3 = 4 + 6
    }

    [Theory]
    // Bilinmeyen alan, sürüm, değer sınır dışı / kesirli, yinelenen id (kart ve hazır yük arasında da), kartsız, uzun etiket / birim,
    // hideTilt bool değil.
    [InlineData("""{"version":1,"type":"balanceScale","cards":[{"id":"k1","value":5}],"x":1}""")]
    [InlineData("""{"version":2,"type":"balanceScale","cards":[{"id":"k1","value":5}]}""")]
    [InlineData("""{"version":1,"type":"balanceScale","cards":[{"id":"k1","value":0}]}""")]
    [InlineData("""{"version":1,"type":"balanceScale","cards":[{"id":"k1","value":10001}]}""")]
    [InlineData("""{"version":1,"type":"balanceScale","cards":[{"id":"k1","value":2.5}]}""")]
    [InlineData("""{"version":1,"type":"balanceScale","cards":[{"id":"k1","value":5},{"id":"k1","value":5}]}""")]
    [InlineData("""{"version":1,"type":"balanceScale","left":[{"id":"k1","value":5}],"cards":[{"id":"k1","value":5}]}""")]
    [InlineData("""{"version":1,"type":"balanceScale","cards":[]}""")]
    [InlineData("""{"version":1,"type":"balanceScale","cards":[{"id":"k1","value":5,"label":"0123456789012345678901234567890"}]}""")]
    [InlineData("""{"version":1,"type":"balanceScale","unit":"kilogramlar","cards":[{"id":"k1","value":5}]}""")]
    [InlineData("""{"version":1,"type":"balanceScale","hideTilt":"yes","cards":[{"id":"k1","value":5}]}""")]
    [InlineData("""{"version":1,"type":"balanceScale","cards":[{"id":"k1","value":5,"label":"a‮b"}]}""")]
    public void ValidateDefinition_BadPlan_Rejected(string plan)
        => Grader.ValidateDefinition(plan, null).ShouldBeFalse();

    [Fact]
    public void ValidateDefinition_TooManyCardsOrFixed_Rejected()
    {
        var cards = string.Join(",", Enumerable.Range(1, 13).Select(i => $$"""{"id":"k{{i}}","value":1}"""));
        Grader.ValidateDefinition($$"""{"version":1,"type":"balanceScale","cards":[{{cards}}]}""", null).ShouldBeFalse();
        var fixedLoads = string.Join(",", Enumerable.Range(1, 7).Select(i => $$"""{"id":"f{{i}}","value":1}"""));
        Grader.ValidateDefinition($$"""{"version":1,"type":"balanceScale","left":[{{fixedLoads}}],"cards":[{"id":"k1","value":1}]}""", null)
            .ShouldBeFalse();
    }

    [Theory]
    // Bilinmeyen alan, useAll eksik / bool değil.
    [InlineData("""{"useAll":true,"x":1}""")]
    [InlineData("""{}""")]
    [InlineData("""{"useAll":"true"}""")]
    public void ValidateDefinition_BadSolution_Rejected(string solution)
        => Grader.ValidateDefinition(Plan, solution).ShouldBeFalse();

    [Fact]
    public void ValidateDefinition_Unsolvable_Rejected()
    {
        // 5, 9, 10, 15: toplam 39 tek → hepsiyle denge yok; ama 5 + 10 = 15 ile (hepsi gerekmezse) var.
        const string plan = """
            {"version":1,"type":"balanceScale","cards":[{"id":"k1","value":5},{"id":"k2","value":9},{"id":"k3","value":10},{"id":"k4","value":15}]}
            """;
        Grader.ValidateDefinition(plan, UseAll).ShouldBeFalse();
        Grader.ValidateDefinition(plan, Any).ShouldBeTrue();
        // Tek kart, hazır yük yok: kart bir kefeye konunca denge bozulur.
        Grader.ValidateDefinition("""{"version":1,"type":"balanceScale","cards":[{"id":"k1","value":5}]}""", Any).ShouldBeFalse();
        // Hazır yük kendi başına dengede ama en az bir kart konmalı.
        Grader.ValidateDefinition(
            """{"version":1,"type":"balanceScale","left":[{"id":"f1","value":5}],"right":[{"id":"f2","value":5}],"cards":[{"id":"k1","value":3}]}""",
            Any).ShouldBeFalse();
        // Tek kart hazır yükü dengeler.
        Grader.ValidateDefinition(
            """{"version":1,"type":"balanceScale","left":[{"id":"f1","value":5}],"cards":[{"id":"k1","value":5}]}""", UseAll).ShouldBeTrue();
    }

    // ---- Payload ----

    [Fact]
    public void Validate_Payload()
    {
        Grader.Validate(Plan, Payload(["k1", "k4"], ["k2", "k3"])).ShouldBeTrue();
        Grader.Validate(Plan, Payload([], [])).ShouldBeTrue();
        Grader.Validate(Plan, null).ShouldBeTrue();
        // Bilinmeyen kart, aynı kart iki kefede, hazır yük kart değil, eksik kefe, bilinmeyen alan.
        Grader.Validate(Plan, Payload(["k9"], [])).ShouldBeFalse();
        Grader.Validate(Plan, Payload(["k1"], ["k1"])).ShouldBeFalse();
        Grader.Validate(FixedPlan, Payload(["f1"], [])).ShouldBeFalse();
        Grader.Validate(Plan, """{"version":1,"type":"balanceScale","left":["k1"]}""").ShouldBeFalse();
        Grader.Validate(Plan, """{"version":1,"type":"balanceScale","left":[],"right":[],"x":1}""").ShouldBeFalse();
    }

    [Fact]
    public void Normalize_EmptyPans_IsNull()
    {
        Grader.Normalize(Payload([], [])).ShouldBeNull();
        var payload = Payload(["k1"], []);
        Grader.Normalize(payload).ShouldBe(payload);
    }

    // ---- Kontrol ----

    [Fact]
    public void Grade_Balanced_UsingAll_Correct()
    {
        var result = Grader.Grade(Input(UseAll, Payload(["k1", "k4"], ["k2", "k3"])));
        result.CorrectCount.ShouldBe(1);
        result.TotalCount.ShouldBe(1);
        Feedback(result.Feedback).ShouldBe((true, "level", 0));
        // Ayna yerleşim de doğru (çoklu çözüm).
        Grader.Grade(Input(UseAll, Payload(["k2", "k3"], ["k4", "k1"]))).CorrectCount.ShouldBe(1);
    }

    [Fact]
    public void Grade_Unbalanced_ReportsTilt()
    {
        var left = Grader.Grade(Input(UseAll, Payload(["k3", "k4"], ["k1", "k2"])));
        left.CorrectCount.ShouldBe(0);
        Feedback(left.Feedback).ShouldBe((false, "left", 0));
        Feedback(Grader.Grade(Input(UseAll, Payload(["k1"], ["k4"]))).Feedback).ShouldBe((false, "right", 2));
    }

    [Fact]
    public void Grade_UseAll_BalancedButCardsLeft_Wrong()
    {
        // 5 kart yok ama 9 + 5 = 14 dengede; useAll'da 10 kg dışarıda kaldı.
        var payload = Payload(["k1", "k2"], ["k4"]);
        Feedback(Grader.Grade(Input(UseAll, payload)).Feedback).ShouldBe((false, "level", 1));
        var any = Grader.Grade(Input(Any, payload));
        any.CorrectCount.ShouldBe(1);
        Feedback(any.Feedback).ShouldBe((true, "level", null));
    }

    [Fact]
    public void Grade_FixedLoads_Counted()
    {
        // Sol 7 + 3 = sağ 4 + 6.
        Grader.Grade(Input(UseAll, Payload(["k1"], ["k2", "k3"]), FixedPlan)).CorrectCount.ShouldBe(1);
        Feedback(Grader.Grade(Input(UseAll, Payload([], ["k1", "k2", "k3"]), FixedPlan)).Feedback).ShouldBe((false, "right", 0));
    }

    [Fact]
    public void Grade_Empty_Wrong()
    {
        var result = Grader.Grade(Input(Any, null));
        result.CorrectCount.ShouldBe(0);
        result.TotalCount.ShouldBe(1);
        Feedback(result.Feedback).ShouldBe((false, "level", null));
    }

    [Fact]
    public void Grade_NoSolution_Ungradable()
    {
        Grader.Grade(Input(null, Payload(["k1"], ["k2"]))).ShouldBe(InteractionGradeResult.Ungradable);
        Grader.Analyze(Input(null, Payload(["k1"], ["k2"]))).ShouldBeEmpty();
    }

    // ---- Analiz ----

    [Fact]
    public void Analyze_MirrorPlacementsShareKey()
    {
        var a = Grader.Analyze(Input(UseAll, Payload(["k1", "k4"], ["k2", "k3"]))).Single();
        var b = Grader.Analyze(Input(UseAll, Payload(["k3", "k2"], ["k4", "k1"]))).Single();
        a.ItemId.ShouldBe("balance");
        a.Correct.ShouldBeTrue();
        b.AnswerKey.ShouldBe(a.AnswerKey);
        a.AnswerLabel.ShouldBe("5 kg + 14 kg | 9 kg + 10 kg");
    }

    [Fact]
    public void Analyze_FixedLoads_SidesNotSwapped_Empty_NoAnswer()
    {
        var a = Grader.Analyze(Input(UseAll, Payload(["k1"], ["k2", "k3"]), FixedPlan)).Single();
        var b = Grader.Analyze(Input(UseAll, Payload(["k2", "k3"], ["k1"]), FixedPlan)).Single();
        a.AnswerKey.ShouldNotBe(b.AnswerKey);
        a.Correct.ShouldBeTrue();
        b.Correct.ShouldBeFalse();
        var empty = Grader.Analyze(Input(UseAll, null)).Single();
        empty.AnswerKey.ShouldBeNull();
        empty.Correct.ShouldBeFalse();
    }
}
