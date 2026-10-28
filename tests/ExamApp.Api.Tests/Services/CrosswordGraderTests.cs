using System.Text.Json;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #586: <see cref="CrosswordGrader"/> — kayıt kuralları (sınırlar, aynı yönde çakışma / uç uca değme, kesişen harf tutarlılığı),
/// payload doğrulama (boyut, siyah hücre), tr-TR büyük/küçük harf, ipucu başına kontrol, çözüm sızıntısı yok, analiz, çözümsüz soru.
/// </summary>
public class CrosswordGraderTests
{
    // 3×4: k1 → (0,0) KALP; k2 ↓ (0,0) KİR; k3 → (2,0) RAF. (0,0) ve (2,0) kesişir.
    //   K A L P
    //   İ . . .
    //   R A F .
    private const string Plan =
        """{"version":1,"type":"crossword","rows":3,"cols":4,"clues":[{"id":"k1","dir":"across","row":0,"col":0,"length":4,"number":1,"text":"Kanı pompalayan organ"},{"id":"k2","dir":"down","row":0,"col":0,"length":3,"number":1,"text":"Temizliğin zıttı"},{"id":"k3","dir":"across","row":2,"col":0,"length":3,"number":2,"text":"Kitap konan raf"}]}""";

    private const string Solution = """{"answers":{"k1":"KALP","k2":"KİR","k3":"RAF"}}""";

    private static readonly CrosswordGrader Grader = new();

    private static string Grid(params string[] lines) => JsonSerializer.Serialize(new { version = 1, type = "crossword", grid = lines });

    private static InteractionGradeInput Input(string? solution, string? payload)
        => new(Plan, solution, payload, SelectedAnswerId: null, CorrectAnswerId: null);

    private static List<(string Id, bool Correct)> Rows(string? feedback)
    {
        feedback.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(feedback!);
        return doc.RootElement.GetProperty("rows").EnumerateArray()
            .Select(r => (r.GetProperty("id").GetString()!, r.GetProperty("correct").GetBoolean())).ToList();
    }

    private static string WithClues(string clues) =>
        $$"""{"version":1,"type":"crossword","rows":4,"cols":4,"clues":[{{clues}}]}""";

    // ---- Kayıt ----

    [Fact]
    public void ValidateDefinition_Pilot_AcceptedWithAndWithoutSolution()
    {
        Grader.ValidateDefinition(Plan, Solution).ShouldBeTrue();
        Grader.ValidateDefinition(Plan, null).ShouldBeTrue(); // taslak
    }

    [Theory]
    // Ortak hücrede farklı harf (k2 K yerine M ile başlıyor).
    [InlineData("""{"answers":{"k1":"KALP","k2":"MİR","k3":"RAF"}}""")]
    // Uzunluk tutmuyor.
    [InlineData("""{"answers":{"k1":"KAL","k2":"KİR","k3":"RAF"}}""")]
    // Eksik cevap.
    [InlineData("""{"answers":{"k1":"KALP","k2":"KİR"}}""")]
    // Bilinmeyen ipucu.
    [InlineData("""{"answers":{"k1":"KALP","k2":"KİR","k3":"RAF","k9":"XX"}}""")]
    // Harf/rakam dışı karakter.
    [InlineData("""{"answers":{"k1":"KA P","k2":"KİR","k3":"RAF"}}""")]
    // Bilinmeyen anahtar.
    [InlineData("""{"answers":{"k1":"KALP","k2":"KİR","k3":"RAF"},"extra":1}""")]
    public void ValidateDefinition_BadSolution_Rejected(string solution)
        => Grader.ValidateDefinition(Plan, solution).ShouldBeFalse();

    [Fact]
    public void ValidateDefinition_CrossingLettersCompareCaseInsensitively()
        => Grader.ValidateDefinition(Plan, """{"answers":{"k1":"kalp","k2":"KİR","k3":"raf"}}""").ShouldBeTrue();

