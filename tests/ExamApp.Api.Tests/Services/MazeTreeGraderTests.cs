using System.Text.Json;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #585: <see cref="MazeTreeGrader"/> — dört varyantın kayıt kuralları (ağaç, pano zinciri, oda komşuluğu, taşlar), payload
/// doğrulama (kesintisiz yol, bitişten sonra adım yok), ilk sapmada duran yol kontrolü, iki yönlü kapı, taş işaretleme, çözüm
/// sızıntısı yok, analiz ve çözümsüz soru.
/// </summary>
public class MazeTreeGraderTests
{
    // İki düzeyli D/Y ağacı: n1 →(D) n2, (Y) n3; n2 → x1 / x2; n3 → x3 / x4. Doğru: n1 D, n2 Y → x2.
    private const string TreePlan =
        """{"version":1,"type":"mazeTree","variant":"tree","start":"n1","nodes":[{"id":"n1","label":"Kök"},{"id":"n2","label":"Sol"},{"id":"n3","label":"Sağ"},{"id":"x1","label":"1. çıkış","end":true},{"id":"x2","label":"2. çıkış","end":true},{"id":"x3","label":"3. çıkış","end":true},{"id":"x4","label":"4. çıkış","end":true}],"edges":[{"id":"e1","from":"n1","to":"n2","label":"D"},{"id":"e2","from":"n1","to":"n3","label":"Y"},{"id":"e3","from":"n2","to":"x1","label":"D"},{"id":"e4","from":"n2","to":"x2","label":"Y"},{"id":"e5","from":"n3","to":"x3","label":"D"},{"id":"e6","from":"n3","to":"x4","label":"Y"}]}""";

    private const string TreeSolution = """{"choices":{"n1":"e1","n2":"e4","n3":"e5"}}""";

    // Pano: r1 (3 kutu) → r2 (2 kutu) → f. Doğru: r1 b2, r2 b4.
    private const string BoardPlan =
        """{"version":1,"type":"mazeTree","variant":"board","start":"r1","nodes":[{"id":"r1","label":"Gaz"},{"id":"r2","label":"Katı"},{"id":"f","label":"Bitiş","end":true}],"edges":[{"id":"b1","from":"r1","to":"r2","label":"Sıkıştırılamaz."},{"id":"b2","from":"r1","to":"r2","label":"Akışkandır."},{"id":"b3","from":"r1","to":"r2","label":"Belirli şekli vardır."},{"id":"b4","from":"r2","to":"f","label":"Belirli şekli vardır."},{"id":"b5","from":"r2","to":"f","label":"Akışkandır."}]}""";

    private const string BoardSolution = """{"choices":{"r1":"b2","r2":"b4"}}""";

    // Odalar (2×2): a(0,0) başla, b(0,1), c(1,0), d(1,1) bitiş. Kapılar: a-b "30", a-c "24", b-d "12", c-d "26", a-d "20" (çapraz).
    // Doğru: a → b (d1), b → d (d3).
    private const string RoomsPlan =
        """{"version":1,"type":"mazeTree","variant":"rooms","start":"a","nodes":[{"id":"a","label":"6×4 alan?","row":0,"col":0},{"id":"b","label":"3×4 alan?","row":0,"col":1},{"id":"c","label":"4×6 alan?","row":1,"col":0},{"id":"d","label":"Bitiş","row":1,"col":1,"end":true}],"edges":[{"id":"d1","from":"a","to":"b","label":"24"},{"id":"d2","from":"a","to":"c","label":"30"},{"id":"d3","from":"b","to":"d","label":"12"},{"id":"d4","from":"c","to":"d","label":"26"},{"id":"d5","from":"a","to":"d","label":"20"}]}""";

    private const string RoomsSolution = """{"choices":{"a":"d1","b":"d3","c":"d2"}}""";

    private const string StonesPlan =
        """{"version":1,"type":"mazeTree","variant":"stones","nodes":[{"id":"s1","label":"5'er 5'er dağıttı."},{"id":"s2","label":"8.'inci sınıflar"},{"id":"s3","label":"18.45'te"},{"id":"s4","label":"onbeşbin"}]}""";

    private const string StonesSolution = """{"marked":["s2","s4"]}""";

    private static readonly MazeTreeGrader Grader = new();

    private static string Path(params string[] edges) => JsonSerializer.Serialize(new { version = 1, type = "mazeTree", path = edges });

    private static string Marked(params string[] nodes) => JsonSerializer.Serialize(new { version = 1, type = "mazeTree", marked = nodes });

