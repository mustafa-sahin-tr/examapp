using System.Text.Json;
using ExamApp.Api.Services.Interactions;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #584: kontrolcülerin <see cref="IInteractionGrader.Analyze"/>'ı — öğe başına doğru/yanlış ve öğrencinin cevabı (öğretmen
/// etkinlik analizi). Öğeler öğrenciden bağımsız sırayla döner (cevapsız öğe de), doğruluk kontrolcünün satır kuralıyla aynıdır,
/// çözümsüz soru ve openText boş döner.
/// </summary>
public class InteractionAnalyzeTests
{
    private static InteractionGradeInput Input(string plan, string? solution, string? payload)
        => new(plan, solution, payload, SelectedAnswerId: null, CorrectAnswerId: null);

    // ---------------------------------------------------------------- dragPlace

    private const string PlacePlan =
        """{"version":1,"type":"dragPlace","mode":"place","targets":[{"id":"t1","label":"İskelet","capacity":2},{"id":"t2","label":"Kaslar","capacity":2}],"cards":[{"id":"c1","label":"Kemik"},{"id":"c2","label":"Kas lifi"},{"id":"c3","box":{"x":0,"y":0,"width":5,"height":5}},{"id":"c4","label":"Çeldirici"}]}""";

    private const string PlaceSolution = """{"t1":["c1","c3"],"t2":["c2"]}""";

    private static string PlacePayload(params (string T, string C)[] placements) => JsonSerializer.Serialize(new
    {
        version = 1, type = "dragPlace",
        placements = placements.Select(p => new { targetId = p.T, cardId = p.C }),
    });

    [Fact]
    public void DragPlace_ItemPerCard_AnswerIsTargetWherePlaced()
    {
        var items = new DragPlaceGrader().Analyze(Input(PlacePlan, PlaceSolution,
            PlacePayload(("t2", "c1"), ("t2", "c2"), ("t1", "c4"))));

        items.Select(i => i.ItemId).ShouldBe(new[] { "c1", "c2", "c3", "c4" });
        items[0].ShouldBe(new InteractionItemResponse("c1", "Kemik", false, "t2", "Kaslar"));
        items[1].ShouldBe(new InteractionItemResponse("c2", "Kas lifi", true, "t2", "Kaslar"));
        // Etiketsiz (görsel kırpma) kart plan sırasıyla adlanır; yerleştirilmemiş = cevapsız.
        items[2].ShouldBe(new InteractionItemResponse("c3", "#3", false, null, null));
        // Çeldirici: bir yere konduysa yanlış.
        items[3].ShouldBe(new InteractionItemResponse("c4", "Çeldirici", false, "t1", "İskelet", Distractor: true));
    }

    [Fact]
    public void DragPlace_UnusedDistractor_IsCorrect_AndEmptyPayloadReturnsEveryCard()
    {
        var items = new DragPlaceGrader().Analyze(Input(PlacePlan, PlaceSolution, payload: null));

        items.Count.ShouldBe(4);
        items.Take(3).ShouldAllBe(i => !i.Correct && i.AnswerKey == null);
        items[3].Correct.ShouldBeTrue();
        items[3].Distractor.ShouldBeTrue();
    }

    [Fact]
    public void DragPlace_WithoutSolution_IsEmpty()
        => new DragPlaceGrader().Analyze(Input(PlacePlan, null, PlacePayload(("t1", "c1")))).ShouldBeEmpty();

    // ---------------------------------------------------------------- matrixChoice

    [Fact]
    public void MatrixChoice_ItemPerRow_AnswerIsChosenColumns()
    {
        const string plan =
            """{"version":1,"type":"matrixChoice","multiPerRow":false,"columns":[{"id":"d","label":"D"},{"id":"y","label":"Y"}],"rows":[{"id":"r1","label":"Su 100 °C'de kaynar.","box":{"x":0,"y":0,"width":10,"height":5}},{"id":"r2","box":{"x":0,"y":5,"width":10,"height":5}}]}""";
        const string solution = """{"r1":["d"],"r2":["y"]}""";
        const string payload = """{"version":1,"type":"matrixChoice","selections":{"r1":["y"]}}""";

        var items = new MatrixChoiceGrader().Analyze(Input(plan, solution, payload));

        items.ShouldBe(new[]
        {
            new InteractionItemResponse("r1", "Su 100 °C'de kaynar.", false, "y", "Y"),
            new InteractionItemResponse("r2", "#2", false, null, null),
        });
    }

    // ---------------------------------------------------------------- match

    [Fact]
    public void Match_ItemPerGradableLeft_SkipsPrelinked()
    {
        const string plan =
            """{"version":1,"type":"match","left":[{"id":"l1","label":"Ankara"},{"id":"l2","label":"Paris"},{"id":"l3","label":"Roma"}],"right":[{"id":"r1","label":"Türkiye"},{"id":"r2","label":"Fransa"},{"id":"r3","label":"İtalya"}],"prelinked":[{"leftId":"l1","rightId":"r1"}]}""";
        const string solution = """{"l2":"r2","l3":"r3"}""";
        const string payload = """{"version":1,"type":"match","pairs":[{"leftId":"l2","rightId":"r3"},{"leftId":"l3","rightId":"r3"}]}""";

        var items = new MatchGrader().Analyze(Input(plan, solution, payload));

        items.ShouldBe(new[]
        {
            new InteractionItemResponse("l2", "Paris", false, "r3", "İtalya"),
            new InteractionItemResponse("l3", "Roma", true, "r3", "İtalya"),
        });
    }

