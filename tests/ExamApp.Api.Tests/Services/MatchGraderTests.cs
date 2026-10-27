using System.Text.Json;
using System.Text.Json.Nodes;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #576: <see cref="MatchGrader"/> — payload doğrulama (katı: bir sol/sağ bir kez, örnek çift kullanılamaz), normalize,
/// sol başına puanlama (plan sırası, örnek çift hariç, bağsız sol yanlış), çeldirici sağ, çözüm sızıntısı yok, çözümsüz soru
/// puanlanamaz ve kayıtta tanım doğrulaması.
/// </summary>
public class MatchGraderTests
{
    // l1–r1 örnek çift; l2 → r3, l3 → r2, l4 → r4; r5 çeldirici.
    private const string Plan =
        """{"version":1,"type":"match","left":[{"id":"l1","label":"Kalem"},{"id":"l2","box":{"x":0,"y":0,"width":5,"height":5}},{"id":"l3","label":"Top"},{"id":"l4","label":"Ağaç"}],"right":[{"id":"r1","label":"Gölge 1"},{"id":"r2","box":{"x":10,"y":0,"width":5,"height":5}},{"id":"r3","label":"Gölge 3"},{"id":"r4","label":"Gölge 4"},{"id":"r5","label":"Çeldirici"}],"prelinked":[{"leftId":"l1","rightId":"r1"}]}""";

    private const string Solution = """{"l2":"r3","l3":"r2","l4":"r4"}""";

    private static readonly MatchGrader Grader = new();

    private static string Payload(params (string L, string R)[] pairs) => JsonSerializer.Serialize(new
    {
        version = 1, type = "match",
        pairs = pairs.Select(p => new { leftId = p.L, rightId = p.R }),
    });

    private static InteractionGradeInput Input(string? payload, string? solution = Solution, string? plan = Plan)
        => new(plan, solution, payload, SelectedAnswerId: null, CorrectAnswerId: null);

    private static List<(string Id, bool Correct)> Rows(string? feedback)
    {
        feedback.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(feedback!);
        return doc.RootElement.GetProperty("rows").EnumerateArray()
            .Select(r => (r.GetProperty("id").GetString()!, r.GetProperty("correct").GetBoolean()))
            .ToList();
    }

    [Fact]
    public void Grade_AllMatched_IsCorrectInPlanOrderWithoutPrelinked()
    {
        var result = Grader.Grade(Input(Payload(("l4", "r4"), ("l2", "r3"), ("l3", "r2"))));

        result.IsCorrect.ShouldBeTrue();
        result.CorrectCount.ShouldBe(3);
        result.TotalCount.ShouldBe(3);
        Rows(result.Feedback).ShouldBe(new[] { ("l2", true), ("l3", true), ("l4", true) });
    }

    [Fact]
    public void Grade_WrongAndDistractorAndUnpaired_AreWrong()
    {
        // l2 ↔ r2 (yanlış), l3 ↔ r5 (çeldirici), l4 bağsız.
        var result = Grader.Grade(Input(Payload(("l2", "r2"), ("l3", "r5"))));

        result.IsCorrect.ShouldBeFalse();
        result.CorrectCount.ShouldBe(0);
        result.TotalCount.ShouldBe(3);
        Rows(result.Feedback).ShouldBe(new[] { ("l2", false), ("l3", false), ("l4", false) });
    }

    [Fact]
    public void Grade_NoPayload_EveryLeftWrong()
    {
        var result = Grader.Grade(Input(null));

        result.TotalCount.ShouldBe(3);
        Rows(result.Feedback).ShouldAllBe(r => !r.Correct);
    }

