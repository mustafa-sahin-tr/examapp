using System.Text.Json;
using System.Text.Json.Nodes;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #576: <see cref="DragPlaceGrader"/> — payload doğrulama (katı: id, kapasite, tekrar kullanım, yinelenen çift),
/// normalize, yerleşim başına puanlama (beklenen çift, eksik kart hedef başına tek satır, fazladan yanlış yerleşim), kova,
/// çeldirici, sıralama modu, çözüm sızıntısı yok, çözümsüz soru puanlanamaz ve kayıtta tanım doğrulaması.
/// </summary>
public class DragPlaceGraderTests
{
    // İki kova (t1 kapasite 3, t2 kapasite 2) + boş kalması gereken kova t3; c5 çeldirici. t1 görsel üstü bölge, t2/t3 kutusuz.
    private const string Plan =
        """{"version":1,"type":"dragPlace","mode":"place","reuse":false,"targets":[{"id":"t1","label":"Erime","box":{"x":0,"y":0,"width":10,"height":5},"capacity":3},{"id":"t2","label":"Donma","capacity":2},{"id":"t3","label":"Boş","capacity":2}],"cards":[{"id":"c1","label":"Buz eriyor"},{"id":"c2","label":"Tereyağı eriyor"},{"id":"c3","box":{"x":0,"y":10,"width":5,"height":5}},{"id":"c4","label":"Su donuyor"},{"id":"c5","label":"Çeldirici"}]}""";

    private const string Solution = """{"t1":["c1","c2"],"t2":["c3","c4"],"t3":[]}""";

    // Sıralama: üç konum, üç kart.
    private const string OrderPlan =
        """{"version":1,"type":"dragPlace","mode":"order","targets":[{"id":"p1","capacity":1},{"id":"p2","capacity":1},{"id":"p3","capacity":1}],"cards":[{"id":"a","label":"Tohum"},{"id":"b","label":"Fide"},{"id":"c","label":"Ağaç"}]}""";

    private const string OrderSolution = """{"p1":["a"],"p2":["b"],"p3":["c"]}""";

    // Tekrar kullanım: aynı kart iki hedefe.
    private const string ReusePlan =
        """{"version":1,"type":"dragPlace","reuse":true,"targets":[{"id":"t1","capacity":2},{"id":"t2","capacity":2}],"cards":[{"id":"x","label":"+"},{"id":"y","label":"-"}]}""";

    private const string ReuseSolution = """{"t1":["x"],"t2":["x","y"]}""";

    private static readonly DragPlaceGrader Grader = new();

