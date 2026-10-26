using System.Text.Json;
using ExamApp.Api.Services.Interactions;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #574: <see cref="MatrixChoiceGrader"/> — payload doğrulama (katı), normalize, satır bazlı puanlama (plan sırası, tüm
/// satırlar, cevapsız satır yanlış), geri bildirimde çözüm sızıntısı yok, çözümsüz soru puanlanamaz ve kayıtta tanım
/// doğrulaması (<see cref="IInteractionGrader.ValidateDefinition"/>). Saf birim testleri: veritabanı yok.
/// </summary>
public class MatrixChoiceGraderTests
{
    // D/Y tablosu: r1 → D, r2 → Y, r3 → D. Satır sırası planda r1, r2, r3.
    private const string Plan =
        """{"version":1,"type":"matrixChoice","multiPerRow":false,"columns":[{"id":"cD","label":"D"},{"id":"cY","label":"Y"}],"rows":[{"id":"r1","label":"Su 100 derecede kaynar.","box":{"x":0,"y":0,"width":10,"height":5},"cells":{"cD":{"x":11,"y":0,"width":3,"height":5},"cY":{"x":15,"y":0,"width":3,"height":5}}},{"id":"r2","box":{"x":0,"y":6,"width":10,"height":5}},{"id":"r3","box":{"x":0,"y":12,"width":10,"height":5}}]}""";

    private const string Solution = """{"r1":["cD"],"r2":["cY"],"r3":["cD"]}""";

    // Çoklu seçim: Katı/Sıvı/Gaz sınıflandırma; r1 → katı+sıvı, r2 → gaz.
    private const string MultiPlan =
        """{"version":1,"type":"matrixChoice","multiPerRow":true,"columns":[{"id":"k","label":"Katı"},{"id":"s","label":"Sıvı"},{"id":"g","label":"Gaz"}],"rows":[{"id":"r1","box":{"x":0,"y":0,"width":10,"height":5}},{"id":"r2","box":{"x":0,"y":6,"width":10,"height":5}}]}""";

    private const string MultiSolution = """{"r1":["k","s"],"r2":["g"]}""";

    private static readonly MatrixChoiceGrader Grader = new();

    private static string Payload(string selections) =>
        "{\"version\":1,\"type\":\"matrixChoice\",\"selections\":" + selections + "}";

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

    // ---------------------------------------------------------------- Grade

    [Fact]
    public void Grade_AllRowsCorrect_IsCorrectWithEveryRowCorrectInPlanOrder()
    {
        var result = Grader.Grade(Input(Payload("""{"r3":["cD"],"r1":["cD"],"r2":["cY"]}""")));

        result.IsCorrect.ShouldBeTrue();
        result.CorrectCount.ShouldBe(3);
        result.TotalCount.ShouldBe(3);
        Rows(result.Feedback).ShouldBe(new[] { ("r1", true), ("r2", true), ("r3", true) });
    }

    [Fact]
    public void Grade_PartiallyCorrect_MarksOnlyTheWrongRow()
    {
        var result = Grader.Grade(Input(Payload("""{"r1":["cD"],"r2":["cD"],"r3":["cD"]}""")));

        result.IsCorrect.ShouldBeFalse();
        result.CorrectCount.ShouldBe(2);
        result.TotalCount.ShouldBe(3);
        Rows(result.Feedback).ShouldBe(new[] { ("r1", true), ("r2", false), ("r3", true) });
    }

    [Fact]
    public void Grade_UnansweredRows_AreWrongAndStillListed()
    {
        var result = Grader.Grade(Input(Payload("""{"r1":["cD"]}""")));

        result.CorrectCount.ShouldBe(1);
        result.TotalCount.ShouldBe(3);
        Rows(result.Feedback).ShouldBe(new[] { ("r1", true), ("r2", false), ("r3", false) });
    }

    [Fact]
    public void Grade_NoPayload_EveryRowWrong()
    {
        var result = Grader.Grade(Input(null));

        result.IsCorrect.ShouldBeFalse();
        result.CorrectCount.ShouldBe(0);
        result.TotalCount.ShouldBe(3);
        Rows(result.Feedback).ShouldAllBe(r => !r.Correct);
    }

