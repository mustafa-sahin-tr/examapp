using System.Text.Json;
using ExamApp.Api.Services.Interactions;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #571: sunucu tarafı etkileşim kontrolcüleri — <see cref="DragDropLabelingGrader"/> (satır bazlı doğrulama, puanlama,
/// geri bildirim sızıntısı), <see cref="McqGrader"/> (bugünkü kuralla birebir) ve <see cref="InteractionGraderRegistry"/>.
/// Saf birim testleri: veritabanı yok.
/// </summary>
public class InteractionGraderTests
{
    // Plan: hedefler z1, z2, z9 (z9 çözümde yok); kartlar l1, l2 ve çözümde olan ama geri bildirimde ASLA görünmemesi gereken SECRET-LBL-7.
    private const string Plan =
        """{"version":1,"type":"dragDropLabeling","dropZones":[{"id":"z1"},{"id":"z2"},{"id":"z9"}],"draggables":[{"id":"l1","label":"Ali"},{"id":"l2","label":"Veli"},{"id":"SECRET-LBL-7","label":"Ayşe"}]}""";

    // Çözüm: z1 ← l1, z2 ← l2 (SECRET-LBL-7 hiçbir çözümde yok; yanlış yerleşimlerde kullanılır).
    private const string Solution = """{"placements":[{"draggableId":"l1","dropZoneId":"z1"},{"draggableId":"l2","dropZoneId":"z2"}]}""";

    private static readonly DragDropLabelingGrader Grader = new();

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

    // ---------------------------------------------------------------- DragDropLabelingGrader.Grade

    [Fact]
    public void DragDropLabelingGrader_Grade_FullyCorrectPlacements_IsCorrectWithEveryRowCorrect()
    {
        var payload = """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"z1","draggableId":"l1"},{"dropZoneId":"z2","draggableId":"l2"}]}""";

        var result = Grader.Grade(Input(payload));

        result.IsCorrect.ShouldBeTrue();
        result.CorrectCount.ShouldBe(2);
        result.TotalCount.ShouldBe(2);
        Rows(result.Feedback).ShouldBe(new[] { ("z1", true), ("z2", true) });
    }

    [Fact]
    public void DragDropLabelingGrader_Grade_PartiallyCorrect_NamesTheWrongZoneInFeedback()
    {
        // z1 doğru, z2 boş bırakıldı → kısmen doğru: IsCorrect false, geri bildirimde z2 yanlış.
        var payload = """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"z1","draggableId":"l1"}]}""";

        var result = Grader.Grade(Input(payload));

        result.IsCorrect.ShouldBeFalse();
        result.CorrectCount.ShouldBe(1);
        result.TotalCount.ShouldBe(2);
        Rows(result.Feedback).ShouldBe(new[] { ("z1", true), ("z2", false) });
    }

    [Fact]
    public void DragDropLabelingGrader_Grade_WrongCardInZone_MarksOnlyThatZoneWrong()
    {
        // z1'e yanlış kart (SECRET-LBL-7), z2 doğru.
        var payload = """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"z1","draggableId":"SECRET-LBL-7"},{"dropZoneId":"z2","draggableId":"l2"}]}""";

        var result = Grader.Grade(Input(payload));

        result.CorrectCount.ShouldBe(1);
        Rows(result.Feedback).ShouldBe(new[] { ("z1", false), ("z2", true) });
    }

    [Fact]
    public void DragDropLabelingGrader_Grade_FeedbackNeverContainsTheExpectedDraggableIds()
    {
        // Çözümdeki kart id'leri (l1, l2) ve yanlış denemede kullanılan kart: geri bildirim yalnız zone id + doğru/yanlış taşır.
        var payload = """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"z1","draggableId":"SECRET-LBL-7"}]}""";

        var result = Grader.Grade(Input(payload));

        result.Feedback.ShouldNotBeNull();
        result.Feedback!.ShouldNotContain("l1");
        result.Feedback.ShouldNotContain("l2");
        result.Feedback.ShouldNotContain("\"draggableId\"");
        result.Feedback.ShouldNotContain("placements");
    }

    [Fact]
    public void DragDropLabelingGrader_Grade_ExtraZoneFilledByStudent_CountsAsAlwaysWrongRow()
    {
        // z9 çözümde yok; öğrenci doldurdu → hep yanlış satır, toplam 3.
        var payload = """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"z1","draggableId":"l1"},{"dropZoneId":"z2","draggableId":"l2"},{"dropZoneId":"z9","draggableId":"SECRET-LBL-7"}]}""";

        var result = Grader.Grade(Input(payload));

        result.IsCorrect.ShouldBeFalse();
        result.CorrectCount.ShouldBe(2);
        result.TotalCount.ShouldBe(3);
        Rows(result.Feedback).ShouldBe(new[] { ("z1", true), ("z2", true), ("z9", false) });
    }

