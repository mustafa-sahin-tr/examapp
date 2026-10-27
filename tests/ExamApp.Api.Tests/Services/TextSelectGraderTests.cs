using System.Text.Json;
using ExamApp.Api.Services.Interactions;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #582: <see cref="TextSelectGrader"/> — üç mod (select / correct / punctuation) için kayıtta tanım doğrulaması, payload
/// doğrulama (katı), normalize (boş → null), kontrol (bulunan / eksik / fazla, çok token'lı hedef, düzeltme değeri, gap satırları),
/// geri bildirimde çözüm sızıntısı yok, çözümsüz soru puanlanamaz.
/// </summary>
public class TextSelectGraderTests
{
    private static readonly TextSelectGrader Grader = new();

    private static string Token(string id, string text, int line = 0, bool gap = false, int x = 0)
        => "{\"id\":\"" + id + "\",\"text\":\"" + text + "\",\"line\":" + line
           + ",\"boxes\":[{\"x\":" + x + ",\"y\":" + (line * 20) + ",\"width\":30,\"height\":18}]" + (gap ? ",\"gap\":true" : "") + "}";

    // "Sonun da gökte ki bulutlar uçuştu ." — hedefler: "Sonun da" (t1+t2), "gökte ki" (t3+t4); t5..t7 doğru kelimeler.
    private static readonly string Words = string.Join(",",
        Token("t1", "Sonun"), Token("t2", "da", x: 40), Token("t3", "gökte", x: 80), Token("t4", "ki", x: 120),
        Token("t5", "bulutlar", line: 1), Token("t6", "uçuştu", line: 1, x: 40), Token("t7", "yatak", line: 2));

    private static string PlanOf(string mode, string tokens, string extra = "")
        => "{\"version\":1,\"type\":\"textSelect\",\"mode\":\"" + mode + "\"" + extra + ",\"tokens\":[" + tokens + "]}";

    private static readonly string SelectPlan = PlanOf("select", Words, ",\"style\":\"circle\"");
    private static readonly string CorrectPlan = PlanOf("correct", Words, ",\"style\":\"underline\"");

    // Noktalama: "Eyvah ( ) Ne yaptın ( )" + "Gel ( )".
    private static readonly string PunctuationPlan = PlanOf("punctuation",
        string.Join(",", Token("t1", "Eyvah"), Token("g1", "( )", gap: true, x: 40), Token("t2", "Ne", x: 80),
            Token("t3", "yaptın", x: 120), Token("g2", "( )", gap: true, x: 160), Token("t4", "Gel", line: 1),
            Token("g3", "( )", line: 1, gap: true, x: 40)),
        ",\"palette\":[\".\",\",\",\"!\",\"?\"]");

    private const string SelectSolution = """{"targets":[{"tokenIds":["t1","t2"]},{"tokenIds":["t3","t4"]},{"tokenIds":["t7"]}]}""";

    // Çözüm değerleri bilinçli olarak ayırt edilebilir: geri bildirimde hiçbiri görünmemeli.
    private const string CorrectSolution =
        """{"targets":[{"tokenIds":["t1","t2"],"accept":["Sonunda"]},{"tokenIds":["t3","t4"],"accept":["gökteki"],"optionalSuffixes":["bulutlar"]}]}""";

    private const string PunctuationSolution = """{"gaps":{"g1":{"accept":["!"]},"g2":{"accept":["?"]},"g3":{"accept":[".","!"]}}}""";

    private static string Payload(string selected = "[]", string values = "{}")
        => "{\"version\":1,\"type\":\"textSelect\",\"selected\":" + selected + ",\"values\":" + values + "}";

    private static InteractionGradeInput Input(string plan, string? solution, string? payload)
        => new(plan, solution, payload, SelectedAnswerId: null, CorrectAnswerId: null);

    private sealed record Row(string Id, bool Correct, string? Reason);