    [Fact]
    public void Grade_MultiPerRow_RowCorrectOnlyWhenSelectedSetEqualsSolutionSet()
    {
        // r1: eksik (yalnız k) → yanlış; r2: fazla (g + k) → yanlış.
        var partial = Grader.Grade(Input(Payload("""{"r1":["k"],"r2":["g","k"]}"""), MultiSolution, MultiPlan));
        Rows(partial.Feedback).ShouldBe(new[] { ("r1", false), ("r2", false) });

        // Sıra önemsiz: s, k == k, s.
        var full = Grader.Grade(Input(Payload("""{"r1":["s","k"],"r2":["g"]}"""), MultiSolution, MultiPlan));
        full.IsCorrect.ShouldBeTrue();
        full.CorrectCount.ShouldBe(2);
        full.TotalCount.ShouldBe(2);
    }

    [Fact]
    public void Grade_FeedbackNeverContainsColumnIdsOrLabels()
    {
        var result = Grader.Grade(Input(Payload("""{"r1":["cY"]}""")));

        result.Feedback.ShouldNotBeNull();
        result.Feedback!.ShouldNotContain("cD");
        result.Feedback.ShouldNotContain("cY");
        result.Feedback.ShouldNotContain("\"D\"");
        // Yalnız id + correct anahtarları.
        using var doc = JsonDocument.Parse(result.Feedback);
        doc.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(new[] { "rows" });
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
        var result = Grader.Grade(Input(Payload("""{"r1":["cD"]}"""), solution));

        result.ShouldBe(InteractionGradeResult.Ungradable);
    }

    [Fact]
    public void Grade_UnreadablePlan_IsUngradableAndDoesNotThrow()
    {
        Grader.Grade(Input(Payload("""{"r1":["cD"]}"""), Solution, plan: "{broken")).ShouldBe(InteractionGradeResult.Ungradable);
        Grader.Grade(Input(Payload("""{"r1":["cD"]}"""), Solution, plan: null)).ShouldBe(InteractionGradeResult.Ungradable);
    }

    [Fact]
    public void Grade_MalformedPayload_TreatedAsBlank()
    {
        var result = Grader.Grade(Input("{broken"));

        result.CorrectCount.ShouldBe(0);
        result.TotalCount.ShouldBe(3);
    }

    // ---------------------------------------------------------------- Validate / Normalize

    [Theory]
    [InlineData(null)]
    [InlineData("""{"version":1,"type":"matrixChoice","selections":{}}""")]
    [InlineData("""{"type":"matrixChoice","selections":{"r1":["cD"]}}""")]
    [InlineData("""{"version":1,"type":"matrixChoice","selections":{"r1":["cD"],"r2":["cY"],"r3":[]}}""")]
    public void Validate_ValidPayloads_AreAccepted(string? payload)
    {
        Grader.Validate(Plan, payload).ShouldBeTrue();
    }

    [Theory]
    [InlineData("{broken")]
    [InlineData("""[1]""")]
    [InlineData("""{"version":1,"type":"dragDropLabeling","selections":{}}""")]          // yanlış tip
    [InlineData("""{"version":2,"type":"matrixChoice","selections":{}}""")]              // yanlış sürüm
    [InlineData("""{"version":1,"type":"matrixChoice"}""")]                              // selections yok
    [InlineData("""{"version":1,"type":"matrixChoice","selections":[]}""")]              // nesne değil
    [InlineData("""{"version":1,"type":"matrixChoice","selections":{"r9":["cD"]}}""")]   // plan dışı satır
    [InlineData("""{"version":1,"type":"matrixChoice","selections":{"r1":["cX"]}}""")]   // plan dışı kolon
    [InlineData("""{"version":1,"type":"matrixChoice","selections":{"r1":["cD","cY"]}}""")] // tek seçimli satıra iki kolon
    [InlineData("""{"version":1,"type":"matrixChoice","selections":{"r1":["cD"],"r1":["cY"]}}""")] // yinelenen anahtar
    [InlineData("""{"version":1,"type":"matrixChoice","selections":{"r1":[1]}}""")]      // string olmayan kolon
    [InlineData("""{"version":1,"type":"matrixChoice","selections":{"r1":[""]}}""")]     // boş kolon id
    [InlineData("""{"version":1,"type":"matrixChoice","selections":{"r1":"cD"}}""")]     // dizi değil
    public void Validate_InvalidPayloads_AreRejected(string payload)
    {
        Grader.Validate(Plan, payload).ShouldBeFalse();
    }