    [Theory]
    // Aynı yönde hücre paylaşan iki ipucu.
    [InlineData("""{"id":"a","dir":"across","row":0,"col":0,"length":3,"text":"x"},{"id":"b","dir":"across","row":0,"col":2,"length":2,"text":"y"}""")]
    // Aynı yönde uç uca değen iki ipucu (tek kelime gibi görünür).
    [InlineData("""{"id":"a","dir":"across","row":0,"col":0,"length":2,"text":"x"},{"id":"b","dir":"across","row":0,"col":2,"length":2,"text":"y"}""")]
    // Izgaradan taşan.
    [InlineData("""{"id":"a","dir":"down","row":2,"col":0,"length":3,"text":"x"}""")]
    // Tek harflik ipucu.
    [InlineData("""{"id":"a","dir":"across","row":0,"col":0,"length":1,"text":"x"}""")]
    // İpucu metni yok.
    [InlineData("""{"id":"a","dir":"across","row":0,"col":0,"length":2}""")]
    // Bilinmeyen yön.
    [InlineData("""{"id":"a","dir":"diagonal","row":0,"col":0,"length":2,"text":"x"}""")]
    // Aynı başlangıçta farklı numara.
    [InlineData("""{"id":"a","dir":"across","row":0,"col":0,"length":2,"number":1,"text":"x"},{"id":"b","dir":"down","row":0,"col":0,"length":2,"number":2,"text":"y"}""")]
    // Yinelenen id.
    [InlineData("""{"id":"a","dir":"across","row":0,"col":0,"length":2,"text":"x"},{"id":"a","dir":"down","row":1,"col":3,"length":2,"text":"y"}""")]
    // Denetim karakteri.
    [InlineData("""{"id":"a","dir":"across","row":0,"col":0,"length":2,"text":"x‮y"}""")]
    // Bilinmeyen anahtar.
    [InlineData("""{"id":"a","dir":"across","row":0,"col":0,"length":2,"text":"x","answer":"AB"}""")]
    public void ValidateDefinition_BadStructure_Rejected(string clues)
        => Grader.ValidateDefinition(WithClues(clues), null).ShouldBeFalse();

    [Fact]
    public void ValidateDefinition_SizeAndClueCountLimits()
    {
        Grader.ValidateDefinition("""{"version":1,"type":"crossword","rows":4,"cols":4,"clues":[]}""", null).ShouldBeFalse();
        Grader.ValidateDefinition(
            """{"version":1,"type":"crossword","rows":21,"cols":4,"clues":[{"id":"a","dir":"across","row":0,"col":0,"length":2,"text":"x"}]}""",
            null).ShouldBeFalse();
        Grader.ValidateDefinition(WithClues("""{"id":"a","dir":"across","row":0,"col":0,"length":2,"text":"x"},{"id":"b","dir":"across","row":0,"col":3,"length":1,"text":"y"}"""), null)
            .ShouldBeFalse();
        // Aynı yönde araya bir hücre girince geçerli.
        Grader.ValidateDefinition(WithClues("""{"id":"a","dir":"across","row":0,"col":0,"length":2,"text":"x"},{"id":"b","dir":"down","row":0,"col":3,"length":2,"text":"y"}"""), null)
            .ShouldBeTrue();
    }

    [Fact]
    public void PrepareForStorage_UppercasesAnswersTurkish()
    {
        var (_, solution) = Grader.PrepareForStorage(Plan, """{"answers":{"k1":"kalp","k2":"kir","k3":"raf"}}""");
        using var doc = JsonDocument.Parse(solution!);
        doc.RootElement.GetProperty("answers").GetProperty("k2").GetString().ShouldBe("KİR");
        Grader.ValidateDefinition(Plan, solution).ShouldBeTrue();
    }

    // ---- Payload ----