    private static (List<Row> Rows, int? Found, int? Missing, int? Extra) Feedback(string? feedback)
    {
        feedback.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(feedback!);
        var root = doc.RootElement;
        var rows = root.GetProperty("rows").EnumerateArray()
            .Select(r => new Row(r.GetProperty("id").GetString()!, r.GetProperty("correct").GetBoolean(),
                r.TryGetProperty("reason", out var reason) ? reason.GetString() : null))
            .ToList();
        int? Count(string name) => root.TryGetProperty(name, out var p) ? p.GetInt32() : null;
        return (rows, Count("found"), Count("missing"), Count("extra"));
    }

    // ---------------------------------------------------------------- ValidateDefinition

    [Fact]
    public void ValidateDefinition_PilotDefinitionsOfEveryMode_AreValid()
    {
        Grader.ValidateDefinition(SelectPlan, SelectSolution).ShouldBeTrue();
        Grader.ValidateDefinition(CorrectPlan, CorrectSolution).ShouldBeTrue();
        Grader.ValidateDefinition(PunctuationPlan, PunctuationSolution).ShouldBeTrue();
        // Çözümsüz plan = taslak (puanlanamaz ama kaydedilir).
        Grader.ValidateDefinition(SelectPlan, null).ShouldBeTrue();
        Grader.ValidateDefinition(PunctuationPlan, " ").ShouldBeTrue();
    }

    [Fact]
    public void ValidateDefinition_HyphenatedTokenWithThreeBoxes_IsValid()
    {
        var token = "{\"id\":\"t1\",\"text\":\"dolaşmış\",\"line\":0,\"boxes\":[{\"x\":40,\"y\":0,\"width\":60,\"height\":20},{\"x\":0,\"y\":30,\"width\":30,\"height\":20},{\"x\":0,\"y\":60,\"width\":5,\"height\":20}]}";
        Grader.ValidateDefinition(PlanOf("select", token), """{"targets":[{"tokenIds":["t1"]}]}""").ShouldBeTrue();
    }

    public static TheoryData<string> InvalidPlans => new()
    {
        PlanOf("highlight", Words),                                                        // bilinmeyen mod
        "{\"version\":1,\"type\":\"textSelect\",\"tokens\":[" + Words + "]}",               // mod yok
        "{\"version\":2,\"type\":\"textSelect\",\"mode\":\"select\",\"tokens\":[" + Words + "]}", // sürüm
        "{\"version\":1,\"type\":\"slotFill\",\"mode\":\"select\",\"tokens\":[" + Words + "]}",   // tip
        PlanOf("select", Words, ",\"style\":\"bold\""),                                    // bilinmeyen stil
        PlanOf("select", Words, ",\"palette\":[\".\"]"),                                   // select'te palet
        PlanOf("select", Words, ",\"answer\":\"t1\""),                                     // izinli olmayan alan
        PlanOf("select", ""),                                                              // token yok
        PlanOf("select", Token("t1", "Ali") + "," + Token("t1", "Veli")),                  // yinelenen id
        PlanOf("select", Token("__proto__", "Ali")),                                       // ayrılmış id
        PlanOf("select", Token("t1", " ")),                                                // boş kelime
        PlanOf("select", Token("t1", new string('a', 65))),                                // metin > 64
        PlanOf("select", Token("t1", "Ali", line: 2) + "," + Token("t2", "Veli", line: 1)), // satır geriler
        PlanOf("select", Token("t1", "Ali", line: 300)),                                   // satır > 299
        PlanOf("select", Token("t1", "Ali") + "," + Token("g1", "( )", gap: true)),        // select'te gap
        PlanOf("correct", Token("g1", "( )", gap: true)),                                  // correct'te gap
        PlanOf("punctuation", Token("t1", "Ali"), ",\"palette\":[\".\"]"),                 // punctuation'da gap yok
        PlanOf("punctuation", Token("g1", "( )", gap: true)),                              // punctuation'da palet yok
        PlanOf("punctuation", Token("g1", "( )", gap: true), ",\"palette\":[]"),           // boş palet
        PlanOf("punctuation", Token("g1", "( )", gap: true), ",\"palette\":[\".\",\".\"]"), // yinelenen sembol
        PlanOf("punctuation", Token("g1", "( )", gap: true), ",\"palette\":[\"123456789\"]"), // sembol > 8
        PlanOf("select", "{\"id\":\"t1\",\"text\":\"Ali\",\"line\":0,\"boxes\":[]}"),     // kutusuz
        PlanOf("select", "{\"id\":\"t1\",\"text\":\"Ali\",\"line\":0,\"boxes\":[{\"x\":-1,\"y\":0,\"width\":5,\"height\":5}]}"), // negatif kutu
        PlanOf("select", "{\"id\":\"t1\",\"text\":\"Ali\",\"line\":0,\"boxes\":[{\"x\":0,\"y\":0,\"width\":5,\"height\":5,\"hint\":1}]}"), // kutuda alan
        PlanOf("select", "{\"id\":\"t1\",\"text\":\"Ali\",\"line\":0,\"boxes\":[{\"x\":0,\"y\":0,\"width\":5,\"height\":5}],\"target\":true}"), // token'da gizli alan
        PlanOf("select", "{\"id\":\"t1\",\"text\":\"Ali\",\"line\":0,\"gap\":\"yes\",\"boxes\":[{\"x\":0,\"y\":0,\"width\":5,\"height\":5}]}"), // gap bool değil
    };