    [Fact]
    public void Validate_DuplicateColumnInMultiPerRow_IsRejected()
    {
        Grader.Validate(MultiPlan, Payload("""{"r1":["k","k"]}""")).ShouldBeFalse();
        Grader.Validate(MultiPlan, Payload("""{"r1":["k","s"]}""")).ShouldBeTrue();
    }

    [Fact]
    public void Validate_PayloadAboveMaxLength_IsRejected()
    {
        var oversized = Payload("{\"r1\":[\"" + new string('c', InteractionPayload.MaxLength) + "\"]}");

        Grader.Validate(Plan, oversized).ShouldBeFalse();
    }

    [Fact]
    public void Validate_NonEmptySelectionsWithUnreadablePlan_IsRejected()
    {
        Grader.Validate("{broken", Payload("""{"r1":["cD"]}""")).ShouldBeFalse();
        Grader.Validate(null, Payload("{}")).ShouldBeTrue();
    }

    [Theory]
    [InlineData("""{"version":1,"type":"matrixChoice","selections":{}}""")]
    [InlineData("""{"version":1,"type":"matrixChoice","selections":{"r1":[],"r2":[]}}""")]
    public void Normalize_EmptySelections_IsNull(string payload)
    {
        Grader.Normalize(payload).ShouldBeNull();
    }

    [Fact]
    public void Normalize_NonEmptySelections_IsUnchanged()
    {
        var payload = Payload("""{"r1":["cD"]}""");

        Grader.Normalize(payload).ShouldBe(payload);
    }

    // ---------------------------------------------------------------- ValidateDefinition (kayıt)

    [Fact]
    public void ValidateDefinition_ValidPlanAndSolution_IsAccepted()
    {
        Grader.ValidateDefinition(Plan, Solution).ShouldBeTrue();
        Grader.ValidateDefinition(MultiPlan, MultiSolution).ShouldBeTrue();
    }

    [Fact]
    public void ValidateDefinition_PlanWithoutSolution_IsAcceptedAsDraft()
    {
        Grader.ValidateDefinition(Plan, null).ShouldBeTrue();
    }

