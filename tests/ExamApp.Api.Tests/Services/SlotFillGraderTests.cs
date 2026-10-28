using System.Text.Json;
using ExamApp.Api.Services.Interactions;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #575: <see cref="SlotFillGrader"/> — payload doğrulama (katı), normalize (hepsi boş → null), kutu bazlı puanlama
/// (plan sırası, tüm kutular, cevapsız kutu yanlış), geri bildirimde çözüm sızıntısı yok, çözümsüz soru puanlanamaz ve kayıtta
/// tanım doğrulaması. Karşılaştırma kurallarının ayrıntısı paylaşılan fixture'da (<see cref="SlotNormalizationFixtureTests"/>).
/// </summary>
public class SlotFillGraderTests
{
    private const string Box = "\"box\":{\"x\":0,\"y\":0,\"width\":10,\"height\":3}";

    // s1 sayı (kesir), s2 metin, s3 seçim, s4 palet.
    private static readonly string Plan =
        "{\"version\":1,\"type\":\"slotFill\",\"slots\":["
        + "{\"id\":\"s1\",\"label\":\"Toplam\"," + Box + ",\"input\":{\"kind\":\"number\",\"entry\":\"fraction\",\"unit\":\"cm\"}},"
        + "{\"id\":\"s2\"," + Box + ",\"input\":{\"kind\":\"text\"}},"
        + "{\"id\":\"s3\"," + Box + ",\"input\":{\"kind\":\"select\",\"options\":[{\"id\":\"OPT-UP-1\",\"label\":\"artar\"},{\"id\":\"OPT-DOWN-2\",\"label\":\"azalır\"},{\"id\":\"OPT-SAME-3\",\"label\":\"değişmez\"}]}},"
        + "{\"id\":\"s4\"," + Box + ",\"input\":{\"kind\":\"palette\",\"symbols\":[\"∠\",\"⊥\",\"//\",\"[AB]\"]}}"
        + "]}";

    // Çözüm değerleri bilinçli olarak ayırt edilebilir: geri bildirimde hiçbiri görünmemeli.
    private const string Solution =
        """{"s1":{"value":"3/4","equivalence":true,"requireSimplest":false,"units":["cm","santimetre"],"unitMode":"optional"},"s2":{"accept":["Kızılırmak"],"optionalSuffixes":["Nehri"]},"s3":{"optionIds":["OPT-UP-1"]},"s4":{"accept":["∠ABC","∠CBA"]}}""";

    private static readonly SlotFillGrader Grader = new();

    private static string Payload(string values) => "{\"version\":1,\"type\":\"slotFill\",\"values\":" + values + "}";

    private static InteractionGradeInput Input(string? payload, string? solution = Solution, string? plan = null)
        => new(plan ?? Plan, solution, payload, SelectedAnswerId: null, CorrectAnswerId: null);

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
    public void Grade_AllSlotsCorrect_InPlanOrderRegardlessOfPayloadOrder()
    {
        var result = Grader.Grade(Input(Payload("""{"s4":"∠ C B A","s3":"OPT-UP-1","s2":"KIZILIRMAK NEHRİ","s1":"0,75 cm"}""")));

        result.IsCorrect.ShouldBeTrue();
        result.CorrectCount.ShouldBe(4);
        result.TotalCount.ShouldBe(4);
        Rows(result.Feedback).ShouldBe(new[] { ("s1", true), ("s2", true), ("s3", true), ("s4", true) });
    }

    [Fact]
    public void Grade_PartialAndUnanswered_AreWrongAndStillListed()
    {
        var result = Grader.Grade(Input(Payload("""{"s1":"%75","s2":"kizilirmak","s3":"  "}""")));

        result.IsCorrect.ShouldBeFalse();
        result.CorrectCount.ShouldBe(1);
        result.TotalCount.ShouldBe(4);
        Rows(result.Feedback).ShouldBe(new[] { ("s1", true), ("s2", false), ("s3", false), ("s4", false) });
    }

    [Fact]
    public void Grade_NoPayload_EverySlotWrong()
    {
        var result = Grader.Grade(Input(null));

        result.CorrectCount.ShouldBe(0);
        result.TotalCount.ShouldBe(4);
        Rows(result.Feedback).All(r => !r.Correct).ShouldBeTrue();
    }