    [Theory]
    [MemberData(nameof(InvalidPlans))]
    public void ValidateDefinition_InvalidPlan_IsRejected(string plan)
    {
        Grader.ValidateDefinition(plan, null).ShouldBeFalse();
    }

    [Fact]
    public void ValidateDefinition_TooManyTokensBoxesOrGaps_IsRejected()
    {
        PlanOf("select", string.Join(",", Enumerable.Range(1, 601).Select(i => Token($"t{i}", "a"))))
            .ShouldSatisfyAllConditions(p => Grader.ValidateDefinition(p, null).ShouldBeFalse());
        Grader.ValidateDefinition(PlanOf("select", string.Join(",", Enumerable.Range(1, 600).Select(i => Token($"t{i}", "a")))), null)
            .ShouldBeTrue();
        var fourBoxes = "{\"id\":\"t1\",\"text\":\"a\",\"line\":0,\"boxes\":[" + string.Join(",", Enumerable.Repeat("{\"x\":0,\"y\":0,\"width\":5,\"height\":5}", 4)) + "]}";
        Grader.ValidateDefinition(PlanOf("select", fourBoxes), null).ShouldBeFalse();
        var gaps = string.Join(",", Enumerable.Range(1, 101).Select(i => Token($"g{i}", "( )", gap: true)));
        Grader.ValidateDefinition(PlanOf("punctuation", gaps, ",\"palette\":[\".\"]"), null).ShouldBeFalse();
    }

    [Theory]
    [InlineData("select", """{"targets":[]}""")]                                                 // hedef yok
    [InlineData("select", """{"targets":[{"tokenIds":["t9"]}]}""")]                              // plan dışı token
    [InlineData("select", """{"targets":[{"tokenIds":["t1"]},{"tokenIds":["t1","t2"]}]}""")]     // token iki hedefte
    [InlineData("select", """{"targets":[{"tokenIds":["t1","t1"]}]}""")]                         // yinelenen token
    [InlineData("select", """{"targets":[{"tokenIds":[]}]}""")]                                  // boş hedef
    [InlineData("select", """{"targets":[{"tokenIds":["t1","t2","t3","t4","t5","t6","t7","t8","t9"]}]}""")] // > 8 token
    [InlineData("select", """{"targets":[{"tokenIds":["t1"],"accept":["x"]}]}""")]               // select'te kabul
    [InlineData("select", """{"targets":[{"tokenIds":["t1"]}],"note":"x"}""")]                   // kökte alan
    [InlineData("select", """{"gaps":{}}""")]                                                    // başka modun çözümü
    [InlineData("correct", """{"targets":[{"tokenIds":["t1"]}]}""")]                             // kabul yok
    [InlineData("correct", """{"targets":[{"tokenIds":["t1"],"accept":[]}]}""")]                 // boş kabul
    [InlineData("correct", """{"targets":[{"tokenIds":["t1"],"accept":["Sonunda","SONUNDA"]}]}""")] // normalize hâli yinelenir
    [InlineData("correct", """{"targets":[{"tokenIds":["t1"],"accept":["."]}]}""")]              // normalize hâli boş
    public void ValidateDefinition_InvalidTargetSolution_IsRejected(string mode, string solution)
    {
        Grader.ValidateDefinition(mode == "select" ? SelectPlan : CorrectPlan, solution).ShouldBeFalse();
    }

