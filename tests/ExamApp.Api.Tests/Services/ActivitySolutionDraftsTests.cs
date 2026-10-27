using System.Drawing;
using System.Text.Json.Nodes;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Services.Labeling;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #580: Claude'un çözüm şeması → tipin çözüm biçimi (taslak id'leri) eşlemesi ve sonucun tipin <c>ValidateDefinition</c>'ından
/// geçmesi; cevap anahtarı maddelerinin eşlenmesi (etiket temizleme, görsel kutu → sayfa pikseli); yapı JSON'unda kutu olmaması.
/// </summary>
public class ActivitySolutionDraftsTests
{
    internal const string MatrixPlan =
        """{"version":1,"type":"matrixChoice","multiPerRow":false,"columns":[{"id":"c1","label":"D"},{"id":"c2","label":"Y"}],"rows":[{"id":"r1","label":"Kedi memelidir.","box":{"x":0,"y":0,"width":10,"height":10}},{"id":"r2","label":"Balık uçar.","box":{"x":0,"y":10,"width":10,"height":10}}]}""";

    internal const string SlotPlan =
        """{"version":1,"type":"slotFill","slots":[{"id":"s1","box":{"x":0,"y":0,"width":10,"height":10},"input":{"kind":"number","entry":"plain","unit":"cm"}},{"id":"s2","box":{"x":0,"y":20,"width":10,"height":10},"input":{"kind":"text"}},{"id":"s3","box":{"x":0,"y":40,"width":10,"height":10},"input":{"kind":"select","options":[{"id":"s3o1","label":"az"},{"id":"s3o2","label":"çok"}]}},{"id":"s4","box":{"x":0,"y":60,"width":10,"height":10},"input":{"kind":"palette","symbols":["<",">","="]}}]}""";

    internal const string DragPlan =
        """{"version":1,"type":"dragPlace","mode":"place","reuse":false,"targets":[{"id":"t1","label":"Canlı","capacity":2},{"id":"t2","label":"Cansız","capacity":2}],"cards":[{"id":"k1","label":"kedi"},{"id":"k2","label":"taş"},{"id":"k3","label":"ağaç"}]}""";

    internal const string MatchPlan =
        """{"version":1,"type":"match","left":[{"id":"l1","label":"Ankara"},{"id":"l2","label":"İzmir"},{"id":"l3","label":"Bursa"}],"right":[{"id":"r1","label":"başkent"},{"id":"r2","label":"Ege"},{"id":"r3","label":"Marmara"},{"id":"r4","label":"çeldirici"}],"prelinked":[{"leftId":"l1","rightId":"r1"}]}""";

    private static JsonObject Plan(string json) => (JsonObject)JsonNode.Parse(json)!;

    private static bool Valid(string type, string plan, JsonObject solution) =>
        InteractionGraderRegistry.Default.Resolve(type).ValidateDefinition(plan, solution.ToJsonString());

    [Fact]
    public void Map_MatrixChoice_DropsUnknownIds_AndIsValid()
    {
        var solution = ActivitySolutionDrafts.Map(new ActivitySolutionDrafts.MatrixSolutionDraft(
        [
            new("r1", ["c1", "c1", "zz"]),
            new("r2", ["c2"]),
            new("r9", ["c1"]),
        ]), Plan(MatrixPlan));

        solution.ToJsonString().ShouldBe("""{"r1":["c1"],"r2":["c2"]}""");
        Valid(MatrixChoiceGrader.Type, MatrixPlan, solution).ShouldBeTrue();
    }

    [Fact]
    public void Map_MatrixChoice_MissingRow_IsInvalid()
    {
        var solution = ActivitySolutionDrafts.Map(new ActivitySolutionDrafts.MatrixSolutionDraft([new("r1", ["c1"])]), Plan(MatrixPlan));
        Valid(MatrixChoiceGrader.Type, MatrixPlan, solution).ShouldBeFalse();
    }

    [Fact]
    public void Map_SlotFill_ByKind_AndIsValid()
    {
        var solution = ActivitySolutionDrafts.Map(new ActivitySolutionDrafts.SlotSolutionDraft(
        [
            new("s1", "12 cm", []),           // birim ipucu atılır
            new("s2", "Ankara", ["ankara", "ANKARA'da"]), // normalize hâline göre tekil
            new("s3", "çok", []),             // etiketle seçenek id'si
            new("s4", ">", ["≥"]),             // palette dışı sembol atılır
        ]), Plan(SlotPlan));

        solution["s1"]!.ToJsonString().ShouldBe("""{"value":"12"}""");
        solution["s2"]!["accept"]!.AsArray().Select(v => (string?)v).ShouldBe(["Ankara", "ANKARA'da"]);
        solution["s3"]!.ToJsonString().ShouldBe("""{"optionIds":["s3o2"]}""");
        solution["s4"]!["accept"]!.AsArray().Select(v => (string?)v).ShouldBe([">"]);
        Valid(SlotFillGrader.Type, SlotPlan, solution).ShouldBeTrue();
    }