    private static string Payload(params (string T, string C)[] placements) => JsonSerializer.Serialize(new
    {
        version = 1, type = "dragPlace",
        placements = placements.Select(p => new { targetId = p.T, cardId = p.C }),
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

    // ---------------------------------------------------------------- Grade

    [Fact]
    public void Grade_AllPlaced_IsCorrectWithRowPerExpectedPairInSolutionOrder()
    {
        var result = Grader.Grade(Input(Payload(("t2", "c4"), ("t1", "c2"), ("t2", "c3"), ("t1", "c1"))));

        result.IsCorrect.ShouldBeTrue();
        result.CorrectCount.ShouldBe(4);
        result.TotalCount.ShouldBe(4);
        Rows(result.Feedback).ShouldBe(new[] { ("t1:c1", true), ("t1:c2", true), ("t2:c3", true), ("t2:c4", true) });
    }

    [Fact]
    public void Grade_WrongCard_MarksPlacedPairWrongAndTargetIncomplete()
    {
        // c4 (donma) erime kovasına: t1:c4 yanlış (UI kartı işaretler); t2 eksik.
        var result = Grader.Grade(Input(Payload(("t1", "c1"), ("t1", "c2"), ("t1", "c4"), ("t2", "c3"))));

        result.IsCorrect.ShouldBeFalse();
        result.CorrectCount.ShouldBe(3);
        result.TotalCount.ShouldBe(4);
        Rows(result.Feedback).ShouldBe(new[] { ("t1:c1", true), ("t1:c2", true), ("t2:c3", true), ("t2:", false), ("t1:c4", false) });
    }

    [Fact]
    public void Grade_DistractorPlaced_MakesAnswerWrongWithoutChangingTotal()
    {
        var result = Grader.Grade(Input(Payload(("t1", "c1"), ("t1", "c2"), ("t2", "c3"), ("t2", "c4"), ("t3", "c5"))));

        result.IsCorrect.ShouldBeFalse();
        result.CorrectCount.ShouldBe(4);
        result.TotalCount.ShouldBe(4);
        Rows(result.Feedback).Last().ShouldBe(("t3:c5", false));
    }

    [Fact]
    public void Grade_EmptyByDesignTargetTouched_OnlyTheWrongPlacementRow()
    {
        var result = Grader.Grade(Input(Payload(("t3", "c5"))));

        Rows(result.Feedback).ShouldBe(new[] { ("t3:c5", false) });
    }

    [Fact]
    public void Grade_EmptyBucketLeftEmpty_AddsNoRow()
    {
        var result = Grader.Grade(Input(Payload(("t1", "c1"))));

        Rows(result.Feedback).Select(r => r.Id).ShouldNotContain(id => id.StartsWith("t3"));
    }

    [Fact]
    public void Grade_NoPayload_NoRowsButExpectedPairsStillCount()
    {
        var result = Grader.Grade(Input(null));

        result.IsCorrect.ShouldBeFalse();
        result.CorrectCount.ShouldBe(0);
        result.TotalCount.ShouldBe(4);
        Rows(result.Feedback).ShouldBeEmpty();
    }

    [Fact]
    public void Grade_IncompleteRow_OnlyForTouchedTargets()
    {
        // t1 dokunuldu (c1), c2 eksik → "t1:"; t2 (beklenen) dokunulmadı → satır yok; t3 (boş olmalı) dokunulmadı → satır yok.
        var result = Grader.Grade(Input(Payload(("t1", "c1"))));

        Rows(result.Feedback).ShouldBe(new[] { ("t1:c1", true), ("t1:", false) });
        result.TotalCount.ShouldBe(4);
    }

    [Fact]
    public void Grade_UntouchedExpectedAndEmptyByDesignTargets_AreIndistinguishable()
    {
        // t2 (beklenen kartları var) ve t3 (boş olmalı) ikisi de dokunulmamış: geri bildirimde ikisinden de hiçbir iz yok.
        var result = Grader.Grade(Input(Payload(("t1", "c1"), ("t1", "c2"))));

        var ids = Rows(result.Feedback).Select(r => r.Id).ToList();
        ids.ShouldBe(new[] { "t1:c1", "t1:c2" });
        ids.ShouldNotContain(id => id.StartsWith("t2") || id.StartsWith("t3"));
    }

    [Fact]
    public void Grade_FeedbackNeverRevealsMissingExpectedCards()
    {
        // Yalnız c1 yerleştirildi; c2/c3/c4 (beklenen ama yerleştirilmemiş) geri bildirimde geçmez.
        var result = Grader.Grade(Input(Payload(("t1", "c1"))));

        result.Feedback!.ShouldNotContain("c2");
        result.Feedback!.ShouldNotContain("c3");
        result.Feedback!.ShouldNotContain("c4");
        result.Feedback!.ShouldNotContain("Donma");
        using var doc = JsonDocument.Parse(result.Feedback!);
        doc.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(new[] { "rows" });
        foreach (var row in doc.RootElement.GetProperty("rows").EnumerateArray())
            row.EnumerateObject().Select(p => p.Name).ShouldBe(new[] { "id", "correct" });
    }

    [Fact]
    public void Grade_OrderMode_PositionsAreTargets()
    {
        var right = Grader.Grade(Input(Payload(("p1", "a"), ("p2", "b"), ("p3", "c")), OrderSolution, OrderPlan));
        right.IsCorrect.ShouldBeTrue();
        right.CorrectCount.ShouldBe(3);

        var swapped = Grader.Grade(Input(Payload(("p1", "b"), ("p2", "a"), ("p3", "c")), OrderSolution, OrderPlan));
        swapped.IsCorrect.ShouldBeFalse();
        swapped.CorrectCount.ShouldBe(1);
        Rows(swapped.Feedback).ShouldBe(new[] { ("p1:", false), ("p2:", false), ("p3:c", true), ("p1:b", false), ("p2:a", false) });
    }

    [Fact]
    public void Grade_Reuse_SameCardInSeveralTargets()
    {
        var result = Grader.Grade(Input(Payload(("t1", "x"), ("t2", "x"), ("t2", "y")), ReuseSolution, ReusePlan));

        result.IsCorrect.ShouldBeTrue();
        result.CorrectCount.ShouldBe(3);
        result.TotalCount.ShouldBe(3);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("{broken")]
    [InlineData("""{"t1":[],"t2":[]}""")]
    [InlineData("""{"t9":["c1"]}""")]
    public void Grade_NoUsableSolution_IsUngradable(string? solution)
    {
        Grader.Grade(Input(Payload(("t1", "c1")), solution)).ShouldBe(InteractionGradeResult.Ungradable);
    }

    [Fact]
    public void Grade_UnreadablePlan_IsUngradableAndDoesNotThrow()
    {
        Grader.Grade(Input(Payload(("t1", "c1")), Solution, plan: "{broken")).ShouldBe(InteractionGradeResult.Ungradable);
        Grader.Grade(Input(Payload(("t1", "c1")), Solution, plan: null)).ShouldBe(InteractionGradeResult.Ungradable);
    }

    [Fact]
    public void Grade_MalformedPayload_TreatedAsBlank()
    {
        var result = Grader.Grade(Input("{broken"));

        result.CorrectCount.ShouldBe(0);
        result.TotalCount.ShouldBe(4);
    }

    // ---------------------------------------------------------------- Validate / Normalize

    [Fact]
    public void Validate_NullOrEmptyPlacements_IsValid()
    {
        Grader.Validate(Plan, null).ShouldBeTrue();
        Grader.Validate(Plan, Payload()).ShouldBeTrue();
        Grader.Validate(Plan, Payload(("t1", "c1"), ("t1", "c5"), ("t2", "c3"))).ShouldBeTrue();
    }

    [Theory]
    [InlineData("""{"version":1,"type":"dragPlace","placements":[{"targetId":"t9","cardId":"c1"}]}""")]            // plan dışı hedef
    [InlineData("""{"version":1,"type":"dragPlace","placements":[{"targetId":"t1","cardId":"c9"}]}""")]            // plan dışı kart
    [InlineData("""{"version":1,"type":"dragPlace","placements":[{"targetId":"t1","cardId":"c1"},{"targetId":"t2","cardId":"c1"}]}""")] // tekrar kullanım kapalı
    [InlineData("""{"version":1,"type":"dragPlace","placements":[{"targetId":"t2","cardId":"c1"},{"targetId":"t2","cardId":"c2"},{"targetId":"t2","cardId":"c3"}]}""")] // kapasite 2 aşıldı
    [InlineData("""{"version":1,"type":"dragPlace","placements":[{"targetId":"t1","cardId":"c1","x":1}]}""")]      // bilinmeyen alan
    [InlineData("""{"version":1,"type":"dragPlace","placements":[{"targetId":"t1","cardId":1}]}""")]               // string değil
    [InlineData("""{"version":1,"type":"dragPlace","placements":[{"targetId":"__proto__","cardId":"c1"}]}""")]      // ayrılmış id
    [InlineData("""{"version":1,"type":"dragPlace","placements":{}}""")]                                           // dizi değil
    [InlineData("""{"version":2,"type":"dragPlace","placements":[]}""")]                                           // sürüm
    [InlineData("""{"version":1,"type":"dragDropLabeling","placements":[]}""")]                                    // başka tip
    [InlineData("""{"version":1,"type":"dragPlace","type":"dragPlace","placements":[]}""")]                        // yinelenen anahtar
    [InlineData("{broken")]
    public void Validate_InvalidPayload_IsRejected(string payload)
    {
        Grader.Validate(Plan, payload).ShouldBeFalse();
    }

    [Fact]
    public void Validate_DuplicatePairEvenWithReuse_IsRejected()
    {
        Grader.Validate(ReusePlan, Payload(("t1", "x"), ("t2", "x"))).ShouldBeTrue();
        Grader.Validate(ReusePlan, Payload(("t1", "x"), ("t1", "x"))).ShouldBeFalse();
    }

    [Fact]
    public void Validate_OversizedPayload_IsRejected()
    {
        var huge = """{"version":1,"type":"dragPlace","placements":[],"pad":""" + "\"" + new string('a', InteractionPayload.MaxLength) + "\"}";
        Grader.Validate(Plan, huge).ShouldBeFalse();
    }

    [Fact]
    public void Validate_PlacementsWithUnreadablePlan_IsRejected()
    {
        Grader.Validate("{broken", Payload(("t1", "c1"))).ShouldBeFalse();
    }

    [Fact]
    public void Normalize_EmptyPlacements_IsNull()
    {
        Grader.Normalize(Payload()).ShouldBeNull();
        Grader.Normalize(null).ShouldBeNull();
        var payload = Payload(("t1", "c1"));
        Grader.Normalize(payload).ShouldBe(payload);
    }

    // ---------------------------------------------------------------- ValidateDefinition

    [Fact]
    public void ValidateDefinition_ValidPlansAndSolutions_Pass()
    {
        Grader.ValidateDefinition(Plan, Solution).ShouldBeTrue();
        Grader.ValidateDefinition(Plan, null).ShouldBeTrue();
        Grader.ValidateDefinition(OrderPlan, OrderSolution).ShouldBeTrue();
        Grader.ValidateDefinition(ReusePlan, ReuseSolution).ShouldBeTrue();
        // mode / reuse / capacity yoksa varsayılanlar (place, false, 1).
        Grader.ValidateDefinition(
            """{"version":1,"type":"dragPlace","targets":[{"id":"t1"}],"cards":[{"id":"c1","label":"a"}]}""",
            """{"t1":["c1"]}""").ShouldBeTrue();
    }

    [Theory]
    [InlineData("""{"version":1,"type":"dragPlace","targets":[{"id":"t1"}],"cards":[{"id":"c1","label":"a"}],"solution":{"t1":["c1"]}}""")] // kök gizli alan
    [InlineData("""{"version":1,"type":"dragPlace","targets":[{"id":"t1","answer":"c1"}],"cards":[{"id":"c1","label":"a"}]}""")]           // hedefte gizli alan
    [InlineData("""{"version":1,"type":"dragPlace","targets":[{"id":"t1"}],"cards":[{"id":"c1","label":"a","correct":true}]}""")]          // kartta gizli alan
    [InlineData("""{"version":1,"type":"dragPlace","targets":[{"id":"t1","box":{"x":0,"y":0,"width":1,"height":1,"z":1}}],"cards":[{"id":"c1","label":"a"}]}""")] // kutuda gizli alan
    [InlineData("""{"version":1,"type":"dragPlace","targets":[{"id":"t1"}],"cards":[{"id":"c1"}]}""")]                                      // etiketsiz kutusuz kart
    [InlineData("""{"version":1,"type":"dragPlace","targets":[{"id":"t1"}],"cards":[{"id":"c1","label":"  "}]}""")]                        // boş etiket
    [InlineData("""{"version":1,"type":"dragPlace","targets":[{"id":"t1"}],"cards":[{"id":"c1","box":{"x":0,"y":0,"width":0,"height":1}}]}""")] // geçersiz kutu
    [InlineData("""{"version":1,"type":"dragPlace","targets":[],"cards":[{"id":"c1","label":"a"}]}""")]                                     // hedefsiz
    [InlineData("""{"version":1,"type":"dragPlace","targets":[{"id":"t1"}],"cards":[]}""")]                                                 // kartsız
    [InlineData("""{"version":1,"type":"dragPlace","targets":[{"id":"t1","capacity":0}],"cards":[{"id":"c1","label":"a"}]}""")]            // kapasite 0
    [InlineData("""{"version":1,"type":"dragPlace","targets":[{"id":"t1","capacity":31}],"cards":[{"id":"c1","label":"a"}]}""")]           // kapasite > 30
    [InlineData("""{"version":1,"type":"dragPlace","targets":[{"id":"t1","capacity":1.5}],"cards":[{"id":"c1","label":"a"}]}""")]          // tam sayı değil
    [InlineData("""{"version":1,"type":"dragPlace","mode":"order","targets":[{"id":"t1","capacity":2}],"cards":[{"id":"c1","label":"a"}]}""")] // sıralamada kapasite 1
    [InlineData("""{"version":1,"type":"dragPlace","mode":"sort","targets":[{"id":"t1"}],"cards":[{"id":"c1","label":"a"}]}""")]           // bilinmeyen mod
    [InlineData("""{"version":1,"type":"dragPlace","reuse":"yes","targets":[{"id":"t1"}],"cards":[{"id":"c1","label":"a"}]}""")]           // reuse bool değil
    [InlineData("""{"version":1,"type":"dragPlace","targets":[{"id":"t1"},{"id":"t1"}],"cards":[{"id":"c1","label":"a"}]}""")]             // yinelenen hedef
    [InlineData("""{"version":1,"type":"dragPlace","targets":[{"id":"t1"}],"cards":[{"id":"c1","label":"a"},{"id":"c1","label":"b"}]}""")] // yinelenen kart
    [InlineData("""{"version":1,"type":"dragPlace","targets":[{"id":"x1"}],"cards":[{"id":"x1","label":"a"}]}""")]                         // hedef/kart id çakışması
    [InlineData("""{"version":1,"type":"dragPlace","targets":[{"id":"constructor"}],"cards":[{"id":"c1","label":"a"}]}""")]                // ayrılmış id
    [InlineData("""{"version":1,"type":"dragPlace","targets":[{"id":"t:1"}],"cards":[{"id":"c1","label":"a"}]}""")]                        // id deseni
    [InlineData("""{"version":1,"type":"dragDropLabeling","targets":[{"id":"t1"}],"cards":[{"id":"c1","label":"a"}]}""")]                  // tip
    [InlineData("""{"version":2,"type":"dragPlace","targets":[{"id":"t1"}],"cards":[{"id":"c1","label":"a"}]}""")]                         // sürüm
    [InlineData(null)]
    [InlineData("{broken")]
    public void ValidateDefinition_InvalidPlan_IsRejected(string? plan)
    {
        Grader.ValidateDefinition(plan, null).ShouldBeFalse();
    }

    [Fact]
    public void ValidateDefinition_LabelTooLong_IsRejected()
    {
        var plan = """{"version":1,"type":"dragPlace","targets":[{"id":"t1"}],"cards":[{"id":"c1","label":"LBL"}]}""";
        Grader.ValidateDefinition(plan.Replace("LBL", new string('a', 200)), null).ShouldBeTrue();
        Grader.ValidateDefinition(plan.Replace("LBL", new string('a', 201)), null).ShouldBeFalse();
    }

    [Fact]
    public void ValidateDefinition_Limits_60CardsAnd30Targets()
    {
        static string PlanOf(int targets, int cards) =>
            "{\"version\":1,\"type\":\"dragPlace\",\"targets\":["
            + string.Join(",", Enumerable.Range(1, targets).Select(i => "{\"id\":\"t" + i + "\"}"))
            + "],\"cards\":[" + string.Join(",", Enumerable.Range(1, cards).Select(i => "{\"id\":\"c" + i + "\",\"label\":\"k\"}")) + "]}";

        Grader.ValidateDefinition(PlanOf(30, 60), null).ShouldBeTrue();
        Grader.ValidateDefinition(PlanOf(31, 60), null).ShouldBeFalse();
        Grader.ValidateDefinition(PlanOf(30, 61), null).ShouldBeFalse();
    }

    [Theory]
    [InlineData("""{"t1":["c1","c2"],"t2":["c3","c4"]}""")]                 // t3 eksik
    [InlineData("""{"t1":["c1","c2"],"t2":["c3","c4"],"t3":[],"t9":[]}""")] // plan dışı hedef
    [InlineData("""{"t1":["c1","c9"],"t2":["c3","c4"],"t3":[]}""")]         // plan dışı kart
    [InlineData("""{"t1":["c1","c1"],"t2":["c3","c4"],"t3":[]}""")]         // yinelenen kart
    [InlineData("""{"t1":["c1","c2"],"t2":["c1","c4"],"t3":[]}""")]         // tekrar kullanım kapalı
    [InlineData("""{"t1":["c1","c2"],"t2":["c3","c4","c5"],"t3":[]}""")]    // kapasite 2 aşıldı
    [InlineData("""{"t1":[],"t2":[],"t3":[]}""")]                           // hiç yerleşim yok
    [InlineData("""{"t1":"c1","t2":["c3","c4"],"t3":[]}""")]                // dizi değil
    [InlineData("""{"t1":[1],"t2":["c3","c4"],"t3":[]}""")]                 // string değil
    [InlineData("""{"t1":["c1"],"t1":["c2"],"t2":["c3"],"t3":[]}""")]       // yinelenen anahtar
    [InlineData("""["c1"]""")]
    public void ValidateDefinition_InconsistentSolution_IsRejected(string solution)
    {
        Grader.ValidateDefinition(Plan, solution).ShouldBeFalse();
    }

    [Fact]
    public void Registry_ResolvesDragPlaceCaseInsensitivelyAndCanonicalizes()
    {
        var registry = InteractionGraderRegistry.Default;
        registry.Resolve("DragPlace").ShouldBeOfType<DragPlaceGrader>();
        registry.CanonicalType("DRAGPLACE").ShouldBe(DragPlaceGrader.Type);
        registry.Resolve("dragDropLabeling").ShouldBeOfType<DragDropLabelingGrader>();
    }

    // ---------------------------------------------------------------- review #576: kutu sınırları, sıralama kuralları

    [Theory]
    [InlineData("""{"x":-1,"y":0,"width":5,"height":5}""")]        // negatif x
    [InlineData("""{"x":0,"y":-0.5,"width":5,"height":5}""")]      // negatif y
    [InlineData("""{"x":0,"y":0,"width":0.5,"height":5}""")]       // genişlik < 1
    [InlineData("""{"x":0,"y":0,"width":5,"height":0.9}""")]       // yükseklik < 1
    [InlineData("""{"x":99999,"y":0,"width":2,"height":5}""")]     // x + w > 100000
    [InlineData("""{"x":0,"y":99999.5,"width":5,"height":1}""")]   // y + h > 100000
    [InlineData("""{"x":0,"y":0,"width":1e400,"height":5}""")]     // sonsuz
    public void ValidateDefinition_BoxOutOfBounds_IsRejected(string box)
    {
        Grader.ValidateDefinition(
            """{"version":1,"type":"dragPlace","targets":[{"id":"t1"}],"cards":[{"id":"c1","box":BOX}]}""".Replace("BOX", box), null)
            .ShouldBeFalse();
        Grader.ValidateDefinition(
            """{"version":1,"type":"dragPlace","targets":[{"id":"t1","box":BOX}],"cards":[{"id":"c1","label":"a"}]}""".Replace("BOX", box), null)
            .ShouldBeFalse();
    }

    [Fact]
    public void ValidateDefinition_BoxAtBounds_Passes()
    {
        Grader.ValidateDefinition(
            """{"version":1,"type":"dragPlace","targets":[{"id":"t1","box":{"x":0,"y":0,"width":1,"height":1}}],"cards":[{"id":"c1","box":{"x":99000,"y":99000,"width":1000,"height":1000}}]}""",
            null).ShouldBeTrue();
    }

    [Theory]
    [InlineData("""{"p1":["a"],"p2":["b"],"p3":[]}""")]       // boş konum
    [InlineData("""{"p1":["a"],"p2":["b"]}""")]               // konum eksik
    [InlineData("""{"p1":["a"],"p2":["a"],"p3":["c"]}""")]    // kart iki konumda
    public void ValidateDefinition_OrderMode_EveryPositionExactlyOneDistinctCard(string solution)
    {
        Grader.ValidateDefinition(OrderPlan, solution).ShouldBeFalse();
    }

    [Fact]
    public void ValidateDefinition_OrderModeWithReuse_IsRejected()
    {
        Grader.ValidateDefinition(OrderPlan.Replace("\"mode\":\"order\"", "\"mode\":\"order\",\"reuse\":true"), null).ShouldBeFalse();
        Grader.ValidateDefinition(OrderPlan.Replace("\"mode\":\"order\"", "\"mode\":\"order\",\"reuse\":false"), OrderSolution).ShouldBeTrue();
    }

    // ---------------------------------------------------------------- review #576 (H1/M1): PrepareForStorage

    private static readonly string[] Changes = { "Erime", "Donma", "Buharlaşma", "Yoğuşma", "Süblimleşme", "Kırağılaşma" };

    // 12 kart yazarlık sırasında = çözüm sırasında (c1, c2 → t1; c3, c4 → t2; ...): sıra ve id çözümü ele veriyor.
    private static readonly string BigPlan =
        "{\"version\":1,\"type\":\"dragPlace\",\"targets\":["
        + string.Join(",", Changes.Select((c, i) => "{\"id\":\"t" + (i + 1) + "\",\"label\":\"" + c + "\",\"capacity\":2}"))
        + "],\"cards\":[" + string.Join(",", Enumerable.Range(1, 12).Select(i => "{\"id\":\"c" + i + "\",\"label\":\"Olay " + i + " ğüşıöç\"}")) + "]}";

    private static readonly string BigSolution =
        "{" + string.Join(",", Enumerable.Range(1, 6).Select(t => "\"t" + t + "\":[\"c" + (2 * t - 1) + "\",\"c" + (2 * t) + "\"]")) + "}";

    private static (List<string> Ids, List<string> Labels) Cards(string plan)
    {
        var cards = JsonNode.Parse(plan)!["cards"]!.AsArray();
        return (cards.Select(c => c!["id"]!.GetValue<string>()).ToList(), cards.Select(c => c!["label"]!.GetValue<string>()).ToList());
    }

    [Fact]
    public void PrepareForStorage_ShufflesCardsAndReplacesIdsWithOpaqueOnes()
    {
        var grader = new DragPlaceGrader(new SeededInteractionRandom(576));

        var (plan, solution) = grader.PrepareForStorage(BigPlan, BigSolution);

        plan.ShouldNotBeNull();
        solution.ShouldNotBeNull();
        var (ids, labels) = Cards(plan!);
        labels.ShouldNotBe(Enumerable.Range(1, 12).Select(i => "Olay " + i + " ğüşıöç").ToList());   // sıra değişti
        labels.OrderBy(l => l, StringComparer.Ordinal).ShouldBe(Cards(BigPlan).Labels.OrderBy(l => l, StringComparer.Ordinal)); // aynı kartlar
        ids.ShouldAllBe(id => System.Text.RegularExpressions.Regex.IsMatch(id, "^[a-z0-9]{8}$"));
        ids.Distinct().Count().ShouldBe(12);
        ids.ShouldNotContain(id => id.StartsWith('t') && id.Length < 3);
        // Hedefler aynen, yazarlık sırasında.
        JsonNode.Parse(plan!)!["targets"]!.ToJsonString().ShouldBe(JsonNode.Parse(BigPlan)!["targets"]!.ToJsonString());
        // Türkçe etiketler kaçışsız; eski id'ler yok.
        plan!.ShouldContain("ğüşıöç");
        foreach (var old in Enumerable.Range(1, 12).Select(i => "\"c" + i + "\""))
        {
            plan.ShouldNotContain(old);
            solution!.ShouldNotContain(old);
        }
        grader.ValidateDefinition(plan, solution).ShouldBeTrue();
    }

    [Fact]
    public void PrepareForStorage_SolutionStillGradesTheSameAnswerIdentically()
    {
        var grader = new DragPlaceGrader(new SeededInteractionRandom(11));
        var (plan, solution) = grader.PrepareForStorage(BigPlan, BigSolution);

        // Yazarlık id'si → saklanan id (etiket üzerinden).
        var (oldIds, oldLabels) = Cards(BigPlan);
        var (newIds, newLabels) = Cards(plan!);
        var map = oldIds.ToDictionary(o => o, o => newIds[newLabels.IndexOf(oldLabels[oldIds.IndexOf(o)])]);

        var authored = new[] { ("t1", "c1"), ("t1", "c4"), ("t2", "c3"), ("t6", "c12") };
        var before = Grader.Grade(Input(Payload(authored), BigSolution, BigPlan));
        var after = grader.Grade(Input(Payload(authored.Select(p => (p.Item1, map[p.Item2])).ToArray()), solution, plan));

        (after.IsCorrect, after.CorrectCount, after.TotalCount).ShouldBe((before.IsCorrect, before.CorrectCount, before.TotalCount));
        Rows(after.Feedback).Select(r => r.Correct).ShouldBe(Rows(before.Feedback).Select(r => r.Correct));
        var full = Payload(Enumerable.Range(1, 12).Select(i => ("t" + ((i + 1) / 2), map["c" + i])).ToArray());
        grader.Grade(Input(full, solution, plan)).IsCorrect.ShouldBeTrue();
    }

    [Fact]
    public void PrepareForStorage_IsDeterministicPerSeedAndVariesAcrossSeeds()
    {
        var a = new DragPlaceGrader(new SeededInteractionRandom(1)).PrepareForStorage(BigPlan, BigSolution);
        var b = new DragPlaceGrader(new SeededInteractionRandom(1)).PrepareForStorage(BigPlan, BigSolution);
        var c = new DragPlaceGrader(new SeededInteractionRandom(2)).PrepareForStorage(BigPlan, BigSolution);

        a.ShouldBe(b);
        Cards(a.Plan!).Labels.ShouldNotBe(Cards(c.Plan!).Labels);
    }

    [Fact]
    public void PrepareForStorage_CryptoDefault_OrderDiffersFromAuthoringAcrossRuns()
    {
        // 12! permütasyon: 20 denemenin hepsinin yazarlık sırasında kalma olasılığı ihmal edilebilir.
        var authored = Cards(BigPlan).Labels;
        Enumerable.Range(0, 20).Select(_ => Cards(Grader.PrepareForStorage(BigPlan, BigSolution).Plan!).Labels)
            .Count(l => l.SequenceEqual(authored)).ShouldBeLessThan(2);
    }

    [Fact]
    public void PrepareForStorage_WithoutSolution_ScramblesPlanOnly()
    {
        var (plan, solution) = new DragPlaceGrader(new SeededInteractionRandom(3)).PrepareForStorage(Plan, null);

        solution.ShouldBeNull();
        Grader.ValidateDefinition(plan, null).ShouldBeTrue();
        JsonNode.Parse(plan!)!["cards"]!.AsArray().Select(c => c!["id"]!.GetValue<string>()).ShouldNotContain("c1");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{broken")]
    public void PrepareForStorage_UnreadablePlan_ReturnedAsIs(string? plan)
    {
        Grader.PrepareForStorage(plan, Solution).ShouldBe((plan, Solution));
    }

    [Fact]
    public void PrepareForStorage_OtherTypes_AreUntouched()
    {
        const string matrixPlan = """{"version":1,"type":"matrixChoice","columns":[{"id":"a"},{"id":"b"}],"rows":[{"id":"r1","box":{"x":0,"y":0,"width":1,"height":1}}]}""";
        const string matrixSolution = """{"r1":["a"]}""";
        const string slotPlan = """{"version":1,"type":"slotFill","slots":[{"id":"s1","box":{"x":0,"y":0,"width":1,"height":1},"input":{"kind":"text"}}]}""";
        const string slotSolution = """{"s1":{"accept":["x"]}}""";
        var registry = InteractionGraderRegistry.Default;

        registry.Resolve("matrixChoice").PrepareForStorage(matrixPlan, matrixSolution).ShouldBe((matrixPlan, matrixSolution));
        registry.Resolve("slotFill").PrepareForStorage(slotPlan, slotSolution).ShouldBe((slotPlan, slotSolution));
        registry.Resolve("dragDropLabeling").PrepareForStorage("{\"a\":1}", "{\"b\":2}").ShouldBe(("{\"a\":1}", "{\"b\":2}"));
        registry.Resolve(null).PrepareForStorage("{}", null).ShouldBe(("{}", (string?)null));
    }
}