    private static InteractionGradeInput Input(string plan, string? solution, string? payload)
        => new(plan, solution, payload, SelectedAnswerId: null, CorrectAnswerId: null);

    private static (List<(string Id, bool Correct)> Rows, string Status, string? At) Feedback(string? feedback)
    {
        feedback.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(feedback!);
        var root = doc.RootElement;
        var rows = root.GetProperty("rows").EnumerateArray()
            .Select(r => (r.GetProperty("id").GetString()!, r.GetProperty("correct").GetBoolean())).ToList();
        return (rows, root.GetProperty("status").GetString()!, root.TryGetProperty("at", out var at) ? at.GetString() : null);
    }

    // ---- Kayıt ----

    [Theory]
    [InlineData(TreePlan, TreeSolution)]
    [InlineData(BoardPlan, BoardSolution)]
    [InlineData(RoomsPlan, RoomsSolution)]
    [InlineData(StonesPlan, StonesSolution)]
    public void ValidateDefinition_ValidPilots_Accepted(string plan, string solution)
    {
        Grader.ValidateDefinition(plan, solution).ShouldBeTrue();
        Grader.ValidateDefinition(plan, null).ShouldBeTrue(); // taslak
    }

    [Fact]
    public void ValidateDefinition_TreeWithSharedChild_Rejected()
    {
        // x2'ye iki kenar girer (ağaç değil).
        var plan = TreePlan.Replace("""{"id":"e5","from":"n3","to":"x3","label":"D"}""", """{"id":"e5","from":"n3","to":"x2","label":"D"}""");
        Grader.ValidateDefinition(plan, null).ShouldBeFalse();
    }

    [Fact]
    public void ValidateDefinition_TreeDecisionWithSingleBranch_Rejected()
    {
        var plan = TreePlan.Replace(""",{"id":"e6","from":"n3","to":"x4","label":"Y"}""", "")
            .Replace(""",{"id":"x4","label":"4. çıkış","end":true}""", "");
        Grader.ValidateDefinition(plan, null).ShouldBeFalse();
    }

    [Fact]
    public void ValidateDefinition_DuplicateBranchLabel_Rejected()
    {
        var plan = TreePlan.Replace("""{"id":"e2","from":"n1","to":"n3","label":"Y"}""", """{"id":"e2","from":"n1","to":"n3","label":"D"}""");
        Grader.ValidateDefinition(plan, null).ShouldBeFalse();
    }

    [Fact]
    public void ValidateDefinition_BoardRowBoxesToDifferentNodes_Rejected()
    {
        var plan = BoardPlan.Replace("""{"id":"b3","from":"r1","to":"r2",""", """{"id":"b3","from":"r1","to":"f",""");
        Grader.ValidateDefinition(plan, null).ShouldBeFalse();
    }

    [Fact]
    public void ValidateDefinition_BoardRowWithoutLabel_Accepted_TreeStatementWithoutLabel_Rejected()
    {
        Grader.ValidateDefinition(BoardPlan.Replace("""{"id":"r1","label":"Gaz"}""", """{"id":"r1"}"""), BoardSolution).ShouldBeTrue();
        Grader.ValidateDefinition(TreePlan.Replace("""{"id":"n2","label":"Sol"}""", """{"id":"n2"}"""), null).ShouldBeFalse();
    }

    [Fact]
    public void ValidateDefinition_RoomsDoorBetweenNonNeighbours_Rejected()
    {
        var plan = RoomsPlan.Replace("""{"id":"d","label":"Bitiş","row":1,"col":1,"end":true}""", """{"id":"d","label":"Bitiş","row":1,"col":3,"end":true}""");
        Grader.ValidateDefinition(plan, null).ShouldBeFalse();
    }

    [Fact]
    public void ValidateDefinition_RoomsTwoDiagonalsInOneBlock_Rejected()
    {
        // a(0,0)–d(1,1) çaprazı var; b(0,1)–c(1,0) çaprazı eklenince aynı köşede iki kapı.
        var plan = RoomsPlan.Replace("""{"id":"d5","from":"a","to":"d","label":"20"}""",
            """{"id":"d5","from":"a","to":"d","label":"20"},{"id":"d6","from":"b","to":"c","label":"7"}""");
        Grader.ValidateDefinition(plan, null).ShouldBeFalse();
    }

    [Fact]
    public void ValidateDefinition_BoardEdgeLeavingFinish_Rejected()
    {
        var plan = BoardPlan.Replace("""{"id":"b5","from":"r2","to":"f","label":"Akışkandır."}""",
            """{"id":"b5","from":"r2","to":"f","label":"Akışkandır."},{"id":"b6","from":"f","to":"r1","label":"x"}""");
        Grader.ValidateDefinition(plan, null).ShouldBeFalse();
    }