    [Fact]
    public void Map_SlotFill_SelectById_AndUnparseableNumberIsLeftOut()
    {
        var solution = ActivitySolutionDrafts.Map(new ActivitySolutionDrafts.SlotSolutionDraft(
        [
            new("s1", "on iki", []),
            new("s3", "s3o1", []),
        ]), Plan(SlotPlan));

        solution.ContainsKey("s1").ShouldBeFalse();
        solution["s3"]!.ToJsonString().ShouldBe("""{"optionIds":["s3o1"]}""");
        Valid(SlotFillGrader.Type, SlotPlan, solution).ShouldBeFalse(); // eksik kutu → öğretmen düzeltir
    }

    [Fact]
    public void Map_DragPlace_EveryTargetPresent_AndIsValid()
    {
        var solution = ActivitySolutionDrafts.Map(new ActivitySolutionDrafts.DragSolutionDraft(
        [
            new("t1", ["k1", "k3", "k9"]),
        ]), Plan(DragPlan));

        solution.ToJsonString().ShouldBe("""{"t1":["k1","k3"],"t2":[]}""");
        Valid(DragPlaceGrader.Type, DragPlan, solution).ShouldBeTrue();
    }

    [Fact]
    public void Map_Match_SkipsPrelinkedAndReusedRight_AndIsValid()
    {
        var solution = ActivitySolutionDrafts.Map(new ActivitySolutionDrafts.MatchSolutionDraft(
        [
            new("l1", "r1"),   // örnek
            new("l2", "r2"),
            new("l3", "r2"),   // sağ tekrar → atılır
            new("l3", "r3"),
        ]), Plan(MatchPlan));

        solution.ToJsonString().ShouldBe("""{"l2":"r2","l3":"r3"}""");
        Valid(MatchGrader.Type, MatchPlan, solution).ShouldBeTrue();
    }

    [Fact]
    public void Structure_RemovesBoxesAndCells()
    {
        var structure = ActivitySolutionDrafts.Structure(MatrixPlan);
        structure.ShouldNotContain("box");
        structure.ShouldContain("\"r1\"");
        structure.ShouldContain("Kedi memelidir.");
    }

    [Fact]
    public void TypeExplanation_ExistsForEveryActivityType()
    {
        foreach (var type in ActivityStructureSettings.Types)
            ActivitySolutionDrafts.TypeExplanation(type).ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public void Map_AnswerKey_CleansLabels_MapsVisualBoxToPage_DropsEmptyEntries()
    {
        // Gönderilen görsel 2 kat küçük (sayfa 1000x1400 → 500x700).
        var region = new EncodedRegion([], 500, 700, 0, 0, 2d, 2d);
        var draft = new ActivitySolutionDrafts.AnswerKeyDraft(
        [
            new(12, "  Etkinlik\n 3 ", "a", "3/4", false, 0, 0, 0, 0),
            new(0, "Etkinlik 4", "", "", true, 10, 20, 60, 80),   // görsel; sayfa yok
            new(13, "", "", "", false, 0, 0, 0, 0),              // boş → atılır
            new(14, "Etkinlik 5", "", "", true, 0, 0, 1, 1),     // kutusu çok küçük ve cevapsız → atılır
        ]);

        var entries = ActivitySolutionDrafts.Map(draft, region, new Size(1000, 1400));

        entries.Count.ShouldBe(2);
        entries[0].ShouldBe(new AnswerKeyReadEntry(12, "Etkinlik 3", "a", "3/4", false, null));
        entries[1].PrintedPage.ShouldBeNull();
        entries[1].Visual.ShouldBeTrue();
        entries[1].Box.ShouldBe(new Rectangle(20, 40, 100, 120));
    }

    // Review D-3: sayfa başına görsel madde sınırı.
    [Fact]
    public void Map_AnswerKey_CapsVisualEntries()
    {
        var region = new EncodedRegion([], 1000, 1000, 0, 0, 1d, 1d);
        var draft = new ActivitySolutionDrafts.AnswerKeyDraft(Enumerable.Range(0, 65)
            .Select(i => new ActivitySolutionDrafts.AnswerKeyDraftEntry(1, $"Etkinlik {i}", "", i % 2 == 0 ? "" : "x", true, 0, i * 10, 50, i * 10 + 8))
            .ToList());

        var entries = ActivitySolutionDrafts.Map(draft, region, new Size(1000, 1000));

        entries.Count(e => e.Visual).ShouldBe(60);
        // Sınırı aşan görsel maddeler: metni olanlar metin olarak kalır, metinsizler atılır.
        entries.Skip(60).ShouldAllBe(e => !e.Visual && e.AnswerText == "x");
    }
}