    [Fact]
    public void Grade_FeedbackNeverContainsTheSolution()
    {
        var result = Grader.Grade(Input(Payload("""{"s1":"1/2","s2":"Yeşilırmak","s3":"OPT-DOWN-2","s4":"∠ACB"}""")));

        var feedback = result.Feedback!;
        foreach (var secret in new[] { "3/4", "Kızılırmak", "Nehri", "OPT-UP-1", "∠ABC", "santimetre", "accept", "value" })
            feedback.ShouldNotContain(secret);
        using var doc = JsonDocument.Parse(feedback);
        doc.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(new[] { "rows" });
        foreach (var row in doc.RootElement.GetProperty("rows").EnumerateArray())
            row.EnumerateObject().Select(p => p.Name).ShouldBe(new[] { "id", "correct" });
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("{}")]
    [InlineData("not json")]
    public void Grade_WithoutSolution_IsUngradable(string? solution)
    {
        var result = Grader.Grade(Input(Payload("""{"s1":"3/4"}"""), solution));

        result.ShouldBe(InteractionGradeResult.Ungradable);
    }

    [Fact]
    public void Grade_SlotMissingFromSolution_IsWrong()
    {
        var partialSolution = """{"s1":{"value":"3/4"}}""";

        var result = Grader.Grade(Input(Payload("""{"s1":"3/4","s2":"Kızılırmak"}"""), partialSolution));

        Rows(result.Feedback).ShouldBe(new[] { ("s1", true), ("s2", false), ("s3", false), ("s4", false) });
    }

    [Fact]
    public void Grade_NumberDefaults_EquivalenceOnNoUnits()
    {
        var plan = "{\"version\":1,\"type\":\"slotFill\",\"slots\":[{\"id\":\"s1\"," + Box + ",\"input\":{\"kind\":\"number\"}}]}";
        Grader.Grade(Input(Payload("""{"s1":"%75"}"""), """{"s1":{"value":"3/4"}}""", plan)).IsCorrect.ShouldBeTrue();
        Grader.Grade(Input(Payload("""{"s1":"0,75 cm"}"""), """{"s1":{"value":"3/4"}}""", plan)).IsCorrect.ShouldBeFalse();
    }

    [Fact]
    public void Grade_CorruptPlan_IsUngradableAndDoesNotThrow()
    {
        Grader.Grade(Input(Payload("""{"s1":"3/4"}"""), plan: "{\"slots\":")).ShouldBe(InteractionGradeResult.Ungradable);
        Grader.Grade(Input(Payload("""{"s1":"3/4"}"""), plan: "[]")).ShouldBe(InteractionGradeResult.Ungradable);
    }

    // ---------------------------------------------------------------- Validate / Normalize

    [Theory]
    [InlineData("""{"s1":"3/4","s3":"OPT-UP-1"}""")]
    [InlineData("""{"s1":"tamamen yanlış ama metin","s2":null}""")]
    [InlineData("""{}""")]
    [InlineData("""{"s3":""}""")]
    public void Validate_WellFormedPayload_IsValid(string values)
        => Grader.Validate(Plan, Payload(values)).ShouldBeTrue();

    [Fact]
    public void Validate_NullPayload_IsValid() => Grader.Validate(Plan, null).ShouldBeTrue();

    [Theory]
    [InlineData("""{"version":1,"type":"slotFill","values":{"s9":"x"}}""")]                  // plan dışı kutu
    [InlineData("""{"version":1,"type":"slotFill","values":{"s3":"NOPE"}}""")]               // seçimde plan dışı seçenek
    [InlineData("""{"version":1,"type":"slotFill","values":{"s1":3}}""")]                    // string değil
    [InlineData("""{"version":1,"type":"slotFill","values":{"s1":"a","s1":"b"}}""")]         // yinelenen anahtar
    [InlineData("""{"version":1,"type":"slotFill","values":{"bad id":"a"}}""")]              // id deseni
    [InlineData("""{"version":2,"type":"slotFill","values":{"s1":"a"}}""")]                  // sürüm
    [InlineData("""{"version":1,"type":"matrixChoice","selections":{}}""")]                  // başka tip
    [InlineData("""{"version":1,"type":"slotFill"}""")]                                      // values yok
    [InlineData("""{"version":1,"type":"slotFill","values":[]}""")]                          // values dizi
    [InlineData("not json")]
    public void Validate_MalformedPayload_IsInvalid(string payload)
        => Grader.Validate(Plan, payload).ShouldBeFalse();

