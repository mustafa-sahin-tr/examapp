using System.Text.Json;
using ExamApp.Api.Services.Interactions;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #583: <c>openText</c> kontrolcüsü — tanım (plan allowlist, sınır aralıkları, çözümde yalnız sampleAnswer/rubric), öğrenci
/// payload'u (tip, kontrol karakteri, uzunluk, maxWords × 2), normalize (boş → null, satır sonları, kırpma), puanlama (her zaman 0/0,
/// cevap varsa "pending" geri bildirimi) ve öğrenciye giden çözüm (rubrik yok).
/// </summary>
public class OpenTextGraderTests
{
    private readonly OpenTextGrader _grader = new();

    private const string Plan = """{"version":1,"type":"openText","minWords":null,"maxWords":5,"lines":5}""";

    private static string Payload(string? text) => JsonSerializer.Serialize(new { version = 1, type = "openText", text });

    [Theory]
    [InlineData("""{"version":1,"type":"openText"}""", null)]
    [InlineData("""{"type":"openText","minWords":10,"maxWords":200,"lines":30}""", null)]
    [InlineData("""{"version":1,"type":"openText","minWords":5,"maxWords":5,"lines":1}""", """{}""")]
    [InlineData(Plan, """{"sampleAnswer":"Çünkü su buharlaşır.","rubric":"Buharlaşmayı söylemeli."}""")]
    [InlineData(Plan, """{"sampleAnswer":null,"rubric":"Satır 1\nSatır 2"}""")]
    public void ValidateDefinition_ValidPlansAndSolutions_AreAccepted(string plan, string? solution)
        => _grader.ValidateDefinition(plan, solution).ShouldBeTrue();

    [Theory]
    [InlineData(null, null)]                                                                         // plansız
    [InlineData("""{"version":1,"type":"slotFill"}""", null)]                                        // başka tip
    [InlineData("""{"version":2,"type":"openText"}""", null)]                                        // sürüm
    [InlineData("""{"version":1,"type":"openText","instruction":"Açıkla"}""", null)]                 // izinli olmayan alan
    [InlineData("""{"version":1,"type":"openText","maxWords":0}""", null)]                           // aralık dışı
    [InlineData("""{"version":1,"type":"openText","maxWords":2001}""", null)]
    [InlineData("""{"version":1,"type":"openText","maxWords":"10"}""", null)]                        // sayı değil
    [InlineData("""{"version":1,"type":"openText","minWords":20,"maxWords":10}""", null)]           // min > max
    [InlineData("""{"version":1,"type":"openText","lines":31}""", null)]
    [InlineData(Plan, """{"sampleAnswer":"x","score":5}""")]                                         // çözümde bilinmeyen alan
    [InlineData(Plan, """{"sampleAnswer":5}""")]                                                     // metin değil
    [InlineData(Plan, """{"rubric":"a\u0001b"}""")]                                                  // kontrol karakteri
    [InlineData(Plan, """["x"]""")]                                                                  // nesne değil
    public void ValidateDefinition_InvalidPlansOrSolutions_AreRejected(string? plan, string? solution)
        => _grader.ValidateDefinition(plan, solution).ShouldBeFalse();

    [Fact]
    public void ValidateDefinition_SolutionTextOverLimit_IsRejected()
    {
        var solution = JsonSerializer.Serialize(new { sampleAnswer = new string('a', OpenTextGrader.MaxSolutionTextLength + 1) });
        _grader.ValidateDefinition(Plan, solution).ShouldBeFalse();
    }

    [Fact]
    public void Validate_AcceptsTextUpToTwiceMaxWords_AndRejectsMore()
    {
        _grader.Validate(Plan, Payload("bir iki üç dört beş altı yedi sekiz dokuz on")).ShouldBeTrue();   // 10 = 2 × 5
        _grader.Validate(Plan, Payload("bir iki üç dört beş altı yedi sekiz dokuz on onbir")).ShouldBeFalse();
        _grader.Validate("""{"version":1,"type":"openText"}""", Payload(string.Join(' ', Enumerable.Repeat("soz", 1900)))).ShouldBeTrue();
    }

