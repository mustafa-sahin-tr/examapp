using System.Drawing;
using System.Text.Json;
using System.Text.Json.Nodes;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Services.Labeling;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #579: tip şeması → InteractionPlan v1 eşlemesi (id üretimi, kırpma → blok-göreli koordinat dönüşümü, etiket temizleme)
/// ve üretilen planın tipin <c>ValidateDefinition</c>'ından geçmesi.
/// </summary>
public class ActivityDraftsTests
{
    // Kırpma (Claude'un gördüğü) 100x50, sayfada (84, 184)'ten başlıyor ve 2 kat küçültülmüş (ScaleX/Y = 2). Blok kutusu sayfada
    // (100, 200, 160x60): kırpma = blok + 16 px pay.
    private static readonly Rectangle Block = new(100, 200, 160, 60);
    private static readonly ActivityDrafts.BlockFrame Frame =
        new(new EncodedRegion([], 100, 50, 84, 184, 2d, 2d), Block);

    private static JsonObject Box(JsonNode? node) => node.ShouldBeOfType<JsonObject>();

    private static (int X, int Y, int W, int H) Xywh(JsonNode? box)
    {
        var b = Box(box);
        return ((int)b["x"]!, (int)b["y"]!, (int)b["width"]!, (int)b["height"]!);
    }

    private static bool Valid(string type, JsonObject plan) =>
        InteractionGraderRegistry.Default.Resolve(type).ValidateDefinition(plan.ToJsonString(), null);

    [Fact]
    public void BlockFrame_ConvertsCropPixelsToBlockRelativePagePixels()
    {
        // Kırpma (10,10)-(30,20) → sayfa (84+20, 184+20) 40x20 → bloğa göre (4, 4) 40x20.
        Xywh(Frame.Box(10, 10, 30, 20)).ShouldBe((4, 4, 40, 20));
    }

    [Fact]
    public void BlockFrame_ClampsToBlockBox_AndDropsBoxesOutsideIt()
    {
        // Pay içindeki kısım atılır: sayfa (84,184)-(104,204) ∩ blok → (100,200) 4x4 → (0, 0) 4x4.
        Xywh(Frame.Box(0, 0, 10, 10)).ShouldBe((0, 0, 4, 4));
        // Tamamen payda (blok dışında) ya da sıfır alanlı → kutu yok.
        Frame.Box(0, 0, 5, 5).ShouldBeNull();
        Frame.Box(0, 0, 0, 0).ShouldBeNull();
    }

    [Fact]
    public void Map_MatrixChoice_GeneratesIdsMapsCellsAndPassesValidation()
    {
        var draft = new ActivityDrafts.MatrixDraft(
            "  Doğru olanlara D,\n yanlış olanlara Y yazınız. ",
            false,
            [new("D"), new("Y")],
            [
                new("1. Kedi bir memelidir.", 10, 10, 90, 20, [new(0, 70, 10, 80, 20), new(1, 80, 10, 90, 20), new(5, 0, 0, 10, 10)]),
                new("", 10, 25, 90, 35, []),
            ]);

        var mapped = ActivityDrafts.Map(draft, Frame);

        mapped.Instruction.ShouldBe("Doğru olanlara D, yanlış olanlara Y yazınız.");
        var plan = mapped.Plan;
        ((string)plan["type"]!).ShouldBe(MatrixChoiceGrader.Type);
        ((int)plan["version"]!).ShouldBe(1);
        plan["columns"]!.AsArray().Select(c => ((string)c!["id"]!, (string)c["label"]!)).ShouldBe([("c1", "D"), ("c2", "Y")]);
        var rows = plan["rows"]!.AsArray();
        rows.Select(r => (string)r!["id"]!).ShouldBe(["r1", "r2"]);
        ((string)rows[0]!["label"]!).ShouldBe("1. Kedi bir memelidir.");
        rows[1]!.AsObject().ContainsKey("label").ShouldBeFalse();     // boş etiket yazılmaz
        Xywh(rows[0]!["box"]).ShouldBe((4, 4, 156, 20));               // sayfada (104, 204) 160x20 → blok sağ kenarına kırpılır
        var cells = rows[0]!["cells"]!.AsObject();
        cells.Select(c => c.Key).ShouldBe(["c1", "c2"]);                // geçersiz kolon indeksi (5) atılır
        Xywh(cells["c1"]).ShouldBe((124, 4, 20, 20));
        rows[1]!.AsObject().ContainsKey("cells").ShouldBeFalse();

        Valid(MatrixChoiceGrader.Type, plan).ShouldBeTrue();
    }