    [Fact]
    public void Validate_ValueLongerThanLimit_IsInvalid()
    {
        var value = new string('a', SlotFillGrader.MaxValueLength + 1);
        Grader.Validate(Plan, Payload("{\"s2\":\"" + value + "\"}")).ShouldBeFalse();
        Grader.Validate(Plan, Payload("{\"s2\":\"" + value[..^1] + "\"}")).ShouldBeTrue();
    }

    [Fact]
    public void Validate_PayloadOverSizeCap_IsInvalid()
    {
        var big = Payload("{" + string.Join(",", Enumerable.Range(0, 200).Select(i => $"\"x{i}\":\"{new string('a', 100)}\"")) + "}");
        big.Length.ShouldBeGreaterThan(InteractionPayload.MaxLength);
        Grader.Validate(Plan, big).ShouldBeFalse();
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"s1":"","s2":"   ","s3":null}""")]
    public void Normalize_AllBlank_IsNull(string values) => Grader.Normalize(Payload(values)).ShouldBeNull();

    [Fact]
    public void Normalize_AnyValue_KeepsPayload()
    {
        var payload = Payload("""{"s1":"","s2":"x"}""");
        Grader.Normalize(payload).ShouldBe(payload);
    }

    // ---------------------------------------------------------------- ValidateDefinition

    [Fact]
    public void ValidateDefinition_ValidPlanAndSolution_IsValid()
        => Grader.ValidateDefinition(Plan, Solution).ShouldBeTrue();

    [Fact]
    public void ValidateDefinition_PlanWithoutSolution_IsDraft()
        => Grader.ValidateDefinition(Plan, null).ShouldBeTrue();

    // ---------------------------------------------------------------- görselsiz düzen (#590 dilim b)

    private const string TextSlots =
        """[{"id":"s1","input":{"kind":"text"}},{"id":"s2","input":{"kind":"number","entry":"plain"}}]""";

    private const string LayoutSolution = """{"s1":{"accept":["Ankara"]},"s2":{"value":"81"}}""";

    [Theory]
    [InlineData("""{"kind":"text","text":"Başkent [[s1]], il sayısı [[s2]]."}""")]
    [InlineData("""{"kind":"table","header":["Soru","Cevap"],"rows":[["Başkent","[[s1]]"],["İl sayısı","[[s2]]"]]}""")]
    [InlineData("""{"kind":"table","rows":[["[[s1]] ve [[s2]]"]]}""")]
    public void ValidateDefinition_LayoutWithoutBoxes_IsValid(string layout)
    {
        var plan = "{\"version\":1,\"type\":\"slotFill\",\"layout\":" + layout + ",\"slots\":" + TextSlots + "}";
        Grader.ValidateDefinition(plan, LayoutSolution).ShouldBeTrue();
        Grader.Grade(Input(Payload("""{"s1":"ankara","s2":"81"}"""), LayoutSolution, plan)).IsCorrect.ShouldBeTrue();
    }

    [Fact]
    public void ValidateDefinition_ListLayout_RequiresEverySlotLabel()
    {
        const string labelled =
            """{"version":1,"type":"slotFill","layout":{"kind":"list"},"slots":[{"id":"s1","label":"Başkent?","input":{"kind":"text"}},{"id":"s2","label":"İl sayısı?","input":{"kind":"number","entry":"plain"}}]}""";
        Grader.ValidateDefinition(labelled, LayoutSolution).ShouldBeTrue();
        const string unlabelled =
            """{"version":1,"type":"slotFill","layout":{"kind":"list"},"slots":[{"id":"s1","label":"Başkent?","input":{"kind":"text"}},{"id":"s2","label":" ","input":{"kind":"number","entry":"plain"}}]}""";
        Grader.ValidateDefinition(unlabelled, LayoutSolution).ShouldBeFalse();
    }

    [Theory]
    [InlineData("""{"kind":"text","text":"Yalnız [[s1]]."}""")]                                    // s2 metinde yok
    [InlineData("""{"kind":"text","text":"[[s1]] [[s2]] [[s1]]"}""")]                              // s1 iki kez
    [InlineData("""{"kind":"text","text":"[[s1]] [[s2]] [[s9]]"}""")]                              // bilinmeyen yer tutucu
    [InlineData("""{"kind":"text","text":"[[s1]] [[s2]]","answer":"Ankara"}""")]                   // düzende gizli alan
    [InlineData("""{"kind":"text","text":"[[s1]] \u0007 [[s2]]"}""")]                              // kontrol karakteri
    [InlineData("""{"kind":"grid"}""")]                                                            // bilinmeyen tür
    [InlineData("""{"kind":"table","rows":[["[[s1]]","a"],["[[s2]]"]]}""")]                         // düzensiz satır
    [InlineData("""{"kind":"table","header":["[[s1]]"],"rows":[["[[s2]]"]]}""")]                    // başlıkta kutu
    [InlineData("""{"kind":"table","rows":[]}""")]                                                 // boş tablo
    public void ValidateDefinition_InvalidLayout_IsRejected(string layout)
    {
        var plan = "{\"version\":1,\"type\":\"slotFill\",\"layout\":" + layout + ",\"slots\":" + TextSlots + "}";
        Grader.ValidateDefinition(plan, LayoutSolution).ShouldBeFalse();
    }

    [Fact]
    public void ValidateDefinition_LayoutAndBoxesDoNotMix()
    {
        // Düzenli planda kutu yasak; düzensiz planda kutu zorunlu.
        var boxed = "{\"version\":1,\"type\":\"slotFill\",\"layout\":{\"kind\":\"text\",\"text\":\"[[s1]]\"},\"slots\":[{\"id\":\"s1\","
            + Box + ",\"input\":{\"kind\":\"text\"}}]}";
        Grader.ValidateDefinition(boxed, null).ShouldBeFalse();
        Grader.ValidateDefinition("""{"version":1,"type":"slotFill","slots":[{"id":"s1","input":{"kind":"text"}}]}""", null)
            .ShouldBeFalse();
    }

    [Fact]
    public void ValidateDefinition_RegistryResolvesAndCanonicalizes()
    {
        InteractionGraderRegistry.Default.Resolve("SlotFill").ShouldBeOfType<SlotFillGrader>();
        InteractionGraderRegistry.Default.CanonicalType("SLOTFILL").ShouldBe("slotFill");
    }

    public static IEnumerable<object?[]> InvalidPlans()
    {
        string P(string slots) => "{\"version\":1,\"type\":\"slotFill\",\"slots\":[" + slots + "]}";
        string S(string input, string extra = "") => "{\"id\":\"s1\"," + Box + extra + ",\"input\":" + input + "}";
        yield return new object?[] { null };
        yield return new object?[] { P("") };                                                              // kutu yok
        yield return new object?[] { "{\"version\":1,\"type\":\"matrixChoice\",\"slots\":[" + S("{\"kind\":\"text\"}") + "]}" };
        yield return new object?[] { "{\"version\":2,\"type\":\"slotFill\",\"slots\":[" + S("{\"kind\":\"text\"}") + "]}" };
        yield return new object?[] { P(S("{\"kind\":\"text\"}")).Replace("\"slots\"", "\"hint\":1,\"slots\"") }; // kökte bilinmeyen alan
        yield return new object?[] { P(S("{\"kind\":\"text\"}", ",\"solution\":\"x\"")) };                 // kutuda bilinmeyen alan
        yield return new object?[] { P(S("{\"kind\":\"text\",\"answer\":\"x\"}")) };                       // girdide bilinmeyen alan
        yield return new object?[] { P(S("{\"kind\":\"essay\"}")) };                                        // bilinmeyen tür
        yield return new object?[] { P(S("{\"kind\":\"number\",\"entry\":\"scientific\"}")) };
        yield return new object?[] { P(S("{\"kind\":\"select\",\"options\":[{\"id\":\"o1\",\"label\":\"a\"}]}")) };          // tek seçenek
        yield return new object?[] { P(S("{\"kind\":\"select\",\"options\":[{\"id\":\"o1\",\"label\":\"a\"},{\"id\":\"o1\",\"label\":\"b\"}]}")) }; // yinelenen
        yield return new object?[] { P(S("{\"kind\":\"select\",\"options\":[{\"id\":\"o1\"},{\"id\":\"o2\",\"label\":\"b\"}]}")) }; // etiketsiz
        yield return new object?[] { P(S("{\"kind\":\"palette\",\"symbols\":[]}")) };
        yield return new object?[] { P(S("{\"kind\":\"palette\",\"symbols\":[\"123456789\"]}")) };          // > 8 karakter
        yield return new object?[] { P("{\"id\":\"s1\",\"input\":{\"kind\":\"text\"}}") };                  // kutusuz
        yield return new object?[] { P("{\"id\":\"s 1\"," + Box + ",\"input\":{\"kind\":\"text\"}}") };      // id deseni
        yield return new object?[] { P(S("{\"kind\":\"text\"}") + "," + S("{\"kind\":\"text\"}")) };       // yinelenen id
        yield return new object?[] { P(string.Join(",", Enumerable.Range(0, 81).Select(i =>
            "{\"id\":\"s" + i + "\"," + Box + ",\"input\":{\"kind\":\"text\"}}"))) };                       // 81 kutu
    }

    [Theory]
    [MemberData(nameof(InvalidPlans))]
    public void ValidateDefinition_InvalidPlan_IsRejected(string? plan)
        => Grader.ValidateDefinition(plan, null).ShouldBeFalse();

    [Theory]
    [InlineData("""{"s1":{"value":"3/4"},"s2":{"accept":["a"]},"s3":{"optionIds":["OPT-UP-1"]}}""")]                                   // s4 eksik
    [InlineData("""{"s1":{"value":"3/4"},"s2":{"accept":["a"]},"s3":{"optionIds":["OPT-UP-1"]},"s4":{"accept":["x"]},"s9":{"accept":["x"]}}""")] // plan dışı
    [InlineData("""{"s1":{"value":"3/0"},"s2":{"accept":["a"]},"s3":{"optionIds":["OPT-UP-1"]},"s4":{"accept":["x"]}}""")]              // geçersiz sayı
    [InlineData("""{"s1":{"value":"5 cm"},"s2":{"accept":["a"]},"s3":{"optionIds":["OPT-UP-1"]},"s4":{"accept":["x"]}}""")]             // değerde birim
    [InlineData("""{"s1":{"value":"5","unitMode":"required"},"s2":{"accept":["a"]},"s3":{"optionIds":["OPT-UP-1"]},"s4":{"accept":["x"]}}""")] // birim listesi yok
    [InlineData("""{"s1":{"value":"%5","unitMode":"required","units":["cm"]},"s2":{"accept":["a"]},"s3":{"optionIds":["OPT-UP-1"]},"s4":{"accept":["x"]}}""")] // yüzde + birim zorunlu
    [InlineData("""{"s1":{"value":"6/8","equivalence":false,"requireSimplest":true},"s2":{"accept":["a"]},"s3":{"optionIds":["OPT-UP-1"]},"s4":{"accept":["x"]}}""")] // çözülemez
    [InlineData("""{"s1":{"value":"3/4","unitMode":"sometimes","units":["cm"]},"s2":{"accept":["a"]},"s3":{"optionIds":["OPT-UP-1"]},"s4":{"accept":["x"]}}""")]
    [InlineData("""{"s1":{"value":"3/4","equivalence":"yes"},"s2":{"accept":["a"]},"s3":{"optionIds":["OPT-UP-1"]},"s4":{"accept":["x"]}}""")]
    [InlineData("""{"s1":{"value":"3/4","tolerance":0.1},"s2":{"accept":["a"]},"s3":{"optionIds":["OPT-UP-1"]},"s4":{"accept":["x"]}}""")] // bilinmeyen alan
    [InlineData("""{"s1":{"value":"3/4"},"s2":{"accept":[]},"s3":{"optionIds":["OPT-UP-1"]},"s4":{"accept":["x"]}}""")]                  // boş kabul
    [InlineData("""{"s1":{"value":"3/4"},"s2":{"accept":["..."]},"s3":{"optionIds":["OPT-UP-1"]},"s4":{"accept":["x"]}}""")]             // normalize boş
    [InlineData("""{"s1":{"value":"3/4"},"s2":{"accept":["İzmir","izmir"]},"s3":{"optionIds":["OPT-UP-1"]},"s4":{"accept":["x"]}}""")]   // normalize yinelenen
    [InlineData("""{"s1":{"value":"3/4"},"s2":{"accept":["a"]},"s3":{"optionIds":["NOPE"]},"s4":{"accept":["x"]}}""")]                  // plan dışı seçenek
    [InlineData("""{"s1":{"value":"3/4"},"s2":{"accept":["a"]},"s3":{"optionIds":[]},"s4":{"accept":["x"]}}""")]
    [InlineData("""{"s1":{"value":"3/4"},"s2":{"accept":["a"]},"s3":{"optionIds":["OPT-UP-1"]},"s4":{"accept":["  "]}}""")]
    [InlineData("""{"s1":{"value":"3/4"},"s2":{"accept":["a"]},"s3":{"optionIds":["OPT-UP-1"]},"s4":["x"]}""")]                         // nesne değil
    [InlineData("""["x"]""")]
    public void ValidateDefinition_InvalidSolution_IsRejected(string solution)
        => Grader.ValidateDefinition(Plan, solution).ShouldBeFalse();

    private static string EntryPlan(string entry)
        => "{\"version\":1,\"type\":\"slotFill\",\"slots\":[{\"id\":\"s1\"," + Box + ",\"input\":{\"kind\":\"number\",\"entry\":\"" + entry + "\"}}]}";

    [Theory]
    // Biçim birebir istenirken giriş bileşeninin üretemeyeceği çözüm biçimi → çözülemez, reddedilir.
    [InlineData("fraction", "0,75", false, false)]
    [InlineData("fraction", "%75", false, false)]
    [InlineData("fraction", "3", false, false)]
    [InlineData("fraction", "2 3/5", false, false)]
    [InlineData("mixed", "2,6", false, false)]
    [InlineData("mixed", "%60", false, false)]
    // Aynı biçim ya da denklik açık → çözülebilir.
    [InlineData("fraction", "3/4", false, true)]
    [InlineData("mixed", "2 3/5", false, true)]
    // Tam+pay+payda bileşeni tam kısım boşken "13/5", kesir boşken "2" gönderir.
    [InlineData("mixed", "13/5", false, true)]
    [InlineData("mixed", "2", false, true)]
    [InlineData("fraction", "0,75", true, true)]
    [InlineData("mixed", "2,6", true, true)]
    [InlineData("fraction", "2 3/5", true, true)]
    // plain her biçimi yazabilir.
    [InlineData("plain", "0,75", false, true)]
    [InlineData("plain", "%75", false, true)]
    [InlineData("plain", "3/4", false, true)]
    [InlineData("plain", "2 3/5", false, true)]
    public void ValidateDefinition_EntryVersusSolutionForm(string entry, string value, bool equivalence, bool expected)
    {
        var solution = "{\"s1\":{\"value\":\"" + value + "\",\"equivalence\":" + (equivalence ? "true" : "false") + "}}";
        Grader.ValidateDefinition(EntryPlan(entry), solution).ShouldBe(expected);
    }

    [Fact]
    public void ValidateDefinition_EntryOmitted_BehavesAsPlain()
    {
        var plan = "{\"version\":1,\"type\":\"slotFill\",\"slots\":[{\"id\":\"s1\"," + Box + ",\"input\":{\"kind\":\"number\"}}]}";
        Grader.ValidateDefinition(plan, """{"s1":{"value":"0,75","equivalence":false}}""").ShouldBeTrue();
    }

    private static string EntrySolution(string unitMode, string units)
        => "{\"s1\":{\"value\":\"5\",\"units\":" + units + ",\"unitMode\":\"" + unitMode + "\"}}";

    [Theory]
    [InlineData("fraction", "required", false)]   // kesir bileşeni birim alamaz → çözülemez
    [InlineData("mixed", "required", false)]
    [InlineData("plain", "required", true)]
    [InlineData("fraction", "optional", true)]
    [InlineData("mixed", "none", true)]
    public void ValidateDefinition_RequiredUnitNeedsPlainEntry(string entry, string unitMode, bool expected)
        => Grader.ValidateDefinition(EntryPlan(entry), EntrySolution(unitMode, """["cm"]""")).ShouldBe(expected);

    [Theory]
    [InlineData("""["cm","santimetre"]""", true)]
    [InlineData("""["m²"]""", true)]
    [InlineData("""["₺","TL"]""", true)]
    [InlineData("""["$"]""", true)]
    [InlineData("""["km/sa"]""", true)]
    [InlineData("""["°"]""", true)]
    [InlineData("""["²"]""", false)]            // ² birim başlatamaz
    [InlineData("""["%"]""", false)]            // yüzde birim değil
    [InlineData("""["5cm"]""", false)]          // rakamlı
    [InlineData("""["cm 3"]""", false)]
    [InlineData("""["\uD835\uDC1C"]""", false)] // astral harf
    [InlineData("""["cm","CM"]""", false)]      // normalize yinelenen
    public void ValidateDefinition_EachUnitMustBeTypeable(string units, bool expected)
        => Grader.ValidateDefinition(EntryPlan("plain"), EntrySolution("optional", units)).ShouldBe(expected);

    [Theory]
    [InlineData("__proto__")]
    [InlineData("constructor")]
    [InlineData("prototype")]
    public void ReservedIds_AreRejectedInPlanSolutionAndPayload(string id)
    {
        InteractionIds.IsValid(id).ShouldBeFalse();
        InteractionIds.IsValid(id.ToUpperInvariant()).ShouldBeTrue(); // yalnız birebir ad tehlikeli

        var slotPlan = "{\"version\":1,\"type\":\"slotFill\",\"slots\":[{\"id\":\"" + id + "\"," + Box + ",\"input\":{\"kind\":\"text\"}}]}";
        Grader.ValidateDefinition(slotPlan, null).ShouldBeFalse();
        var optionPlan = Plan.Replace("OPT-UP-1", id);
        Grader.ValidateDefinition(optionPlan, null).ShouldBeFalse();
        Grader.Validate(Plan, Payload("{\"" + id + "\":\"x\"}")).ShouldBeFalse();

        var matrix = new MatrixChoiceGrader();
        var matrixPlan = "{\"version\":1,\"type\":\"matrixChoice\",\"columns\":[{\"id\":\"cD\"},{\"id\":\"" + id + "\"}],\"rows\":[{\"id\":\"r1\"," + Box + "}]}";
        matrix.ValidateDefinition(matrixPlan, null).ShouldBeFalse();
        matrix.ValidateDefinition(matrixPlan.Replace(id, "cY"), null).ShouldBeTrue();
        matrix.Validate(matrixPlan.Replace(id, "cY"), "{\"version\":1,\"type\":\"matrixChoice\",\"selections\":{\"" + id + "\":[\"cD\"]}}").ShouldBeFalse();
    }

    [Theory]
    [InlineData("\u00a0")]
    [InlineData("\ufeff")]
    [InlineData("\u0085 \u202f")]
    public void Normalize_WhitespaceClassOnlyValues_AreBlank(string blank)
    {
        Grader.Normalize(Payload("{\"s2\":\"" + blank + "\"}")).ShouldBeNull();
        Grader.Grade(Input(Payload("{\"s2\":\"" + blank + "\"}"))).CorrectCount.ShouldBe(0);
    }

    [Fact]
    public void ValidateDefinition_AcceptEntryOverLimit_IsRejected()
    {
        var tooLong = new string('a', SlotFillGrader.MaxAcceptLength + 1);
        var solution = Solution.Replace("\"Kızılırmak\"", "\"" + tooLong + "\"");
        Grader.ValidateDefinition(Plan, solution).ShouldBeFalse();
    }
}