    [Fact]
    public void Grade_FeedbackNeverContainsRightIds()
    {
        var result = Grader.Grade(Input(Payload(("l2", "r2"))));

        foreach (var secret in new[] { "r1", "r2", "r3", "r4", "r5", "Gölge" })
            result.Feedback!.ShouldNotContain(secret);
        using var doc = JsonDocument.Parse(result.Feedback!);
        foreach (var row in doc.RootElement.GetProperty("rows").EnumerateArray())
            row.EnumerateObject().Select(p => p.Name).ShouldBe(new[] { "id", "correct" });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{broken")]
    [InlineData("""["r1"]""")]
    public void Grade_NoUsableSolution_IsUngradable(string? solution)
    {
        Grader.Grade(Input(Payload(("l2", "r3")), solution)).ShouldBe(InteractionGradeResult.Ungradable);
    }

    [Fact]
    public void Grade_UnreadablePlan_IsUngradable()
    {
        Grader.Grade(Input(Payload(("l2", "r3")), Solution, "{broken")).ShouldBe(InteractionGradeResult.Ungradable);
        Grader.Grade(Input(Payload(("l2", "r3")), Solution, null)).ShouldBe(InteractionGradeResult.Ungradable);
    }

    [Fact]
    public void Validate_ValidPayloads()
    {
        Grader.Validate(Plan, null).ShouldBeTrue();
        Grader.Validate(Plan, Payload()).ShouldBeTrue();
        Grader.Validate(Plan, Payload(("l2", "r5"), ("l3", "r2"))).ShouldBeTrue();
    }

    [Theory]
    [InlineData("""{"version":1,"type":"match","pairs":[{"leftId":"l1","rightId":"r2"}]}""")]                               // örnek sol
    [InlineData("""{"version":1,"type":"match","pairs":[{"leftId":"l2","rightId":"r1"}]}""")]                               // örnek sağ
    [InlineData("""{"version":1,"type":"match","pairs":[{"leftId":"l2","rightId":"r2"},{"leftId":"l2","rightId":"r3"}]}""")] // sol iki kez
    [InlineData("""{"version":1,"type":"match","pairs":[{"leftId":"l2","rightId":"r2"},{"leftId":"l3","rightId":"r2"}]}""")] // sağ iki kez
    [InlineData("""{"version":1,"type":"match","pairs":[{"leftId":"l9","rightId":"r2"}]}""")]                               // plan dışı sol
    [InlineData("""{"version":1,"type":"match","pairs":[{"leftId":"l2","rightId":"r9"}]}""")]                               // plan dışı sağ
    [InlineData("""{"version":1,"type":"match","pairs":[{"leftId":"l2","rightId":"r2","ok":true}]}""")]                     // bilinmeyen alan
    [InlineData("""{"version":1,"type":"match","pairs":[{"leftId":"l2"}]}""")]                                              // eksik alan
    [InlineData("""{"version":1,"type":"match","pairs":[{"leftId":"prototype","rightId":"r2"}]}""")]                        // ayrılmış id
    [InlineData("""{"version":1,"type":"match","pairs":{}}""")]
    [InlineData("""{"version":1,"type":"dragPlace","placements":[]}""")]
    [InlineData("""{"version":1,"type":"match","pairs":[],"pairs":[]}""")]
    [InlineData("{broken")]
    public void Validate_InvalidPayload_IsRejected(string payload)
    {
        Grader.Validate(Plan, payload).ShouldBeFalse();
    }

    [Fact]
    public void Validate_OversizedPayload_IsRejected()
    {
        var huge = """{"version":1,"type":"match","pairs":[],"pad":""" + "\"" + new string('a', InteractionPayload.MaxLength) + "\"}";
        Grader.Validate(Plan, huge).ShouldBeFalse();
    }

    [Fact]
    public void Normalize_EmptyPairs_IsNull()
    {
        Grader.Normalize(Payload()).ShouldBeNull();
        var payload = Payload(("l2", "r3"));
        Grader.Normalize(payload).ShouldBe(payload);
    }

    [Fact]
    public void ValidateDefinition_ValidPlanAndSolution_Pass()
    {
        Grader.ValidateDefinition(Plan, Solution).ShouldBeTrue();
        Grader.ValidateDefinition(Plan, null).ShouldBeTrue();
        // prelinked isteğe bağlı.
        Grader.ValidateDefinition(
            """{"version":1,"type":"match","left":[{"id":"a","label":"x"}],"right":[{"id":"b","label":"y"}]}""",
            """{"a":"b"}""").ShouldBeTrue();
    }

    [Theory]
    [InlineData("""{"version":1,"type":"match","left":[{"id":"a","label":"x"}],"right":[{"id":"b","label":"y"}],"answers":{}}""")]                   // kök gizli alan
    [InlineData("""{"version":1,"type":"match","left":[{"id":"a","label":"x","match":"b"}],"right":[{"id":"b","label":"y"}]}""")]                    // öğede gizli alan
    [InlineData("""{"version":1,"type":"match","left":[{"id":"a"}],"right":[{"id":"b","label":"y"}]}""")]                                            // etiketsiz kutusuz
    [InlineData("""{"version":1,"type":"match","left":[{"id":"a","box":{"x":0,"y":0,"width":1}}],"right":[{"id":"b","label":"y"}]}""")]             // kutu eksik
    [InlineData("""{"version":1,"type":"match","left":[],"right":[{"id":"b","label":"y"}]}""")]                                                      // solsuz
    [InlineData("""{"version":1,"type":"match","left":[{"id":"a","label":"x"}],"right":[]}""")]                                                      // sağsız
    [InlineData("""{"version":1,"type":"match","left":[{"id":"a","label":"x"},{"id":"a","label":"y"}],"right":[{"id":"b","label":"y"}]}""")]         // yinelenen sol
    [InlineData("""{"version":1,"type":"match","left":[{"id":"a","label":"x"}],"right":[{"id":"a","label":"y"}]}""")]                                // sol/sağ çakışması
    [InlineData("""{"version":1,"type":"match","left":[{"id":"a","label":"x"}],"right":[{"id":"b","label":"y"}],"prelinked":[{"leftId":"a","rightId":"b"}]}""")] // hepsi örnek
    [InlineData("""{"version":1,"type":"match","left":[{"id":"a","label":"x"},{"id":"c","label":"z"}],"right":[{"id":"b","label":"y"}],"prelinked":[{"leftId":"a","rightId":"q"}]}""")] // örnekte plan dışı sağ
    [InlineData("""{"version":1,"type":"match","left":[{"id":"a","label":"x"},{"id":"c","label":"z"}],"right":[{"id":"b","label":"y"},{"id":"d","label":"w"}],"prelinked":[{"leftId":"a","rightId":"b"},{"leftId":"a","rightId":"d"}]}""")] // örnekte sol iki kez
    [InlineData("""{"version":1,"type":"match","left":[{"id":"a","label":"x"},{"id":"c","label":"z"}],"right":[{"id":"b","label":"y"}],"prelinked":[{"leftId":"a","rightId":"b","note":1}]}""")] // örnekte gizli alan
    [InlineData("""{"version":1,"type":"match","left":[{"id":"a","label":"x"}],"right":[{"id":"b","label":"y"}],"prelinked":{}}""")]                 // örnek dizi değil
    [InlineData("""{"version":1,"type":"slotFill","left":[{"id":"a","label":"x"}],"right":[{"id":"b","label":"y"}]}""")]
    [InlineData(null)]
    public void ValidateDefinition_InvalidPlan_IsRejected(string? plan)
    {
        Grader.ValidateDefinition(plan, null).ShouldBeFalse();
    }

    [Fact]
    public void ValidateDefinition_Limits_30PerSide()
    {
        static string PlanOf(int left, int right) =>
            "{\"version\":1,\"type\":\"match\",\"left\":["
            + string.Join(",", Enumerable.Range(1, left).Select(i => "{\"id\":\"l" + i + "\",\"label\":\"k\"}"))
            + "],\"right\":[" + string.Join(",", Enumerable.Range(1, right).Select(i => "{\"id\":\"r" + i + "\",\"label\":\"k\"}")) + "]}";

        Grader.ValidateDefinition(PlanOf(30, 30), null).ShouldBeTrue();
        Grader.ValidateDefinition(PlanOf(31, 30), null).ShouldBeFalse();
        Grader.ValidateDefinition(PlanOf(30, 31), null).ShouldBeFalse();
    }

    [Theory]
    [InlineData("""{"l2":"r3","l3":"r2"}""")]                   // l4 çözümsüz
    [InlineData("""{"l1":"r1","l2":"r3","l3":"r2","l4":"r4"}""")] // örnek sol çözümde
    [InlineData("""{"l2":"r1","l3":"r2","l4":"r4"}""")]           // örnek sağ çözümde
    [InlineData("""{"l2":"r3","l3":"r3","l4":"r4"}""")]           // sağ iki kez
    [InlineData("""{"l2":"r9","l3":"r2","l4":"r4"}""")]           // plan dışı sağ
    [InlineData("""{"l2":"r3","l3":"r2","l4":"r4","l9":"r5"}""")] // plan dışı sol
    [InlineData("""{"l2":["r3"],"l3":"r2","l4":"r4"}""")]         // string değil
    [InlineData("""{"l2":"r3","l2":"r3","l3":"r2","l4":"r4"}""")] // yinelenen anahtar
    public void ValidateDefinition_InconsistentSolution_IsRejected(string solution)
    {
        Grader.ValidateDefinition(Plan, solution).ShouldBeFalse();
    }

    [Fact]
    public void Registry_ResolvesMatchAndCanonicalizes()
    {
        InteractionGraderRegistry.Default.Resolve("Match").ShouldBeOfType<MatchGrader>();
        InteractionGraderRegistry.Default.CanonicalType("MATCH").ShouldBe(MatchGrader.Type);
    }

    // ---------------------------------------------------------------- review #576: kutu sınırları

    [Theory]
    [InlineData("""{"x":-1,"y":0,"width":5,"height":5}""")]
    [InlineData("""{"x":0,"y":0,"width":0.5,"height":5}""")]
    [InlineData("""{"x":0,"y":99999,"width":5,"height":2}""")]
    public void ValidateDefinition_BoxOutOfBounds_IsRejected(string box)
    {
        Grader.ValidateDefinition(
            """{"version":1,"type":"match","left":[{"id":"a","box":BOX}],"right":[{"id":"b","label":"y"}]}""".Replace("BOX", box), null)
            .ShouldBeFalse();
        Grader.ValidateDefinition(
            """{"version":1,"type":"match","left":[{"id":"a","label":"x"}],"right":[{"id":"b","box":BOX}]}""".Replace("BOX", box), null)
            .ShouldBeFalse();
    }

    // ---------------------------------------------------------------- review #576 (H1/M1): PrepareForStorage

    // Sağ sütun çift sırasıyla yapıştırılmış (l_i ↔ r_i): sıra ve id çözümü ele veriyor. l1–r1 örnek çift.
    private static readonly string PairOrderPlan =
        "{\"version\":1,\"type\":\"match\",\"left\":["
        + string.Join(",", Enumerable.Range(1, 8).Select(i => "{\"id\":\"l" + i + "\",\"label\":\"Afet " + i + "\"}"))
        + "],\"right\":[" + string.Join(",", Enumerable.Range(1, 9).Select(i => "{\"id\":\"r" + i + "\",\"label\":\"Ad " + i + " çğ\"}"))
        + "],\"prelinked\":[{\"leftId\":\"l1\",\"rightId\":\"r1\"}]}";

    private static readonly string PairOrderSolution =
        "{" + string.Join(",", Enumerable.Range(2, 7).Select(i => "\"l" + i + "\":\"r" + i + "\"")) + "}";

    private static (List<string> Ids, List<string> Labels) Rights(string plan)
    {
        var right = JsonNode.Parse(plan)!["right"]!.AsArray();
        return (right.Select(c => c!["id"]!.GetValue<string>()).ToList(), right.Select(c => c!["label"]!.GetValue<string>()).ToList());
    }

    [Fact]
    public void PrepareForStorage_ShufflesRightAndRemapsSolutionAndPrelinked()
    {
        var grader = new MatchGrader(new SeededInteractionRandom(576));

        var (plan, solution) = grader.PrepareForStorage(PairOrderPlan, PairOrderSolution);

        var (ids, labels) = Rights(plan!);
        labels.ShouldNotBe(Rights(PairOrderPlan).Labels);
        labels.OrderBy(l => l, StringComparer.Ordinal).ShouldBe(Rights(PairOrderPlan).Labels.OrderBy(l => l, StringComparer.Ordinal));
        ids.ShouldAllBe(id => System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z0-9]{8}$"));
        ids.Distinct().Count().ShouldBe(9);
        JsonNode.Parse(plan!)!["left"]!.ToJsonString().ShouldBe(JsonNode.Parse(PairOrderPlan)!["left"]!.ToJsonString());
        plan!.ShouldContain("çğ");
        grader.ValidateDefinition(plan, solution).ShouldBeTrue();

        // Örnek çift ve çözüm aynı etiketlere işaret eder.
        string LabelOf(string id) => labels[ids.IndexOf(id)];
        var prelinkedRight = JsonNode.Parse(plan!)!["prelinked"]![0]!["rightId"]!.GetValue<string>();
        LabelOf(prelinkedRight).ShouldBe("Ad 1 çğ");
        var sol = JsonNode.Parse(solution!)!.AsObject();
        foreach (var i in Enumerable.Range(2, 7))
            LabelOf(sol["l" + i]!.GetValue<string>()).ShouldBe("Ad " + i + " çğ");
    }

    [Fact]
    public void PrepareForStorage_SolutionStillGradesTheSameAnswerIdentically()
    {
        var grader = new MatchGrader(new SeededInteractionRandom(9));
        var (plan, solution) = grader.PrepareForStorage(PairOrderPlan, PairOrderSolution);
        var (oldIds, oldLabels) = Rights(PairOrderPlan);
        var (newIds, newLabels) = Rights(plan!);
        string Map(string old) => newIds[newLabels.IndexOf(oldLabels[oldIds.IndexOf(old)])];

        var authored = new[] { ("l2", "r2"), ("l3", "r9"), ("l4", "r5"), ("l5", "r4") };
        var before = Grader.Grade(Input(Payload(authored), PairOrderSolution, PairOrderPlan));
        var after = grader.Grade(Input(Payload(authored.Select(p => (p.Item1, Map(p.Item2))).ToArray()), solution, plan));

        (after.IsCorrect, after.CorrectCount, after.TotalCount).ShouldBe((before.IsCorrect, before.CorrectCount, before.TotalCount));
        after.Feedback.ShouldBe(before.Feedback);   // satır id'leri sol id'leri, değişmez
        var full = Payload(Enumerable.Range(2, 7).Select(i => ("l" + i, Map("r" + i))).ToArray());
        grader.Grade(Input(full, solution, plan)).IsCorrect.ShouldBeTrue();
    }

    [Fact]
    public void PrepareForStorage_DeterministicPerSeed_UnreadableReturnedAsIs()
    {
        new MatchGrader(new SeededInteractionRandom(4)).PrepareForStorage(PairOrderPlan, PairOrderSolution)
            .ShouldBe(new MatchGrader(new SeededInteractionRandom(4)).PrepareForStorage(PairOrderPlan, PairOrderSolution));
        Grader.PrepareForStorage("{broken", Solution).ShouldBe(("{broken", Solution));
        Grader.PrepareForStorage(null, null).ShouldBe(((string?)null, (string?)null));
    }
}