    [Theory]
    [InlineData("""{"gaps":{"g1":{"accept":["!"]},"g2":{"accept":["?"]}}}""")]                                   // g3 çözümsüz
    [InlineData("""{"gaps":{"g1":{"accept":["!"]},"g2":{"accept":["?"]},"g3":{"accept":[";"]}}}""")]             // palet dışı
    [InlineData("""{"gaps":{"g1":{"accept":["!"]},"g2":{"accept":["?"]},"g3":{"accept":[]}}}""")]                // boş kabul
    [InlineData("""{"gaps":{"g1":{"accept":["!"]},"g2":{"accept":["?"]},"g3":{"accept":[".","."]}}}""")]         // yinelenen
    [InlineData("""{"gaps":{"g1":{"accept":["!"]},"g2":{"accept":["?"]},"g3":{"accept":["."]},"t1":{"accept":["."]}}}""")] // gap olmayan token
    [InlineData("""{"gaps":{"g1":{"accept":["!"],"hint":1},"g2":{"accept":["?"]},"g3":{"accept":["."]}}}""")]    // izinli olmayan alan
    [InlineData("""{"targets":[{"tokenIds":["t1"]}]}""")]                                                        // başka modun çözümü
    public void ValidateDefinition_InvalidPunctuationSolution_IsRejected(string solution)
    {
        Grader.ValidateDefinition(PunctuationPlan, solution).ShouldBeFalse();
    }

    // ---------------------------------------------------------------- Validate / Normalize

    [Theory]
    [InlineData("select", """{"version":1,"type":"textSelect","selected":["t1","t3"]}""")]
    [InlineData("select", """{"type":"textSelect","selected":["t7"],"values":{}}""")]
    [InlineData("correct", """{"version":1,"type":"textSelect","selected":["t1"],"values":{"t1":"Sonunda"}}""")]
    [InlineData("correct", """{"version":1,"type":"textSelect","selected":["t1","t2"],"values":{"t2":""}}""")]
    [InlineData("punctuation", """{"version":1,"type":"textSelect","values":{"g1":" ! ","g2":"","g3":null}}""")]
    public void Validate_WellFormedPayload_IsAccepted(string mode, string payload)
    {
        Grader.Validate(PlanFor(mode), payload).ShouldBeTrue();
    }

    [Theory]
    [InlineData("select", """{"version":1,"type":"textSelect","selected":["t99"]}""")]                    // plan dışı token
    [InlineData("select", """{"version":1,"type":"textSelect","selected":["t1","t1"]}""")]                // yinelenen
    [InlineData("select", """{"version":1,"type":"textSelect","selected":["t1"],"values":{"t1":"x"}}""")] // select'te değer
    [InlineData("select", """{"version":1,"type":"textSelect","selected":[1]}""")]                         // string değil
    [InlineData("select", """{"version":1,"type":"textSelect","selected":["t1"],"extra":true}""")]        // izinli olmayan alan
    [InlineData("select", """{"version":1,"type":"slotFill","values":{}}""")]                              // başka tip
    [InlineData("select", """{"version":2,"type":"textSelect","selected":["t1"]}""")]                     // sürüm
    [InlineData("correct", """{"version":1,"type":"textSelect","selected":["t1"],"values":{"t2":"da"}}""")] // seçilmemiş token'a değer
    [InlineData("punctuation", """{"version":1,"type":"textSelect","selected":["t1"]}""")]                // punctuation'da seçim
    [InlineData("punctuation", """{"version":1,"type":"textSelect","values":{"g1":";"}}""")]             // palet dışı sembol
    [InlineData("punctuation", """{"version":1,"type":"textSelect","values":{"t1":"!"}}""")]             // gap olmayan token
    [InlineData("punctuation", """{"version":1,"type":"textSelect","values":{"g1":"!","g1":"?"}}""")]    // yinelenen anahtar
    public void Validate_MalformedPayload_IsRejected(string mode, string payload)
    {
        Grader.Validate(PlanFor(mode), payload).ShouldBeFalse();
    }