    [Fact]
    public void ValidateDefinition_MinimalPlanWithoutLabelsOrCells_IsAccepted()
    {
        const string plan = """{"version":1,"type":"matrixChoice","columns":[{"id":"a"},{"id":"b"}],"rows":[{"id":"r","box":{"x":0,"y":0,"width":1,"height":1}}]}""";

        Grader.ValidateDefinition(plan, """{"r":["b"]}""").ShouldBeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("{broken")]
    [InlineData("""{"version":1,"type":"dragDropLabeling","columns":[{"id":"a"},{"id":"b"}],"rows":[{"id":"r","box":{"x":0,"y":0,"width":1,"height":1}}]}""")]
    [InlineData("""{"version":2,"type":"matrixChoice","columns":[{"id":"a"},{"id":"b"}],"rows":[{"id":"r","box":{"x":0,"y":0,"width":1,"height":1}}]}""")]
    [InlineData("""{"version":1,"type":"matrixChoice","columns":[{"id":"a"}],"rows":[{"id":"r","box":{"x":0,"y":0,"width":1,"height":1}}]}""")]          // < 2 kolon
    [InlineData("""{"version":1,"type":"matrixChoice","columns":[{"id":"a"},{"id":"b"}],"rows":[]}""")]                                                    // 0 satır
    [InlineData("""{"version":1,"type":"matrixChoice","columns":[{"id":"a"},{"id":"a"}],"rows":[{"id":"r","box":{"x":0,"y":0,"width":1,"height":1}}]}""")] // yinelenen kolon
    [InlineData("""{"version":1,"type":"matrixChoice","columns":[{"id":"a"},{"id":"b"}],"rows":[{"id":"r","box":{"x":0,"y":0,"width":1,"height":1}},{"id":"r","box":{"x":0,"y":0,"width":1,"height":1}}]}""")] // yinelenen satır
    [InlineData("""{"version":1,"type":"matrixChoice","columns":[{"id":""},{"id":"b"}],"rows":[{"id":"r","box":{"x":0,"y":0,"width":1,"height":1}}]}""")]  // boş id
    [InlineData("""{"version":1,"type":"matrixChoice","columns":[{"id":1},{"id":"b"}],"rows":[{"id":"r","box":{"x":0,"y":0,"width":1,"height":1}}]}""")]   // sayı id
    [InlineData("""{"version":1,"type":"matrixChoice","columns":[{"id":"a"},{"id":"b"}],"rows":[{"id":"r"}]}""")]                                          // kutu yok
    [InlineData("""{"version":1,"type":"matrixChoice","columns":[{"id":"a"},{"id":"b"}],"rows":[{"id":"r","box":{"x":0,"y":0,"width":0,"height":1}}]}""")] // genişlik 0
    [InlineData("""{"version":1,"type":"matrixChoice","columns":[{"id":"a"},{"id":"b"}],"rows":[{"id":"r","box":{"x":"0","y":0,"width":1,"height":1}}]}""")] // sayı değil
    [InlineData("""{"version":1,"type":"matrixChoice","columns":[{"id":"a"},{"id":"b"}],"rows":[{"id":"r","box":{"x":0,"y":0,"width":1,"height":1},"cells":{"z":{"x":0,"y":0,"width":1,"height":1}}}]}""")] // plan dışı hücre kolonu
    [InlineData("""{"version":1,"type":"matrixChoice","columns":[{"id":"a"},{"id":"b"}],"rows":[{"id":"r","box":{"x":0,"y":0,"width":1,"height":1},"cells":[]}]}""")] // cells nesne değil
    [InlineData("""{"version":1,"type":"matrixChoice","multiPerRow":"yes","columns":[{"id":"a"},{"id":"b"}],"rows":[{"id":"r","box":{"x":0,"y":0,"width":1,"height":1}}]}""")] // bool değil
    [InlineData("""{"version":1,"type":"matrixChoice","columns":[{"id":"a","label":5},{"id":"b"}],"rows":[{"id":"r","box":{"x":0,"y":0,"width":1,"height":1}}]}""")] // etiket string değil
    public void ValidateDefinition_InvalidPlan_IsRejected(string? plan)
    {
        Grader.ValidateDefinition(plan, null).ShouldBeFalse();
    }

    [Fact]
    public void ValidateDefinition_LimitsOnIdsLabelsColumnsAndRows()
    {
        static string Box() => "\"box\":{\"x\":0,\"y\":0,\"width\":1,\"height\":1}";
        static string PlanOf(int columns, int rows, string? columnId = null, string? label = null)
        {
            var cols = Enumerable.Range(0, columns).Select(i =>
                i == 0 && columnId != null ? $"{{\"id\":\"{columnId}\"}}" : $"{{\"id\":\"c{i}\"}}");
            var rs = Enumerable.Range(0, rows).Select(i =>
                i == 0 && label != null ? $"{{\"id\":\"r{i}\",\"label\":\"{label}\",{Box()}}}" : $"{{\"id\":\"r{i}\",{Box()}}}");
            return $"{{\"version\":1,\"type\":\"matrixChoice\",\"columns\":[{string.Join(",", cols)}],\"rows\":[{string.Join(",", rs)}]}}";
        }

        Grader.ValidateDefinition(PlanOf(10, 60), null).ShouldBeTrue();
        Grader.ValidateDefinition(PlanOf(11, 1), null).ShouldBeFalse();
        Grader.ValidateDefinition(PlanOf(2, 61), null).ShouldBeFalse();
        Grader.ValidateDefinition(PlanOf(2, 1, columnId: new string('a', 32)), null).ShouldBeTrue();
        Grader.ValidateDefinition(PlanOf(2, 1, columnId: new string('a', 33)), null).ShouldBeFalse();
        Grader.ValidateDefinition(PlanOf(2, 1, label: new string('x', 200)), null).ShouldBeTrue();
        Grader.ValidateDefinition(PlanOf(2, 1, label: new string('x', 201)), null).ShouldBeFalse();
    }