    [Fact]
    public void ValidateDefinition_RoomsSamePosition_Rejected()
    {
        var plan = RoomsPlan.Replace("""{"id":"c","label":"4×6 alan?","row":1,"col":0}""", """{"id":"c","label":"4×6 alan?","row":0,"col":1}""");
        Grader.ValidateDefinition(plan, null).ShouldBeFalse();
    }

    [Fact]
    public void ValidateDefinition_UnreachableNode_Rejected()
    {
        var plan = BoardPlan.Replace("""{"id":"f","label":"Bitiş","end":true}""", """{"id":"f","label":"Bitiş","end":true},{"id":"z","label":"Yetim"}""");
        Grader.ValidateDefinition(plan, null).ShouldBeFalse();
    }

    [Theory]
    [InlineData("""{"version":1,"type":"mazeTree","variant":"stones","start":"s1","nodes":[{"id":"s1","label":"a"},{"id":"s2","label":"b"}]}""")]
    [InlineData("""{"version":1,"type":"mazeTree","variant":"stones","nodes":[{"id":"s1","label":"a"}]}""")]
    [InlineData("""{"version":1,"type":"mazeTree","variant":"stones","nodes":[{"id":"s1","label":"a"},{"id":"s2","label":" "}]}""")]
    [InlineData("""{"version":1,"type":"mazeTree","variant":"maze","nodes":[{"id":"s1","label":"a"},{"id":"s2","label":"b"}]}""")]
    [InlineData("""{"version":1,"type":"mazeTree","variant":"stones","nodes":[{"id":"s1","label":"a","x":1},{"id":"s2","label":"b"}]}""")]
    public void ValidateDefinition_InvalidStones_Rejected(string plan)
        => Grader.ValidateDefinition(plan, null).ShouldBeFalse();

    [Theory]
    [InlineData("""{"choices":{"n1":"e1"}}""")] // rota bitişe varmaz
    [InlineData("""{"choices":{"n1":"e3","n2":"e4"}}""")] // e3 n1'den kullanılamaz
    [InlineData("""{"choices":{"x1":"e3","n1":"e1","n2":"e4"}}""")] // bitiş düğümünde seçim
    [InlineData("""{"marked":["n1"]}""")]
    [InlineData("""{"choices":{"n1":"e1","n2":"e4"},"extra":1}""")]
    public void ValidateDefinition_InvalidTreeSolution_Rejected(string solution)
        => Grader.ValidateDefinition(TreePlan, solution).ShouldBeFalse();

    [Fact]
    public void ValidateDefinition_RoomsSolutionLoop_Rejected()
    {
        // a → b, b → a (kapı iki yönlü): döngü, bitişe varmaz.
        Grader.ValidateDefinition(RoomsPlan, """{"choices":{"a":"d1","b":"d1"}}""").ShouldBeFalse();
    }

    [Theory]
    [InlineData("""{"marked":["s1","s1"]}""")]
    [InlineData("""{"marked":[]}""")] // hiç işaretsiz çözülemez
    [InlineData("""{"marked":["zz"]}""")]
    [InlineData("""{"choices":{}}""")]
    public void ValidateDefinition_InvalidStonesSolution_Rejected(string solution)
        => Grader.ValidateDefinition(StonesPlan, solution).ShouldBeFalse();

    // ---- Payload ----

    [Fact]
    public void Validate_PathMustBeContinuousAndStopAtEnd()
    {
        Grader.Validate(TreePlan, Path("e1", "e4")).ShouldBeTrue();
        Grader.Validate(TreePlan, Path("e1")).ShouldBeTrue(); // yarım yol
        Grader.Validate(TreePlan, Path("e1", "e5")).ShouldBeFalse(); // e5 n2'den değil
        Grader.Validate(TreePlan, Path("e1", "e4", "e3")).ShouldBeFalse(); // bitişten sonra adım
        Grader.Validate(TreePlan, Path("zz")).ShouldBeFalse();
        Grader.Validate(TreePlan, Marked("n1")).ShouldBeFalse(); // yol varyantında işaret yok
        Grader.Validate(TreePlan, null).ShouldBeTrue();
    }

    [Fact]
    public void Validate_RoomsDoorsAreTwoWay()
    {
        // a → b → a → c → d: geri dönüş serbest.
        Grader.Validate(RoomsPlan, Path("d1", "d1", "d2", "d4")).ShouldBeTrue();
    }