    [Fact]
    public void Map_MatrixChoice_RowWithoutBoxUsesCellUnion_AndRowWithNothingIsDropped()
    {
        var draft = new ActivityDrafts.MatrixDraft("", true,
            [new("A"), new("B")],
            [
                new("satır", 0, 0, 0, 0, [new(0, 20, 10, 30, 20), new(1, 40, 10, 50, 20)]),
                new("boş", 0, 0, 0, 0, []),
            ]);

        var plan = ActivityDrafts.Map(draft, Frame).Plan;

        var rows = plan["rows"]!.AsArray();
        rows.Count.ShouldBe(1);
        Xywh(rows[0]!["box"]).ShouldBe((24, 4, 60, 20));
        ((bool)plan["multiPerRow"]!).ShouldBeTrue();
        Valid(MatrixChoiceGrader.Type, plan).ShouldBeTrue();
    }

    [Fact]
    public void Map_MatrixChoice_SingleColumnDraftIsReturnedButInvalid()
    {
        var plan = ActivityDrafts.Map(new ActivityDrafts.MatrixDraft("", false, [new("D")], [new("x", 10, 10, 90, 20, [])]), Frame).Plan;

        Valid(MatrixChoiceGrader.Type, plan).ShouldBeFalse();
        ActivityPlanDiagnostics.Explain(MatrixChoiceGrader.Type, plan.ToJsonString())
            .ShouldBe(["labeling.activity.invalid.tooFewColumns"]);
    }

    [Fact]
    public void Map_SlotFill_MapsEveryInputKindWithGeneratedIds()
    {
        var draft = new ActivityDrafts.SlotDraft("Boşlukları doldurunuz.",
        [
            new("Toplam", "number", "fraction", "cm", [], [], 10, 10, 30, 20),
            new("", "TEXT", "none", "", [], [], 40, 10, 60, 20),
            new("Hava", "select", "none", "", ["az", "çok", "az", " "], [], 10, 25, 30, 35),
            new("", "palette", "none", "", [], ["<", ">", "=", "<", "too-long-symbol"], 40, 25, 60, 35),
            new("bilinmeyen", "drawing", "none", "", [], [], 70, 25, 90, 35),
            new("kutusuz", "text", "none", "", [], [], 0, 0, 0, 0),
        ]);

        var mapped = ActivityDrafts.Map(draft, Frame);

        var slots = mapped.Plan["slots"]!.AsArray();
        slots.Select(s => (string)s!["id"]!).ShouldBe(["s1", "s2", "s3", "s4", "s5"]);   // kutusuz yuva atlanır
        var number = slots[0]!["input"]!.AsObject();
        ((string)number["kind"]!).ShouldBe("number");
        ((string)number["entry"]!).ShouldBe("fraction");
        ((string)number["unit"]!).ShouldBe("cm");
        ((string)slots[1]!["input"]!["kind"]!).ShouldBe("text");
        slots[2]!["input"]!["options"]!.AsArray().Select(o => ((string)o!["id"]!, (string)o["label"]!))
            .ShouldBe([("s3o1", "az"), ("s3o2", "çok")]);                                  // tekrar ve boşluk atılır
        slots[3]!["input"]!["symbols"]!.AsArray().Select(s => (string)s!).ShouldBe(["<", ">", "="]);
        ((string)slots[4]!["input"]!["kind"]!).ShouldBe("text");                           // bilinmeyen tür → text
        Xywh(slots[0]!["box"]).ShouldBe((4, 4, 40, 20));

        Valid(SlotFillGrader.Type, mapped.Plan).ShouldBeTrue();
    }

    [Fact]
    public void Map_SlotFill_SelectWithOneOption_IsExplained()
    {
        var plan = ActivityDrafts.Map(new ActivityDrafts.SlotDraft("", [new("", "select", "none", "", ["tek"], [], 10, 10, 30, 20)]),
            Frame).Plan;

        Valid(SlotFillGrader.Type, plan).ShouldBeFalse();
        ActivityPlanDiagnostics.Explain(SlotFillGrader.Type, plan.ToJsonString())
            .ShouldBe(["labeling.activity.invalid.selectOptions"]);
    }

