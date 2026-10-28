using System.Text.Json;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #586: <see cref="LogicGridGrader"/> — kayıt kuralları (seçenek sayısı = konu sayısı, tekil id, birebir çözüm), payload
/// doğrulama, (konu, kategori) çifti başına kontrol (tek ✓ şartı, ✗ yok sayılır), çözüm sızıntısı yok, analiz, çözümsüz soru.
/// </summary>
public class LogicGridGraderTests
{
    // Ali, Ayşe, Can × Renk (mavi, kırmızı, yeşil) × Hayvan (kedi, köpek, kuş).
    private const string Plan =
        """{"version":1,"type":"logicGrid","subject":{"label":"Çocuk","items":[{"id":"p1","label":"Ali"},{"id":"p2","label":"Ayşe"},{"id":"p3","label":"Can"}]},"categories":[{"id":"c1","label":"Renk","items":[{"id":"o1","label":"Mavi"},{"id":"o2","label":"Kırmızı"},{"id":"o3","label":"Yeşil"}]},{"id":"c2","label":"Hayvan","items":[{"id":"o4","label":"Kedi"},{"id":"o5","label":"Köpek"},{"id":"o6","label":"Kuş"}]}],"clues":[{"id":"q1","text":"Ali maviyi sevmez."},{"id":"q2","text":"Kediyi seven kırmızıyı sever."}]}""";

    // Ali: yeşil, köpek; Ayşe: kırmızı, kedi; Can: mavi, kuş.
    private const string Solution =
        """{"assign":{"p1":{"c1":"o3","c2":"o5"},"p2":{"c1":"o2","c2":"o4"},"p3":{"c1":"o1","c2":"o6"}}}""";

    private static readonly LogicGridGrader Grader = new();

    private static string Cells(params (string Row, string Col, string Mark)[] cells)
        => JsonSerializer.Serialize(new { version = 1, type = "logicGrid", cells = cells.Select(c => new { row = c.Row, col = c.Col, mark = c.Mark }) });

    private static InteractionGradeInput Input(string? solution, string? payload)
        => new(Plan, solution, payload, SelectedAnswerId: null, CorrectAnswerId: null);

    private static List<(string Id, bool Correct)> Rows(string? feedback)
    {
        feedback.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(feedback!);
        return doc.RootElement.GetProperty("rows").EnumerateArray()
            .Select(r => (r.GetProperty("id").GetString()!, r.GetProperty("correct").GetBoolean())).ToList();
    }

    private static readonly (string, string, string)[] AllCorrect =
        [("p1", "o3", "yes"), ("p1", "o5", "yes"), ("p2", "o2", "yes"), ("p2", "o4", "yes"), ("p3", "o1", "yes"), ("p3", "o6", "yes")];

    // ---- Kayıt ----

    [Fact]
    public void ValidateDefinition_Pilot_Accepted()
    {
        Grader.ValidateDefinition(Plan, Solution).ShouldBeTrue();
        Grader.ValidateDefinition(Plan, null).ShouldBeTrue();
    }

    [Theory]
    // Aynı renk iki kişide.
    [InlineData("""{"assign":{"p1":{"c1":"o3","c2":"o5"},"p2":{"c1":"o3","c2":"o4"},"p3":{"c1":"o1","c2":"o6"}}}""")]
    // Kategori eksik.
    [InlineData("""{"assign":{"p1":{"c1":"o3"},"p2":{"c1":"o2","c2":"o4"},"p3":{"c1":"o1","c2":"o6"}}}""")]
    // Kişi eksik.
    [InlineData("""{"assign":{"p1":{"c1":"o3","c2":"o5"},"p2":{"c1":"o2","c2":"o4"}}}""")]
    // Seçenek başka kategoriden.
    [InlineData("""{"assign":{"p1":{"c1":"o4","c2":"o5"},"p2":{"c1":"o2","c2":"o3"},"p3":{"c1":"o1","c2":"o6"}}}""")]
    // Bilinmeyen kişi.
    [InlineData("""{"assign":{"p1":{"c1":"o3","c2":"o5"},"p2":{"c1":"o2","c2":"o4"},"p9":{"c1":"o1","c2":"o6"}}}""")]
    public void ValidateDefinition_BadSolution_Rejected(string solution)
        => Grader.ValidateDefinition(Plan, solution).ShouldBeFalse();