    [Theory]
    [InlineData("""{"version":1,"type":"mazeTree","path":["e1"],"marked":["n1"]}""")]
    [InlineData("""{"version":1,"type":"mazeTree"}""")]
    [InlineData("""{"version":2,"type":"mazeTree","path":["e1"]}""")]
    [InlineData("""{"version":1,"type":"match","path":["e1"]}""")]
    [InlineData("""{"version":1,"type":"mazeTree","path":["e1"],"path":["e2"]}""")]
    [InlineData("""{"version":1,"type":"mazeTree","path":[1]}""")]
    public void Validate_MalformedPayload_Rejected(string payload)
        => Grader.Validate(TreePlan, payload).ShouldBeFalse();

    [Fact]
    public void Validate_Stones()
    {
        Grader.Validate(StonesPlan, Marked("s1", "s3")).ShouldBeTrue();
        Grader.Validate(StonesPlan, Marked("s1", "s1")).ShouldBeFalse();
        Grader.Validate(StonesPlan, Marked("zz")).ShouldBeFalse();
        Grader.Validate(StonesPlan, Path("s1")).ShouldBeFalse();
    }

    [Fact]
    public void Validate_PathTooLong_Rejected()
    {
        var steps = Enumerable.Repeat("d1", MazeTreeGrader.MaxPathLength + 1).ToArray();
        Grader.Validate(RoomsPlan, Path(steps)).ShouldBeFalse();
    }

    [Fact]
    public void Normalize_EmptyAnswer_IsNull()
    {
        Grader.Normalize(Path()).ShouldBeNull();
        Grader.Normalize(Marked()).ShouldBeNull();
        Grader.Normalize(Path("e1")).ShouldNotBeNull();
    }

    // ---- Kontrol ----

    [Fact]
    public void Grade_TreeCorrectRoute_Reached()
    {
        var result = Grader.Grade(Input(TreePlan, TreeSolution, Path("e1", "e4")));

        result.IsCorrect.ShouldBeTrue();
        result.CorrectCount.ShouldBe(2);
        result.TotalCount.ShouldBe(2);
        var fb = Feedback(result.Feedback);
        fb.Rows.ShouldBe(new[] { ("n1", true), ("n2", true) });
        fb.Status.ShouldBe(MazeTreeGrader.StatusReached);
        fb.At.ShouldBe("x2");
    }

    [Fact]
    public void Grade_TreeWrongTurn_StopsAtDeviationWithoutLeakingRoute()
    {
        // n1'de Y (yanlış) → n3 → x3: sapma n1'de, sonrası değerlendirilmez.
        var result = Grader.Grade(Input(TreePlan, TreeSolution, Path("e2", "e5")));

        result.IsCorrect.ShouldBeFalse();
        result.CorrectCount.ShouldBe(0);
        result.TotalCount.ShouldBe(2);
        var fb = Feedback(result.Feedback);
        fb.Rows.ShouldBe(new[] { ("n1", false) });
        fb.Status.ShouldBe(MazeTreeGrader.StatusDeviated);
        fb.At.ShouldBe("n1");
        result.Feedback!.ShouldNotContain("e1");
        result.Feedback!.ShouldNotContain("x2");
    }

    [Fact]
    public void Grade_TreeSecondLevelDeviation()
    {
        var fb = Feedback(Grader.Grade(Input(TreePlan, TreeSolution, Path("e1", "e3"))).Feedback);
        fb.Rows.ShouldBe(new[] { ("n1", true), ("n2", false) });
        fb.At.ShouldBe("n2");
    }

    [Fact]
    public void Grade_PartialPath_Incomplete()
    {
        var result = Grader.Grade(Input(BoardPlan, BoardSolution, Path("b2")));

        result.IsCorrect.ShouldBeFalse();
        result.CorrectCount.ShouldBe(1);
        result.TotalCount.ShouldBe(2);
        Feedback(result.Feedback).Status.ShouldBe(MazeTreeGrader.StatusIncomplete);
    }

    [Fact]
    public void Grade_Blank_IncompleteWithNoRows()
    {
        var result = Grader.Grade(Input(BoardPlan, BoardSolution, null));

        result.IsCorrect.ShouldBeFalse();
        result.CorrectCount.ShouldBe(0);
        result.TotalCount.ShouldBe(2);
        var fb = Feedback(result.Feedback);
        fb.Rows.ShouldBeEmpty();
        fb.Status.ShouldBe(MazeTreeGrader.StatusIncomplete);
    }