    [Fact]
    public void DragDropLabelingGrader_Grade_NullPayload_EveryExpectedZoneIsWrong()
    {
        var result = Grader.Grade(Input(payload: null));

        result.IsCorrect.ShouldBeFalse();
        result.CorrectCount.ShouldBe(0);
        result.TotalCount.ShouldBe(2);
        Rows(result.Feedback).ShouldBe(new[] { ("z1", false), ("z2", false) });
    }

    [Fact]
    public void DragDropLabelingGrader_Grade_EmptyPlacementsPayload_EveryExpectedZoneIsWrong()
    {
        var result = Grader.Grade(Input("""{"version":1,"type":"dragDropLabeling","placements":[]}"""));

        result.IsCorrect.ShouldBeFalse();
        result.CorrectCount.ShouldBe(0);
        result.TotalCount.ShouldBe(2);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{bad")]
    [InlineData("""{"placements":[]}""")]
    public void DragDropLabelingGrader_Grade_NoUsableSolution_IsUngradable(string? solution)
    {
        var result = Grader.Grade(Input(
            """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"z1","draggableId":"l1"}]}""", solution));

        result.ShouldBe(InteractionGradeResult.Ungradable);
        result.IsCorrect.ShouldBeFalse();
        result.TotalCount.ShouldBe(0);
        result.Feedback.ShouldBeNull();
    }

    [Fact]
    public void DragDropLabelingGrader_Grade_MalformedStoredPayload_TreatedAsEmptyWithoutThrowing()
    {
        var result = Grader.Grade(Input("{bad json"));

        result.IsCorrect.ShouldBeFalse();
        result.CorrectCount.ShouldBe(0);
        result.TotalCount.ShouldBe(2);
    }

    [Fact]
    public void InteractionGradeResult_Of_ZeroRows_IsNeverCorrect()
    {
        // Güvence: IsCorrect yalnız TotalCount > 0 iken doğru olabilir.
        InteractionGradeResult.Of(0, 0).IsCorrect.ShouldBeFalse();
    }

    // ---------------------------------------------------------------- DragDropLabelingGrader.Validate

    [Fact]
    public void DragDropLabelingGrader_Validate_NullPayload_IsAlwaysValid()
    {
        Grader.Validate(null, null).ShouldBeTrue();
        Grader.Validate(Plan, null).ShouldBeTrue();
    }

    [Fact]
    public void DragDropLabelingGrader_Validate_EmptyPlacements_IsValid()
    {
        Grader.Validate(Plan, """{"version":1,"type":"dragDropLabeling","placements":[]}""").ShouldBeTrue();
    }

    [Fact]
    public void DragDropLabelingGrader_Validate_KnownZonesAndCardsWithinLimits_IsValid()
    {
        Grader.Validate(Plan, """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"z1","draggableId":"l1"},{"dropZoneId":"z2","draggableId":"l2"}]}""")
            .ShouldBeTrue();
    }

    [Fact]
    public void DragDropLabelingGrader_Validate_PayloadAboveMaxLength_IsRejected()
    {
        var oversized = "{" + new string(' ', DragDropLabelingGrader.MaxPayloadLength) + "}";

        Grader.Validate(Plan, oversized).ShouldBeFalse();
    }

    public static TheoryData<string> InvalidPayloads => new()
    {
        "not json",
        "{bad",
        """["array-root"]""",
        """{"version":1,"type":"dragDropLabeling","placements":[],"placements":[]}""", // yinelenen anahtar
        """{"version":1,"type":"mcq","placements":[]}""",                             // yanlış tip
        """{"version":1,"placements":[]}""",                                          // tip yok
        """{"version":2,"type":"dragDropLabeling","placements":[]}""",                // desteklenmeyen sürüm
        """{"version":"1","type":"dragDropLabeling","placements":[]}""",              // sürüm sayı değil
        """{"version":1,"type":"dragDropLabeling"}""",                                // placements yok
        """{"version":1,"type":"dragDropLabeling","placements":{}}""",                // placements dizi değil
        """{"version":1,"type":"dragDropLabeling","placements":["z1"]}""",            // öğe nesne değil
        """{"version":1,"type":"dragDropLabeling","placements":[{"draggableId":"l1"}]}""",            // dropZoneId yok
        """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"z1"}]}""",             // draggableId yok
        """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":1,"draggableId":"l1"}]}""", // sayı id
        """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"","draggableId":"l1"}]}""", // boş id
        """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"zX","draggableId":"l1"}]}""", // plan dışı hedef
        """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"z1","draggableId":"lX"}]}""", // plan dışı kart
        """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"z1","draggableId":"l1"},{"dropZoneId":"z1","draggableId":"l2"}]}""", // hedef iki kez
        """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"z1","draggableId":"l1"},{"dropZoneId":"z2","draggableId":"l1"}]}""", // kart iki kez
    };

    [Theory]
    [MemberData(nameof(InvalidPayloads))]
    public void DragDropLabelingGrader_Validate_MalformedOrOutOfPlanPayload_IsRejected(string payload)
    {
        Grader.Validate(Plan, payload).ShouldBeFalse();
    }

    [Fact]
    public void DragDropLabelingGrader_Validate_NonEmptyPlacementsWithoutPlan_IsRejected()
    {
        Grader.Validate(null, """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"z1","draggableId":"l1"}]}""")
            .ShouldBeFalse();
    }

    [Fact]
    public void DragDropLabelingGrader_Validate_PlanWithoutDropZonesArray_IsRejectedForNonEmptyPlacements()
    {
        Grader.Validate("""{"draggables":[{"id":"l1"}]}""",
            """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"z1","draggableId":"l1"}]}""")
            .ShouldBeFalse();
    }

    // ---------------------------------------------------------------- McqGrader (bugünkü kural birebir)

    [Fact]
    public void McqGrader_Grade_SelectedEqualsCorrect_IsOneOfOne()
    {
        var result = new McqGrader().Grade(new InteractionGradeInput(null, null, null, SelectedAnswerId: 7, CorrectAnswerId: 7));

        result.IsCorrect.ShouldBeTrue();
        result.CorrectCount.ShouldBe(1);
        result.TotalCount.ShouldBe(1);
        result.Feedback.ShouldBeNull();
    }

    [Fact]
    public void McqGrader_Grade_SelectedDiffersFromCorrect_IsZeroOfOne()
    {
        var result = new McqGrader().Grade(new InteractionGradeInput(null, null, null, SelectedAnswerId: 7, CorrectAnswerId: 8));

        result.IsCorrect.ShouldBeFalse();
        result.CorrectCount.ShouldBe(0);
        result.TotalCount.ShouldBe(1);
    }

    [Theory]
    [InlineData(null, 8)]
    [InlineData(7, null)]
    [InlineData(null, null)]
    public void McqGrader_Grade_MissingSelectionOrCorrectAnswer_IsWrong(int? selected, int? correct)
    {
        var result = new McqGrader().Grade(new InteractionGradeInput(null, null, null, selected, correct));

        result.IsCorrect.ShouldBeFalse();
        result.CorrectCount.ShouldBe(0);
        result.TotalCount.ShouldBe(1);
    }

    [Fact]
    public void McqGrader_Validate_AcceptsAnyPayload_LikeTheLegacyRule()
    {
        // MCQ payload'a bakmaz (eski davranış); doğrulama reddetmez.
        new McqGrader().Validate(null, "{bad").ShouldBeTrue();
        new McqGrader().Validate(Plan, """{"anything":1}""").ShouldBeTrue();
    }

    // ---------------------------------------------------------------- InteractionGraderRegistry

    [Theory]
    [InlineData("dragDropLabeling", typeof(DragDropLabelingGrader))]
    [InlineData("DRAGDROPLABELING", typeof(DragDropLabelingGrader))]
    [InlineData("  dragDropLabeling  ", typeof(DragDropLabelingGrader))]
    [InlineData("mcq", typeof(McqGrader))]
    [InlineData(null, typeof(McqGrader))]
    [InlineData("", typeof(McqGrader))]
    [InlineData("   ", typeof(McqGrader))]
    [InlineData("unknownFutureType", typeof(McqGrader))]
    public void InteractionGraderRegistry_Resolve_MapsTypeCaseInsensitivelyAndFallsBackToMcq(string? type, Type expected)
    {
        InteractionGraderRegistry.Default.Resolve(type).GetType().ShouldBe(expected);
    }

    [Fact]
    public void InteractionGraderRegistry_Constructor_DuplicateTypesDifferingOnlyByCase_Throws()
    {
        var act = () => new InteractionGraderRegistry(new IInteractionGrader[] { new McqGrader(), new FakeGrader("MCQ") });

        Should.Throw<InvalidOperationException>(act);
    }

    [Fact]
    public void InteractionGraderRegistry_Constructor_WithoutMcq_ResolvesNullToBuiltInMcq()
    {
        var registry = new InteractionGraderRegistry(new IInteractionGrader[] { new DragDropLabelingGrader() });

        registry.Resolve(null).ShouldBeOfType<McqGrader>();
        registry.Resolve("dragDropLabeling").ShouldBeOfType<DragDropLabelingGrader>();
    }

    private sealed class FakeGrader(string type) : IInteractionGrader
    {
        public string InteractionType => type;
        public bool Validate(string? plan, string? payload) => true;
        public InteractionGradeResult Grade(InteractionGradeInput input) => InteractionGradeResult.Ungradable;
    }
}