    [Fact]
    public void Validate_TooLongValueOrPayload_IsRejected()
    {
        Grader.Validate(CorrectPlan, Payload("[\"t1\"]", "{\"t1\":\"" + new string('a', 201) + "\"}")).ShouldBeFalse();
        Grader.Validate(CorrectPlan, Payload("[\"t1\"]", "{\"t1\":\"" + new string('a', 200) + "\"}")).ShouldBeTrue();
        Grader.Validate(SelectPlan, Payload("[\"t1\"]") + new string(' ', InteractionPayload.MaxLength)).ShouldBeFalse();
    }

    [Fact]
    public void ValidateAndNormalize_EmptyAnswer_IsNoAnswer()
    {
        var empty = Payload("[]", "{\"g1\":\" \"}");
        Grader.Validate(PunctuationPlan, empty).ShouldBeTrue();
        Grader.Normalize(empty).ShouldBeNull();
        Grader.Normalize("""{"type":"textSelect"}""").ShouldBeNull();
        Grader.Normalize(Payload("[\"t1\"]")).ShouldNotBeNull();
        Grader.Validate(SelectPlan, null).ShouldBeTrue();
    }

    private static string PlanFor(string mode) => mode switch
    {
        "select" => SelectPlan,
        "correct" => CorrectPlan,
        _ => PunctuationPlan,
    };

    // ---------------------------------------------------------------- Grade: select

    [Fact]
    public void Grade_Select_AllTargetsFoundNoExtra_IsCorrect()
    {
        // Çok token'lı hedefin bir token'ı yeter; ikisi birden de doğru.
        var result = Grader.Grade(Input(SelectPlan, SelectSolution, Payload("[\"t7\",\"t2\",\"t3\",\"t4\"]")));

        result.IsCorrect.ShouldBeTrue();
        result.CorrectCount.ShouldBe(3);
        result.TotalCount.ShouldBe(3);
        var (rows, found, missing, extra) = Feedback(result.Feedback);
        rows.ShouldBe([new("t2", true, null), new("t3", true, null), new("t4", true, null), new("t7", true, null)]); // plan sırası
        (found, missing, extra).ShouldBe((3, 0, 0));
    }

    [Fact]
    public void Grade_Select_MissingAndExtra_AreCountedSeparately()
    {
        var result = Grader.Grade(Input(SelectPlan, SelectSolution, Payload("[\"t5\",\"t1\",\"t6\"]")));

        result.IsCorrect.ShouldBeFalse();
        result.CorrectCount.ShouldBe(1);
        result.TotalCount.ShouldBe(5);             // 3 hedef + 2 fazla
        var (rows, found, missing, extra) = Feedback(result.Feedback);
        rows.ShouldBe([new("t1", true, null), new("t5", false, "extra"), new("t6", false, "extra")]);
        (found, missing, extra).ShouldBe((1, 2, 2));
    }

    [Fact]
    public void Grade_Select_AllFoundPlusExtra_IsNotCorrect()
    {
        var result = Grader.Grade(Input(SelectPlan, SelectSolution, Payload("[\"t1\",\"t3\",\"t7\",\"t5\"]")));

        result.IsCorrect.ShouldBeFalse();
        (result.CorrectCount, result.TotalCount).ShouldBe((3, 4));
    }

    [Fact]
    public void Grade_Select_NoPayload_EverythingMissingNoRows()
    {
        var result = Grader.Grade(Input(SelectPlan, SelectSolution, null));

        (result.IsCorrect, result.CorrectCount, result.TotalCount).ShouldBe((false, 0, 3));
        var (rows, found, missing, extra) = Feedback(result.Feedback);
        rows.ShouldBeEmpty();
        (found, missing, extra).ShouldBe((0, 3, 0));
    }