    [Fact]
    public void Grade_BoardWrongBox_DeviatesAtRow()
    {
        var fb = Feedback(Grader.Grade(Input(BoardPlan, BoardSolution, Path("b2", "b5"))).Feedback);
        fb.Rows.ShouldBe(new[] { ("r1", true), ("r2", false) });
        fb.At.ShouldBe("r2");
    }

    [Fact]
    public void Grade_RoomsShortcutDoor_IsDeviation()
    {
        // Çapraz kapı d5 doğrudan bitişe götürür ama a'nın cevabı d1: sapma.
        var result = Grader.Grade(Input(RoomsPlan, RoomsSolution, Path("d5")));

        result.IsCorrect.ShouldBeFalse();
        Feedback(result.Feedback).At.ShouldBe("a");
    }

    [Fact]
    public void Grade_RoomsCorrect()
        => Grader.Grade(Input(RoomsPlan, RoomsSolution, Path("d1", "d3"))).IsCorrect.ShouldBeTrue();

    [Fact]
    public void Grade_StonesPerStoneWithFirstWrongStone()
    {
        // s2 doğru işaretli, s3 yanlış işaretli, s4 eksik.
        var result = Grader.Grade(Input(StonesPlan, StonesSolution, Marked("s2", "s3")));

        result.IsCorrect.ShouldBeFalse();
        result.CorrectCount.ShouldBe(2);
        result.TotalCount.ShouldBe(4);
        var fb = Feedback(result.Feedback);
        // İlk yanlış taşta durur: s4'ün durumu söylenmez (taş ikili, satırlar çözümü verirdi).
        fb.Rows.ShouldBe(new[] { ("s1", true), ("s2", true), ("s3", false) });
        fb.Status.ShouldBe(MazeTreeGrader.StatusDeviated);
        fb.At.ShouldBe("s3");
    }

    [Fact]
    public void Grade_StonesAllCorrect_Reached()
    {
        var result = Grader.Grade(Input(StonesPlan, StonesSolution, Marked("s4", "s2")));
        result.IsCorrect.ShouldBeTrue();
        Feedback(result.Feedback).Status.ShouldBe(MazeTreeGrader.StatusReached);
    }

    [Fact]
    public void Grade_StonesBlank_AllWrong()
    {
        var result = Grader.Grade(Input(StonesPlan, StonesSolution, null));
        result.CorrectCount.ShouldBe(0);
        result.TotalCount.ShouldBe(4);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("""{"choices":{}}""")]
    [InlineData("""{"choices":{"n1":"e1"}}""")]
    public void Grade_NoUsableSolution_Ungradable(string? solution)
        => Grader.Grade(Input(TreePlan, solution, Path("e1", "e4"))).ShouldBe(InteractionGradeResult.Ungradable);

    [Fact]
    public void Grade_StonesNoSolution_Ungradable()
        => Grader.Grade(Input(StonesPlan, null, Marked("s1"))).ShouldBe(InteractionGradeResult.Ungradable);

    // ---- Analiz ----

    [Fact]
    public void Analyze_TreeItemsAreRouteNodesWithChosenBranch()
    {
        var items = ((IInteractionGrader)Grader).Analyze(Input(TreePlan, TreeSolution, Path("e1", "e3")));

        items.Select(i => (i.ItemId, i.ItemLabel, i.Correct, i.AnswerKey, i.AnswerLabel)).ShouldBe(new[]
        {
            ("n1", "Kök", true, (string?)"e1", (string?)"D"),
            ("n2", "Sol", false, "e3", "D"),
        });
    }

    [Fact]
    public void Analyze_NodesAfterDeviationAreUnanswered()
    {
        var items = ((IInteractionGrader)Grader).Analyze(Input(TreePlan, TreeSolution, Path("e2", "e5")));

        items.Select(i => (i.ItemId, i.Correct, i.AnswerKey)).ShouldBe(new[] { ("n1", false, (string?)"e2"), ("n2", false, null) });
    }

    [Fact]
    public void Analyze_Stones()
    {
        var items = ((IInteractionGrader)Grader).Analyze(Input(StonesPlan, StonesSolution, Marked("s2")));

        items.Select(i => (i.ItemId, i.Correct, i.AnswerKey)).ShouldBe(new[]
        {
            ("s1", true, (string?)"unmarked"), ("s2", true, "marked"), ("s3", true, "unmarked"), ("s4", false, "unmarked"),
        });
    }

    [Fact]
    public void Registry_ResolvesMazeTree()
        => InteractionGraderRegistry.Default.Resolve("MazeTree").InteractionType.ShouldBe(MazeTreeGrader.Type);
}