    [Theory]
    // Seçenek sayısı konu sayısından farklı.
    [InlineData("""{"version":1,"type":"logicGrid","subject":{"label":"K","items":[{"id":"p1","label":"A"},{"id":"p2","label":"B"}]},"categories":[{"id":"c1","label":"R","items":[{"id":"o1","label":"x"},{"id":"o2","label":"y"},{"id":"o3","label":"z"}]}]}""")]
    // Tekrarlanan id (seçenek kişiyle aynı).
    [InlineData("""{"version":1,"type":"logicGrid","subject":{"label":"K","items":[{"id":"p1","label":"A"},{"id":"p2","label":"B"}]},"categories":[{"id":"c1","label":"R","items":[{"id":"p1","label":"x"},{"id":"o2","label":"y"}]}]}""")]
    // Kategori yok.
    [InlineData("""{"version":1,"type":"logicGrid","subject":{"label":"K","items":[{"id":"p1","label":"A"},{"id":"p2","label":"B"}]},"categories":[]}""")]
    // Tek kişi.
    [InlineData("""{"version":1,"type":"logicGrid","subject":{"label":"K","items":[{"id":"p1","label":"A"}]},"categories":[{"id":"c1","label":"R","items":[{"id":"o1","label":"x"}]}]}""")]
    // Etiketsiz seçenek.
    [InlineData("""{"version":1,"type":"logicGrid","subject":{"label":"K","items":[{"id":"p1","label":"A"},{"id":"p2","label":"B"}]},"categories":[{"id":"c1","label":"R","items":[{"id":"o1","label":" "},{"id":"o2","label":"y"}]}]}""")]
    // İpucu metni yok.
    [InlineData("""{"version":1,"type":"logicGrid","subject":{"label":"K","items":[{"id":"p1","label":"A"},{"id":"p2","label":"B"}]},"categories":[{"id":"c1","label":"R","items":[{"id":"o1","label":"x"},{"id":"o2","label":"y"}]}],"clues":[{"id":"q1"}]}""")]
    // Bilinmeyen anahtar.
    [InlineData("""{"version":1,"type":"logicGrid","subject":{"label":"K","items":[{"id":"p1","label":"A"},{"id":"p2","label":"B"}]},"categories":[{"id":"c1","label":"R","items":[{"id":"o1","label":"x"},{"id":"o2","label":"y"}]}],"x":1}""")]
    public void ValidateDefinition_BadPlan_Rejected(string plan)
        => Grader.ValidateDefinition(plan, null).ShouldBeFalse();

    // ---- Payload ----

    [Fact]
    public void Validate_PayloadShape()
    {
        Grader.Validate(Plan, null).ShouldBeTrue();
        Grader.Validate(Plan, Cells(AllCorrect)).ShouldBeTrue();
        Grader.Validate(Plan, Cells(("p1", "o1", "no"))).ShouldBeTrue();
        Grader.Validate(Plan, Cells(("p9", "o1", "yes"))).ShouldBeFalse(); // bilinmeyen satır
        Grader.Validate(Plan, Cells(("p1", "o9", "yes"))).ShouldBeFalse(); // bilinmeyen sütun
        Grader.Validate(Plan, Cells(("p1", "o1", "maybe"))).ShouldBeFalse();
        Grader.Validate(Plan, Cells(("p1", "o1", "yes"), ("p1", "o1", "no"))).ShouldBeFalse(); // aynı hücre iki kez
    }

    [Fact]
    public void Normalize_NoMarksIsNoAnswer()
    {
        Grader.Normalize(Cells()).ShouldBeNull();
        var payload = Cells(("p1", "o1", "no"));
        Grader.Normalize(payload).ShouldBe(payload);
    }

    // ---- Kontrol ----

    [Fact]
    public void Grade_AllCorrect_CrossesIgnored()
    {
        var cells = AllCorrect.Concat([("p1", "o1", "no"), ("p1", "o2", "no")]).ToArray();
        var result = Grader.Grade(Input(Solution, Cells(cells)));
        result.IsCorrect.ShouldBeTrue();
        result.TotalCount.ShouldBe(6);
        Rows(result.Feedback).Select(r => r.Id).ShouldBe(["p1:c1", "p1:c2", "p2:c1", "p2:c2", "p3:c1", "p3:c2"]);
    }

    [Fact]
    public void Grade_PerPair_TwoTicksInOneCategoryIsWrong()
    {
        // Ali renkte iki ✓ (yanlış), hayvan doğru; Ayşe renk yanlış; diğerleri boş.
        var result = Grader.Grade(Input(Solution, Cells(("p1", "o3", "yes"), ("p1", "o1", "yes"), ("p1", "o5", "yes"), ("p2", "o1", "yes"))));
        result.CorrectCount.ShouldBe(1);
        Rows(result.Feedback).ShouldBe([("p1:c1", false), ("p1:c2", true), ("p2:c1", false), ("p2:c2", false), ("p3:c1", false), ("p3:c2", false)]);
    }

    [Fact]
    public void Grade_FeedbackDoesNotLeakSolution()
    {
        var feedback = Grader.Grade(Input(Solution, Cells(("p1", "o1", "yes")))).Feedback!;
        feedback.ShouldNotContain("o3");
        feedback.ShouldNotContain("assign");
    }

    [Fact]
    public void Grade_NoSolution_Ungradable()
    {
        Grader.Grade(Input(null, Cells(AllCorrect))).ShouldBe(InteractionGradeResult.Ungradable);
        Grader.Analyze(Input(null, Cells(AllCorrect))).ShouldBeEmpty();
    }

    // ---- Analiz ----

    [Fact]
    public void Analyze_ItemPerPair_WithChosenOption()
    {
        var items = Grader.Analyze(Input(Solution, Cells(("p1", "o1", "yes"), ("p1", "o5", "yes"))));
        items.Count.ShouldBe(6);
        items[0].ItemLabel.ShouldBe("Ali – Renk");
        items[0].Correct.ShouldBeFalse();
        items[0].AnswerLabel.ShouldBe("Mavi");
        items[1].Correct.ShouldBeTrue();
        items[2].AnswerKey.ShouldBeNull();
    }
}