    [Fact]
    public void Grade_Select_TooManyExtraSelections_HidesRowsButKeepsCounts()
    {
        // review #582 (D1): 3 hedef → eşik Math.Max(3, 3) = 3; t5, t6 + 2 fazla token daha (4 > 3) → satır yok, tooMany.
        var plan = PlanOf("select", Words + "," + Token("t8", "eve", line: 2, x: 40) + "," + Token("t9", "geldi", line: 2, x: 80));
        var result = Grader.Grade(Input(plan, SelectSolution, Payload("[\"t1\",\"t5\",\"t6\",\"t8\",\"t9\"]")));

        (result.IsCorrect, result.CorrectCount, result.TotalCount).ShouldBe((false, 1, 7));
        using var doc = JsonDocument.Parse(result.Feedback!);
        var root = doc.RootElement;
        root.GetProperty("rows").GetArrayLength().ShouldBe(0);
        root.GetProperty("tooMany").GetBoolean().ShouldBeTrue();
        (root.GetProperty("found").GetInt32(), root.GetProperty("missing").GetInt32(), root.GetProperty("extra").GetInt32())
            .ShouldBe((1, 2, 4));
    }

    [Fact]
    public void Grade_Select_ExtraAtThreshold_StillListsRows()
    {
        // 3 fazla = eşik (aşmıyor) → satırlar yazılır, tooMany yok.
        var plan = PlanOf("select", Words + "," + Token("t8", "eve", line: 2, x: 40));
        var result = Grader.Grade(Input(plan, SelectSolution, Payload("[\"t5\",\"t6\",\"t8\"]")));

        var (rows, _, _, extra) = Feedback(result.Feedback);
        rows.Count.ShouldBe(3);
        extra.ShouldBe(3);
        result.Feedback!.ShouldNotContain("tooMany");
    }

    [Fact]
    public void Grade_SelectAll_ManyTargets_ThresholdFollowsTargetCount()
    {
        // 10 hedef + 11 fazla (11 > max(3,10)) → tooMany; 10 fazla → satırlar görünür.
        var tokens = string.Join(",", Enumerable.Range(1, 21).Select(i => Token($"t{i}", "k")));
        var plan = PlanOf("select", tokens);
        var solution = "{\"targets\":[" + string.Join(",", Enumerable.Range(1, 10).Select(i => "{\"tokenIds\":[\"t" + i + "\"]}")) + "]}";
        var all = "[" + string.Join(",", Enumerable.Range(1, 21).Select(i => "\"t" + i + "\"")) + "]";
        var allButOne = "[" + string.Join(",", Enumerable.Range(1, 20).Select(i => "\"t" + i + "\"")) + "]";

        Grader.Grade(Input(plan, solution, Payload(all))).Feedback!.ShouldContain("\"tooMany\":true");
        Grader.Grade(Input(plan, solution, Payload(allButOne))).Feedback!.ShouldNotContain("tooMany");
    }

    // ---------------------------------------------------------------- Grade: correct

    [Fact]
    public void Grade_Correct_NormalizedCorrectionOnAnyTargetToken_IsFound()
    {
        // "SONUNDA." → normalize "sonunda"; "gökteki bulutlar" isteğe bağlı ekle kabul.
        var result = Grader.Grade(Input(CorrectPlan, CorrectSolution,
            Payload("[\"t2\",\"t1\",\"t3\"]", "{\"t2\":\"SONUNDA.\",\"t3\":\"gökteki bulutlar\"}")));

        result.IsCorrect.ShouldBeTrue();
        (result.CorrectCount, result.TotalCount).ShouldBe((2, 2));
        var (rows, _, _, _) = Feedback(result.Feedback);
        rows.ShouldBe([new("t1", true, null), new("t2", true, null), new("t3", true, null)]);
    }

    [Fact]
    public void Grade_Correct_WrongOrEmptyValue_IsReasonValue_AndOtherTokenIsExtra()
    {
        var result = Grader.Grade(Input(CorrectPlan, CorrectSolution,
            Payload("[\"t1\",\"t3\",\"t6\"]", "{\"t1\":\"Sonun da\",\"t6\":\"uçuştu\"}")));

        result.IsCorrect.ShouldBeFalse();
        (result.CorrectCount, result.TotalCount).ShouldBe((0, 3));
        var (rows, found, missing, extra) = Feedback(result.Feedback);
        rows.ShouldBe([new("t1", false, "value"), new("t3", false, "value"), new("t6", false, "extra")]);
        (found, missing, extra).ShouldBe((0, 2, 1));
    }