    [Fact]
    public void Map_DragPlace_PlaceMode_MapsTargetsCardsCapacityAndBoxes()
    {
        var draft = new ActivityDrafts.DragDraft("Kelimeleri yerleştiriniz.", "place", true,
        [
            new("Canlı", 3, 10, 10, 50, 20),
            new("", 0, 0, 0, 0, 0),             // kutusuz, etiketsiz hedef → görselin altında kova; kapasite ≥ 1
            new("Cansız", 99, 60, 10, 90, 20),
        ],
        [
            new("kedi", 10, 30, 20, 35),
            new("", 30, 30, 40, 35),            // resim kartı: yalnız kutu
            new("", 0, 0, 0, 0),                // yüzü yok → atlanır
            new("taş", 0, 0, 0, 0),
        ]);

        var plan = ActivityDrafts.Map(draft, Frame).Plan;

        ((string)plan["mode"]!).ShouldBe("place");
        ((bool)plan["reuse"]!).ShouldBeTrue();
        var targets = plan["targets"]!.AsArray();
        targets.Select(t => ((string)t!["id"]!, (int)t["capacity"]!)).ShouldBe([("t1", 3), ("t2", 1), ("t3", 30)]);
        targets[1]!.AsObject().ContainsKey("box").ShouldBeFalse();
        var cards = plan["cards"]!.AsArray();
        cards.Select(c => (string)c!["id"]!).ShouldBe(["k1", "k2", "k3"]);
        cards[1]!.AsObject().ContainsKey("label").ShouldBeFalse();
        Xywh(cards[1]!["box"]).ShouldBe((44, 44, 20, 10));
        cards[2]!.AsObject().ContainsKey("box").ShouldBeFalse();

        Valid(DragPlaceGrader.Type, plan).ShouldBeTrue();
    }

    [Fact]
    public void Map_DragPlace_OrderMode_ForcesCapacityOneAndNoReuse()
    {
        var plan = ActivityDrafts.Map(new ActivityDrafts.DragDraft("", "ORDER", true,
            [new("1", 4, 10, 10, 20, 20), new("2", 2, 30, 10, 40, 20)],
            [new("a", 0, 0, 0, 0), new("b", 0, 0, 0, 0)]), Frame).Plan;

        ((string)plan["mode"]!).ShouldBe("order");
        ((bool)plan["reuse"]!).ShouldBeFalse();
        plan["targets"]!.AsArray().ShouldAllBe(t => (int)t!["capacity"]! == 1);
        Valid(DragPlaceGrader.Type, plan).ShouldBeTrue();
    }

    [Fact]
    public void Map_Match_MapsItemsAndPrelinkedByOriginalIndex()
    {
        var draft = new ActivityDrafts.MatchDraft("Eşleştiriniz.",
            [new("", 0, 0, 0, 0), new("Ankara", 10, 10, 40, 20), new("İzmir", 10, 25, 40, 35)],   // ilki yüzsüz → atlanır
            [new("Başkent", 60, 10, 90, 20), new("Liman", 60, 25, 90, 35), new("Dağ", 60, 40, 90, 45)],
            [new(1, 0), new(2, 0), new(9, 1)]);   // (1,0) → l1-r1; (2,0) sağ tekrar → atlanır; (9,1) geçersiz

        var plan = ActivityDrafts.Map(draft, Frame).Plan;

        plan["left"]!.AsArray().Select(l => ((string)l!["id"]!, (string)l["label"]!)).ShouldBe([("l1", "Ankara"), ("l2", "İzmir")]);
        plan["right"]!.AsArray().Select(r => (string)r!["id"]!).ShouldBe(["r1", "r2", "r3"]);
        plan["prelinked"]!.AsArray().Select(p => ((string)p!["leftId"]!, (string)p["rightId"]!)).ShouldBe([("l1", "r1")]);
        Valid(MatchGrader.Type, plan).ShouldBeTrue();
    }