    // ---------------------------------------------------------------- slotFill

    [Fact]
    public void SlotFill_SelectUsesOptionLabel_TextIsGroupedCaseInsensitively()
    {
        const string plan =
            """{"version":1,"type":"slotFill","slots":[{"id":"s1","label":"Başkent","box":{"x":0,"y":0,"width":10,"height":5},"input":{"kind":"text"}},{"id":"s2","box":{"x":0,"y":5,"width":10,"height":5},"input":{"kind":"select","options":[{"id":"o1","label":"büyük"},{"id":"o2","label":"küçük"}]}}]}""";
        const string solution = """{"s1":{"accept":["Ankara"]},"s2":{"optionIds":["o1"]}}""";

        var grader = new SlotFillGrader();
        var first = grader.Analyze(Input(plan, solution, """{"version":1,"type":"slotFill","values":{"s1":"  İstanbul ","s2":"o2"}}"""));
        var second = grader.Analyze(Input(plan, solution, """{"version":1,"type":"slotFill","values":{"s1":"istanbul"}}"""));

        first[0].ShouldBe(new InteractionItemResponse("s1", "Başkent", false, "istanbul", "İstanbul"));
        first[1].ShouldBe(new InteractionItemResponse("s2", "#2", false, "o2", "küçük"));
        second[0].AnswerKey.ShouldBe(first[0].AnswerKey);
        second[1].AnswerKey.ShouldBeNull();
    }

    // ---------------------------------------------------------------- textSelect

    [Fact]
    public void TextSelect_Select_ItemPerTarget_PlusSelectedDistractorTokens()
    {
        const string plan =
            """{"version":1,"type":"textSelect","mode":"select","tokens":[{"id":"t1","text":"Ali","line":1,"boxes":[{"x":0,"y":0,"width":5,"height":5}]},{"id":"t2","text":"okula","line":1,"boxes":[{"x":5,"y":0,"width":5,"height":5}]},{"id":"t3","text":"gitti","line":1,"boxes":[{"x":10,"y":0,"width":5,"height":5}]}]}""";
        const string solution = """{"targets":[{"tokenIds":["t1"]},{"tokenIds":["t2"]}]}""";
        const string payload = """{"version":1,"type":"textSelect","selected":["t1","t3"]}""";

        var items = new TextSelectGrader().Analyze(Input(plan, solution, payload));

        items.ShouldBe(new[]
        {
            new InteractionItemResponse("target:0", "Ali", true, TextSelectGrader.AnswerSelected, null),
            new InteractionItemResponse("target:1", "okula", false, null, null),
            new InteractionItemResponse("t3", "gitti", false, TextSelectGrader.AnswerSelected, null, Distractor: true),
        });
    }

    [Fact]
    public void TextSelect_Punctuation_ItemPerGap_NamedAfterPreviousWord()
    {
        const string plan =
            """{"version":1,"type":"textSelect","mode":"punctuation","palette":[".","?"],"tokens":[{"id":"t1","text":"Geldin","line":1,"boxes":[{"x":0,"y":0,"width":5,"height":5}]},{"id":"g1","text":"( )","line":1,"boxes":[{"x":5,"y":0,"width":5,"height":5}],"gap":true}]}""";
        const string solution = """{"gaps":{"g1":{"accept":["?"]}}}""";
        const string payload = """{"version":1,"type":"textSelect","values":{"g1":"."}}""";

        var items = new TextSelectGrader().Analyze(Input(plan, solution, payload));

        items.ShouldBe(new[] { new InteractionItemResponse("g1", "Geldin ( )", false, ".", ".") });
    }

    // ---------------------------------------------------------------- dragDropLabeling / mcq / openText

    [Fact]
    public void DragDropLabeling_ItemPerSolutionZone_AnswerIsPlacedCard()
    {
        const string plan =
            """{"version":1,"type":"dragDropLabeling","dropZones":[{"id":"z1","label":"Kalp"},{"id":"z2"}],"draggables":[{"id":"d1","label":"Pompa"},{"id":"d2","label":"Süzgeç"}]}""";
        const string solution = """{"placements":[{"draggableId":"d1","dropZoneId":"z1"},{"draggableId":"d2","dropZoneId":"z2"}]}""";
        const string payload = """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"z1","draggableId":"d2"}]}""";

        var items = new DragDropLabelingGrader().Analyze(Input(plan, solution, payload));

        items.ShouldBe(new[]
        {
            new InteractionItemResponse("z1", "Kalp", false, "d2", "Süzgeç"),
            new InteractionItemResponse("z2", "#2", false, null, null),
        });
    }

    [Fact]
    public void Mcq_SingleItem_AnswerIsSelectedOptionId_UngradableIsEmpty()
    {
        var grader = new McqGrader();

        grader.Analyze(new InteractionGradeInput(null, null, null, 7, 5))
            .ShouldBe(new[] { new InteractionItemResponse(McqGrader.ItemId, "#1", false, "7", "7") });
        grader.Analyze(new InteractionGradeInput(null, null, null, 5, 5)).Single().Correct.ShouldBeTrue();
        grader.Analyze(new InteractionGradeInput(null, null, null, 5, null)).ShouldBeEmpty();
    }

    [Fact]
    public void OpenText_HasNoItems()
        => ((IInteractionGrader)new OpenTextGrader()).Analyze(Input("""{"version":1,"type":"openText"}""", null,
            """{"version":1,"type":"openText","text":"Bir cevap"}""")).ShouldBeEmpty();
}