    [Theory]
    [InlineData("""{"version":1,"type":"slotFill","text":"x"}""")]
    [InlineData("""{"version":2,"type":"openText","text":"x"}""")]
    [InlineData("""{"version":1,"type":"openText","text":5}""")]
    [InlineData("""{"version":1,"type":"openText","text":"x","html":"<b>"}""")]
    [InlineData("""{"version":1,"type":"openText","text":"a\u0000b"}""")]
    [InlineData("""{"version":1,"type":"openText","text":"a","text":"b"}""")]
    [InlineData("not json")]
    public void Validate_MalformedPayload_IsRejected(string payload)
        => _grader.Validate(Plan, payload).ShouldBeFalse();

    [Fact]
    public void Validate_TextOverMaxLength_IsRejected()
    {
        _grader.Validate("""{"version":1,"type":"openText"}""", Payload(new string('a', OpenTextGrader.MaxTextLength))).ShouldBeTrue();
        _grader.Validate("""{"version":1,"type":"openText"}""", Payload(new string('a', OpenTextGrader.MaxTextLength + 1))).ShouldBeFalse();
    }

    [Fact]
    public void Validate_BlankPayloadNeedsNoPlan()
    {
        _grader.Validate(null, null).ShouldBeTrue();
        _grader.Validate(null, Payload("   ")).ShouldBeTrue();
        _grader.Validate(null, Payload("metin")).ShouldBeFalse();   // cevaplı payload okunabilir plan ister
    }

    [Fact]
    public void Normalize_BlankIsNull_TextIsCanonical()
    {
        _grader.Normalize(Payload(" \n\t ")).ShouldBeNull();
        _grader.Normalize(Payload(null)).ShouldBeNull();
        _grader.Normalize("""{"type":"openText","text":"  Su\r\nbuharlaşır.\r "}""")
            .ShouldBe("""{"version":1,"type":"openText","text":"Su\nbuharlaşır."}""");
    }

    [Fact]
    public void Grade_IsNeverCountedAndMarksAnsweredRowPending()
    {
        var answered = _grader.Grade(new InteractionGradeInput(Plan, """{"sampleAnswer":"x"}""", Payload("cevabım"), null, null));
        answered.IsCorrect.ShouldBeFalse();
        answered.CorrectCount.ShouldBe(0);
        answered.TotalCount.ShouldBe(0);
        answered.Feedback.ShouldBe("""{"rows":[{"id":"text","status":"pending"}]}""");

        _grader.Grade(new InteractionGradeInput(Plan, null, null, null, null)).ShouldBe(InteractionGradeResult.Ungradable);
    }

    [Fact]
    public void AnswerHash_DependsOnlyOnNormalizedText()
    {
        OpenTextGrader.AnswerHash(Payload("Su buharlaşır.")).ShouldBe(OpenTextGrader.AnswerHash(Payload("  Su buharlaşır.\r\n")));
        OpenTextGrader.AnswerHash(Payload("Su buharlaşır.")).ShouldNotBe(OpenTextGrader.AnswerHash(Payload("Su buharlaşmaz.")));
        OpenTextGrader.AnswerHash(Payload("Su buharlaşır."))!.Length.ShouldBe(64);
        OpenTextGrader.AnswerHash(null).ShouldBeNull();
    }

    [Theory]
    [InlineData("", 0)]
    [InlineData("  ", 0)]
    [InlineData("tek", 1)]
    [InlineData(" iki\n\tsöz ", 2)]
    [InlineData("a-b, c.", 2)]
    public void CountWords_CountsNonWhitespaceRuns(string text, int expected)
        => OpenTextGrader.CountWords(text).ShouldBe(expected);

    [Fact]
    public void StudentVisibleSolution_DropsRubric()
    {
        OpenTextGrader.StudentVisibleSolution("openText", """{"sampleAnswer":"Örnek","rubric":"Gizli"}""")
            .ShouldBe("""{"sampleAnswer":"Örnek"}""");
        OpenTextGrader.StudentVisibleSolution("openText", """{"rubric":"Gizli"}""").ShouldBeNull();
        OpenTextGrader.StudentVisibleSolution("openText", "bozuk").ShouldBeNull();
        OpenTextGrader.StudentVisibleSolution("slotFill", """{"s1":{"accept":["x"]}}""").ShouldBe("""{"s1":{"accept":["x"]}}""");
    }

    [Fact]
    public void Registry_ResolvesOpenTextCaseInsensitively_AndCanonicalizes()
    {
        InteractionGraderRegistry.Default.Resolve("OPENTEXT").ShouldBeOfType<OpenTextGrader>();
        InteractionGraderRegistry.Default.CanonicalType("OpenText").ShouldBe("openText");
    }
}