    [Theory]
    [InlineData("""{"r1":["cD"],"r2":["cY"]}""")]                           // r3 çözümsüz
    [InlineData("""{"r1":["cD"],"r2":["cY"],"r3":[]}""")]                    // boş satır
    [InlineData("""{"r1":["cD","cY"],"r2":["cY"],"r3":["cD"]}""")]           // tek seçimli satıra iki kolon
    [InlineData("""{"r1":["cX"],"r2":["cY"],"r3":["cD"]}""")]                // plan dışı kolon
    [InlineData("""{"r1":["cD"],"r2":["cY"],"r3":["cD"],"r9":["cD"]}""")]    // plan dışı satır
    [InlineData("""{"r1":"cD","r2":["cY"],"r3":["cD"]}""")]                  // dizi değil
    [InlineData("""{"r1":[1],"r2":["cY"],"r3":["cD"]}""")]                   // string değil
    [InlineData("""["cD"]""")]                                               // nesne değil
    public void ValidateDefinition_SolutionInconsistentWithPlan_IsRejected(string solution)
    {
        Grader.ValidateDefinition(Plan, solution).ShouldBeFalse();
    }

    [Fact]
    public void ValidateDefinition_MultiPerRow_RejectsDuplicateColumnsButAllowsSeveral()
    {
        Grader.ValidateDefinition(MultiPlan, """{"r1":["k","s","g"],"r2":["g"]}""").ShouldBeTrue();
        Grader.ValidateDefinition(MultiPlan, """{"r1":["k","k"],"r2":["g"]}""").ShouldBeFalse();
    }

    [Fact]
    public void OtherGraders_ValidateDefinition_DefaultsToTrue()
    {
        IInteractionGrader drag = new DragDropLabelingGrader();
        IInteractionGrader mcq = new McqGrader();

        drag.ValidateDefinition("""{"anything":1}""", null).ShouldBeTrue();
        mcq.ValidateDefinition(null, null).ShouldBeTrue();
    }

    // ---------------------------------------------------------------- review #574: allowlist (M1) + id deseni (L2)

    private const string MinBox = "{\"x\":0,\"y\":0,\"width\":1,\"height\":1}";

    [Theory]
    [InlineData("""{"version":1,"type":"matrixChoice","extra":1,"columns":[{"id":"a"},{"id":"b"}],"rows":[{"id":"r","box":{"x":0,"y":0,"width":1,"height":1}}]}""")]          // kök: bilinmeyen alan
    [InlineData("""{"version":1,"type":"matrixChoice","columns":[{"id":"a","color":"red"},{"id":"b"}],"rows":[{"id":"r","box":{"x":0,"y":0,"width":1,"height":1}}]}""")]  // kolon: bilinmeyen alan
    [InlineData("""{"version":1,"type":"matrixChoice","columns":[{"id":"a"},{"id":"b"}],"rows":[{"id":"r","note":"x","box":{"x":0,"y":0,"width":1,"height":1}}]}""")]       // satır: bilinmeyen alan
    [InlineData("""{"version":1,"type":"matrixChoice","columns":[{"id":"a"},{"id":"b"}],"rows":[{"id":"r","box":{"x":0,"y":0,"width":1,"height":1,"z":2}}]}""")]             // kutu: bilinmeyen alan
    [InlineData("""{"version":1,"type":"matrixChoice","columns":[{"id":"a"},{"id":"b"}],"rows":[{"id":"r","box":{"x":0,"y":0,"width":1,"height":1},"cells":{"a":{"x":0,"y":0,"width":1,"height":1,"k":1}}}]}""")] // hücre kutusu: bilinmeyen alan
    [InlineData("""{"version":1,"type":"matrixChoice","columns":[{"id":"a","solution":true},{"id":"b"}],"rows":[{"id":"r","box":{"x":0,"y":0,"width":1,"height":1}}]}""")] // iç içe solution (kolon)
    [InlineData("""{"version":1,"type":"matrixChoice","columns":[{"id":"a"},{"id":"b"}],"rows":[{"id":"r","solution":["a"],"box":{"x":0,"y":0,"width":1,"height":1}}]}""")] // iç içe solution (satır)
    [InlineData("""{"version":1,"type":"matrixChoice","columns":[{"id":"a"},{"id":"b"}],"rows":[{"id":"r","box":{"x":0,"y":0,"width":1,"height":1},"cells":{"solution":{"x":0,"y":0,"width":1,"height":1}}}]}""")] // cells: kolon dışı anahtar
    public void ValidateDefinition_UnknownKeyAtAnyLevel_IsRejected(string plan)
    {
        Grader.ValidateDefinition(plan, null).ShouldBeFalse();
    }