    // ---------------------------------------------------------------- Grade: punctuation

    [Fact]
    public void Grade_Punctuation_RowPerGapInPlanOrder()
    {
        var result = Grader.Grade(Input(PunctuationPlan, PunctuationSolution, Payload("[]", "{\"g3\":\" ! \",\"g1\":\"!\",\"g2\":\".\"}")));

        result.IsCorrect.ShouldBeFalse();
        (result.CorrectCount, result.TotalCount).ShouldBe((2, 3));
        var (rows, found, missing, extra) = Feedback(result.Feedback);
        rows.ShouldBe([new("g1", true, null), new("g2", false, null), new("g3", true, null)]);
        (found, missing, extra).ShouldBe((null, null, null));     // noktalamada sayaç alanları yok
    }

    [Fact]
    public void Grade_Punctuation_AllCorrect_AndUnansweredGapIsWrong()
    {
        Grader.Grade(Input(PunctuationPlan, PunctuationSolution, Payload("[]", "{\"g1\":\"!\",\"g2\":\"?\",\"g3\":\".\"}")))
            .IsCorrect.ShouldBeTrue();
        var partial = Grader.Grade(Input(PunctuationPlan, PunctuationSolution, Payload("[]", "{\"g1\":\"!\"}")));
        (partial.CorrectCount, partial.TotalCount).ShouldBe((1, 3));
    }

    // ---------------------------------------------------------------- sızıntı / puanlanamaz

    [Fact]
    public void Grade_FeedbackNeverContainsTheSolution()
    {
        var feedbacks = new[]
        {
            Grader.Grade(Input(CorrectPlan, CorrectSolution, Payload("[\"t1\",\"t5\"]", "{\"t1\":\"yanlis\"}"))).Feedback!,
            Grader.Grade(Input(SelectPlan, SelectSolution, Payload("[\"t5\"]"))).Feedback!,
            Grader.Grade(Input(PunctuationPlan, PunctuationSolution, Payload("[]", "{\"g1\":\".\"}"))).Feedback!,
        };

        foreach (var feedback in feedbacks)
        {
            foreach (var secret in new[] { "Sonunda", "gökteki", "bulutlar", "accept", "targets", "tokenIds", "!", "?", "t7", "t2", "t3", "t4" })
                feedback.ShouldNotContain(secret);
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("""{"targets":[]}""")]
    [InlineData("""{"targets":[{"tokenIds":["t99"]}]}""")]
    [InlineData("not json")]
    public void Grade_WithoutUsableSolution_IsUngradable(string? solution)
    {
        Grader.Grade(Input(SelectPlan, solution, Payload("[\"t1\"]"))).ShouldBe(InteractionGradeResult.Ungradable);
        Grader.Grade(Input(PunctuationPlan, solution, Payload("[]", "{\"g1\":\"!\"}"))).ShouldBe(InteractionGradeResult.Ungradable);
    }

    [Fact]
    public void Grade_Punctuation_SolutionGapsNotInPlan_IsUngradable()
    {
        Grader.Grade(Input(PunctuationPlan, """{"gaps":{"g9":{"accept":["!"]}}}""", Payload("[]", "{\"g1\":\"!\"}")))
            .ShouldBe(InteractionGradeResult.Ungradable);
    }

    [Fact]
    public void Grade_BrokenPlanOrPayload_DoesNotThrow()
    {
        Grader.Grade(Input("{}", SelectSolution, Payload("[\"t1\"]"))).ShouldBe(InteractionGradeResult.Ungradable);
        Grader.Grade(Input(SelectPlan, SelectSolution, "{broken")).TotalCount.ShouldBe(3);
    }

    [Fact]
    public void Registry_ResolvesTextSelectCaseInsensitively()
    {
        InteractionGraderRegistry.Default.Resolve("TextSelect").ShouldBeOfType<TextSelectGrader>();
        InteractionGraderRegistry.Default.CanonicalType("TEXTSELECT").ShouldBe(TextSelectGrader.Type);
    }
}