    [Fact]
    public void Validate_PayloadShape()
    {
        Grader.Validate(Plan, null).ShouldBeTrue();
        Grader.Validate(Plan, Grid("KALP", "İ   ", "RAF ")).ShouldBeTrue();
        Grader.Validate(Plan, Grid("    ", "    ", "    ")).ShouldBeTrue();
        // Siyah hücrede harf.
        Grader.Validate(Plan, Grid("KALP", "İX  ", "RAF ")).ShouldBeFalse();
        // Satır sayısı / genişlik tutmuyor.
        Grader.Validate(Plan, Grid("KALP", "İ   ")).ShouldBeFalse();
        Grader.Validate(Plan, Grid("KALP", "İ ", "RAF ")).ShouldBeFalse();
        // Harf/rakam dışı karakter.
        Grader.Validate(Plan, Grid("KAL-", "İ   ", "RAF ")).ShouldBeFalse();
        // Başka tür / bilinmeyen anahtar.
        Grader.Validate(Plan, """{"version":1,"type":"mazeTree","grid":[]}""").ShouldBeFalse();
        Grader.Validate(Plan, """{"version":1,"type":"crossword","grid":["KALP","İ   ","RAF "],"x":1}""").ShouldBeFalse();
    }

    [Fact]
    public void Normalize_BlankGridIsNoAnswer()
    {
        Grader.Normalize(Grid("    ", "    ", "    ")).ShouldBeNull();
        var payload = Grid("K   ", "    ", "    ");
        Grader.Normalize(payload).ShouldBe(payload);
    }

    // ---- Kontrol ----

    [Fact]
    public void Grade_AllCorrect_LowercaseTurkishAccepted()
    {
        var result = Grader.Grade(Input(Solution, Grid("kalp", "i   ", "raf ")));
        result.IsCorrect.ShouldBeTrue();
        result.CorrectCount.ShouldBe(3);
        result.TotalCount.ShouldBe(3);
        Rows(result.Feedback).ShouldAllBe(r => r.Correct);
    }

    [Fact]
    public void Grade_PerClue()
    {
        // k2 yanlış (KIR: noktasız I), k3 eksik.
        var result = Grader.Grade(Input(Solution, Grid("KALP", "I   ", "RA  ")));
        result.IsCorrect.ShouldBeFalse();
        result.CorrectCount.ShouldBe(1);
        result.TotalCount.ShouldBe(3);
        Rows(result.Feedback).ShouldBe([("k1", true), ("k2", false), ("k3", false)]);
    }

    [Fact]
    public void Grade_EmptyAnswer_AllWrong()
    {
        var result = Grader.Grade(Input(Solution, null));
        result.CorrectCount.ShouldBe(0);
        result.TotalCount.ShouldBe(3);
    }

    [Fact]
    public void Grade_FeedbackDoesNotLeakLetters()
    {
        var feedback = Grader.Grade(Input(Solution, Grid("K   ", "    ", "    "))).Feedback!;
        feedback.ShouldNotContain("KALP");
        feedback.ShouldNotContain("RAF");
        feedback.ShouldNotContain("answers");
    }

    [Fact]
    public void Grade_NoSolution_Ungradable()
    {
        Grader.Grade(Input(null, Grid("KALP", "İ   ", "RAF "))).ShouldBe(InteractionGradeResult.Ungradable);
        Grader.Analyze(Input(null, Grid("KALP", "İ   ", "RAF "))).ShouldBeEmpty();
    }

    // ---- Analiz ----

    [Fact]
    public void Analyze_ItemPerClue_WithStudentWord()
    {
        var items = Grader.Analyze(Input(Solution, Grid("KALP", "I   ", "    ")));
        items.Select(i => i.ItemId).ShouldBe(["k1", "k2", "k3"]);
        items[0].Correct.ShouldBeTrue();
        items[0].ItemLabel.ShouldStartWith("1 →");
        items[1].Correct.ShouldBeFalse();
        items[1].AnswerLabel.ShouldBe("KI·");
        items[2].AnswerKey.ShouldBeNull();
    }
}