    [Fact]
    public void ValidateDefinition_RootSolutionKey_IsRejected()
    {
        // Kayıt yolunda kök "solution" TryResolveForWrite'ta ayrılır; yine de doğrudan gelirse allowlist reddeder.
        const string plan = """{"version":1,"type":"matrixChoice","columns":[{"id":"a"},{"id":"b"}],"rows":[{"id":"r","box":{"x":0,"y":0,"width":1,"height":1}}],"solution":{"r":["a"]}}""";

        Grader.ValidateDefinition(plan, null).ShouldBeFalse();
    }

    [Theory]
    [InlineData("a b")]
    [InlineData("a.b")]
    [InlineData("ç")]
    [InlineData("<x>")]
    [InlineData(" a")]
    public void ValidateDefinition_IdOutsidePattern_IsRejected(string id)
    {
        var plan = "{\"version\":1,\"type\":\"matrixChoice\",\"columns\":[{\"id\":\"" + id + "\"},{\"id\":\"b\"}],\"rows\":[{\"id\":\"r\",\"box\":" + MinBox + "}]}";
        Grader.ValidateDefinition(plan, null).ShouldBeFalse();

        var rowPlan = "{\"version\":1,\"type\":\"matrixChoice\",\"columns\":[{\"id\":\"a\"},{\"id\":\"b\"}],\"rows\":[{\"id\":\"" + id + "\",\"box\":" + MinBox + "}]}";
        Grader.ValidateDefinition(rowPlan, null).ShouldBeFalse();
    }

    [Fact]
    public void ValidateDefinition_IdWithAllowedCharacters_IsAccepted()
    {
        var plan = "{\"version\":1,\"type\":\"matrixChoice\",\"columns\":[{\"id\":\"Col_1-A\"},{\"id\":\"b\"}],\"rows\":[{\"id\":\"row-9_Z\",\"box\":" + MinBox + "}]}";

        Grader.ValidateDefinition(plan, """{"row-9_Z":["Col_1-A"]}""").ShouldBeTrue();
    }

    [Fact]
    public void Validate_PayloadIdsOutsidePattern_AreRejected()
    {
        Grader.Validate(Plan, Payload("""{"r 1":["cD"]}""")).ShouldBeFalse();
        Grader.Validate(Plan, Payload("""{"r1":["c.D"]}""")).ShouldBeFalse();
        // Seçimsiz ama desene uymayan satır anahtarı da reddedilir.
        Grader.Validate(Plan, Payload("""{"<r>":[]}""")).ShouldBeFalse();
    }

    [Fact]
    public void Registry_CanonicalType_NormalizesKnownInteractiveTypesOnly()
    {
        var registry = InteractionGraderRegistry.Default;

        registry.CanonicalType("MatrixChoice").ShouldBe("matrixChoice");
        registry.CanonicalType(" MATRIXCHOICE ").ShouldBe("matrixChoice");
        registry.CanonicalType("DragDropLabeling").ShouldBe("dragDropLabeling");
        registry.CanonicalType("MCQ").ShouldBe("MCQ");
        registry.CanonicalType(null).ShouldBeNull();
        registry.CanonicalType("").ShouldBe("");
        registry.CanonicalType("unknownFutureType").ShouldBe("unknownFutureType");
    }

    // ---------------------------------------------------------------- registry

    [Theory]
    [InlineData("matrixChoice")]
    [InlineData("MATRIXCHOICE")]
    [InlineData("  matrixChoice ")]
    public void Registry_Default_ResolvesMatrixChoice(string type)
    {
        InteractionGraderRegistry.Default.Resolve(type).ShouldBeOfType<MatrixChoiceGrader>();
    }
}