    [Fact]
    public void Map_Match_AllLeftPrelinked_IsInvalidAndExplained()
    {
        var plan = ActivityDrafts.Map(new ActivityDrafts.MatchDraft("", [new("a", 0, 0, 0, 0)], [new("b", 0, 0, 0, 0)], [new(0, 0)]),
            Frame).Plan;

        Valid(MatchGrader.Type, plan).ShouldBeFalse();
        ActivityPlanDiagnostics.Explain(MatchGrader.Type, plan.ToJsonString())
            .ShouldBe(["labeling.activity.invalid.matchAllPrelinked"]);
    }

    [Fact]
    public void Map_LongLabels_AreTruncatedToGraderLimit_AndNoneIsTreatedAsEmpty()
    {
        var plan = ActivityDrafts.Map(new ActivityDrafts.MatrixDraft("none", false,
            [new(new string('x', 500)), new("none")], [new("r", 10, 10, 90, 20, [])]), Frame);

        plan.Instruction.ShouldBeNull();
        var columns = plan.Plan["columns"]!.AsArray();
        ((string)columns[0]!["label"]!).Length.ShouldBe(MatrixChoiceGrader.MaxLabelLength);
        columns[1]!.AsObject().ContainsKey("label").ShouldBeFalse();
    }

    [Fact]
    public void Map_NullListsFromModel_ProduceEmptyPlans()
    {
        var plan = ActivityDrafts.Map(new ActivityDrafts.DragDraft(null!, null!, false, null!, null!), Frame).Plan;

        plan["targets"]!.AsArray().ShouldBeEmpty();
        ActivityPlanDiagnostics.Explain(DragPlaceGrader.Type, plan.ToJsonString())
            .ShouldBe(["labeling.activity.invalid.noTargets", "labeling.activity.invalid.noCards"]);
    }

    [Fact]
    public void Explain_GraderRejectsForAnotherReason_ReturnsGenericKey()
    {
        ActivityPlanDiagnostics.Explain(MatchGrader.Type, "{\"left\":[{\"id\":\"a\"}],\"right\":[{\"id\":\"b\"}]}")
            .ShouldBe([ActivityPlanDiagnostics.Generic]);
        ActivityPlanDiagnostics.Explain(MatchGrader.Type, "not json").ShouldBe([ActivityPlanDiagnostics.Generic]);
    }

    // ---------- Parmak izi ----------

    [Fact]
    public void Fingerprint_DependsOnTypeInstructionAndRevisions()
    {
        var matrix = ActivityStructureSettings.Default(MatrixChoiceGrader.Type);

        matrix.Fingerprint.ShouldBe(ActivityStructureSettings.Default(MatrixChoiceGrader.Type).Fingerprint);
        (matrix with { Version = 7 }).Fingerprint.ShouldBe(matrix.Fingerprint);                // sürüm numarası anahtarda değil
        (matrix with { Instruction = matrix.Instruction + " " }).Fingerprint.ShouldNotBe(matrix.Fingerprint);
        (matrix with { InteractionType = SlotFillGrader.Type }).Fingerprint.ShouldNotBe(matrix.Fingerprint);
        ActivityStructureSettings.ComputeFingerprint(MatrixChoiceGrader.Type, matrix.Instruction,
            ActivityStructureSettings.SchemaRevision + 1, 1).ShouldNotBe(matrix.Fingerprint);
        ActivityStructureSettings.ComputeFingerprint(MatrixChoiceGrader.Type, matrix.Instruction,
            ActivityStructureSettings.SchemaRevision, 2).ShouldNotBe(matrix.Fingerprint);
    }

    [Theory]
    [InlineData("matrixChoice", "matrixChoice")]
    [InlineData("SLOTFILL", "slotFill")]
    [InlineData(" dragPlace ", "dragPlace")]
    [InlineData("match", "match")]
    [InlineData("mcq", null)]
    [InlineData("dragDropLabeling", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Canonical_AcceptsOnlyTheFourPilotTypes(string? input, string? expected) =>
        ActivityStructureSettings.Canonical(input).ShouldBe(expected);

    [Fact]
    public void DefaultInstructions_AreDistinctPerType_AndUsePlaceholders()
    {
        var instructions = ActivityStructureSettings.Types.Select(ActivityStructureSettings.DefaultInstruction).ToList();

        instructions.Distinct().Count().ShouldBe(4);
        instructions.ShouldAllBe(i => i.Contains("{width}") && i.Contains("{height}")
                                      && i.Length <= ActivityStructureSettings.MaxInstructionLength);
    }
}
