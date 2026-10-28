using System.Text.Json;
using ExamApp.Api.Services.Interactions;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #586 (dilim d): <see cref="SlotFillGrader"/> şifre katmanı — plan <c>cipher.cells</c> kuralları, çözümde <c>letter</c>
/// zorunluluğu ve geri bildirimde yalnız doğru cevaplanmış kutuların harflerinin açılması.
/// </summary>
public class SlotFillCipherTests
{
    private static readonly SlotFillGrader Grader = new();

    private const string Slots =
        """[{"id":"s1","label":"3 × 4","input":{"kind":"number","entry":"plain"}},{"id":"s2","label":"Başkent","input":{"kind":"text"}},{"id":"s3","label":"5 + 5","input":{"kind":"number","entry":"plain"}}]""";

    private static string Plan(string cells)
        => "{\"version\":1,\"type\":\"slotFill\",\"layout\":{\"kind\":\"list\"},\"slots\":" + Slots + ",\"cipher\":{\"cells\":" + cells + "}}";

    private const string Solution =
        """{"s1":{"value":"12","letter":"k"},"s2":{"accept":["Ankara"],"letter":"İ"},"s3":{"value":"10","letter":"R"}}""";

    private static string Payload(string values) => "{\"version\":1,\"type\":\"slotFill\",\"values\":" + values + "}";

    private static List<string?> Cipher(string? feedback)
    {
        using var doc = JsonDocument.Parse(feedback!);
        return doc.RootElement.GetProperty("cipher").EnumerateArray()
            .Select(c => c.ValueKind == JsonValueKind.Null ? null : c.GetString()).ToList();
    }

    [Fact]
    public void ValidateDefinition_CipherWithLetters_IsValid()
        => Grader.ValidateDefinition(Plan("""["s1","s2",null,"s3","s1"]"""), Solution).ShouldBeTrue();

    [Fact]
    public void ValidateDefinition_CipherPlanWithoutSolution_IsDraft()
        => Grader.ValidateDefinition(Plan("""["s1","s2"]"""), null).ShouldBeTrue();

    [Theory]
    [InlineData("""["s1"]""")]                    // tek harf hücresi
    [InlineData("""[null,"s1","s2"]""")]          // başta boşluk
    [InlineData("""["s1","s2",null]""")]          // sonda boşluk
    [InlineData("""["s1","x9"]""")]               // plan dışı kutu
    [InlineData("""["s1",3]""")]                  // id değil
    public void ValidateDefinition_InvalidCells_IsInvalid(string cells)
        => Grader.ValidateDefinition(Plan(cells), null).ShouldBeFalse();

    [Fact]
    public void ValidateDefinition_TooManyCells_IsInvalid()
    {
        var cells = "[" + string.Join(",", Enumerable.Repeat("\"s1\"", SlotFillGrader.MaxCipherCells + 1)) + "]";
        Grader.ValidateDefinition(Plan(cells), null).ShouldBeFalse();
    }

    [Fact]
    public void ValidateDefinition_UnknownCipherKey_IsInvalid()
        => Grader.ValidateDefinition(Plan("""["s1","s2"]""").Replace("\"cells\"", "\"x\":1,\"cells\""), null).ShouldBeFalse();

    [Theory]
    [InlineData("""{"s1":{"value":"12","letter":"K"},"s2":{"accept":["Ankara"]},"s3":{"value":"10","letter":"R"}}""")]     // şifredeki kutuda harf yok
    [InlineData("""{"s1":{"value":"12","letter":"KA"},"s2":{"accept":["Ankara"],"letter":"I"},"s3":{"value":"10","letter":"R"}}""")] // iki karakter
    [InlineData("""{"s1":{"value":"12","letter":"?"},"s2":{"accept":["Ankara"],"letter":"I"},"s3":{"value":"10","letter":"R"}}""")]  // harf değil
    public void ValidateDefinition_BadLetters_IsInvalid(string solution)
        => Grader.ValidateDefinition(Plan("""["s1","s2","s3"]"""), solution).ShouldBeFalse();

    [Fact]
    public void ValidateDefinition_LetterOutsideCipher_IsInvalid()
    {
        Grader.ValidateDefinition(Plan("""["s1","s3"]"""), Solution).ShouldBeFalse();
        var noCipher = "{\"version\":1,\"type\":\"slotFill\",\"layout\":{\"kind\":\"list\"},\"slots\":" + Slots + "}";
        Grader.ValidateDefinition(noCipher, Solution).ShouldBeFalse();
    }

    [Fact]
    public void Grade_RevealsOnlyLettersOfCorrectSlots()
    {
        var result = Grader.Grade(new InteractionGradeInput(Plan("""["s1","s2",null,"s3","s1"]"""), Solution,
            Payload("""{"s1":"12","s2":"İzmir"}"""), null, null));

        result.CorrectCount.ShouldBe(1);
        Cipher(result.Feedback).ShouldBe(["K", null, null, null, "K"]);
    }

    [Fact]
    public void Grade_AllCorrect_RevealsWholeWordInTurkishUpperCase()
    {
        var result = Grader.Grade(new InteractionGradeInput(Plan("""["s1","s2",null,"s3"]"""), Solution,
            Payload("""{"s1":"12","s2":"ankara","s3":"10"}"""), null, null));

        result.IsCorrect.ShouldBeTrue();
        Cipher(result.Feedback).ShouldBe(["K", "İ", null, "R"]);
    }

    [Fact]
    public void Grade_WithoutCipher_FeedbackHasNoCipher()
    {
        var plan = "{\"version\":1,\"type\":\"slotFill\",\"layout\":{\"kind\":\"list\"},\"slots\":" + Slots + "}";
        var result = Grader.Grade(new InteractionGradeInput(plan, """{"s1":{"value":"12"},"s2":{"accept":["Ankara"]},"s3":{"value":"10"}}""",
            Payload("""{"s1":"12"}"""), null, null));

        using var doc = JsonDocument.Parse(result.Feedback!);
        doc.RootElement.TryGetProperty("cipher", out _).ShouldBeFalse();
    }
}
