using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #575: <c>slotFill</c> etkinliği uçtan uca, üç pilotla (Süper Combo Sosyal s.76 dilsiz harita, Süper Combo Fen s.84
/// hücre 10+10 etiket, Yeni Nesil Matematik Ustası s.124 kesir işlemleri): her pilot kayıt yolundan geçen bir tanımla
/// oluşturulur, çözülür ve "Cevapları kontrol et" kutu bazlı sonuç verir (plan sırası, cevapsız kutu yanlış). Geçersiz cevap
/// 400 olup hiçbir şey yazmaz; çözüm kontrol/oynatıcı yanıtlarında gitmez (#570), yalnız "Doğru cevapları göster"den sonra döner.
/// </summary>
public class TestSessionServiceSlotFillActivityTests : IDisposable
{
    private const int OwnerUserId = 7501;

    private static string Box(int i) => "\"box\":{\"x\":0,\"y\":" + (i * 4) + ",\"width\":10,\"height\":3}";

    private static string PlanOf(IEnumerable<(string Id, string Input)> slots)
        => "{\"version\":1,\"type\":\"slotFill\",\"slots\":["
           + string.Join(",", slots.Select((s, i) => "{\"id\":\"" + s.Id + "\"," + Box(i) + ",\"input\":" + s.Input + "}"))
           + "]}";

    private static string Payload(IEnumerable<(string Id, string Value)> values)
        => JsonSerializer.Serialize(new { version = 1, type = "slotFill", values = values.ToDictionary(v => v.Id, v => v.Value) });

    // ---- Pilot 1: Sosyal s.76 dilsiz harita — il adı (metin), bölge (7 bölge seçimi), bölge adı (metin + isteğe bağlı ek).
    private static readonly string[] Regions = { "marmara", "ege", "akdeniz", "icanadolu", "karadeniz", "doguanadolu", "gdanadolu" };

    private static readonly string MapPlan = PlanOf(new[]
    {
        ("il1", """{"kind":"text"}"""),
        ("bolge1", "{\"kind\":\"select\",\"options\":[" + string.Join(",", Regions.Select(r => "{\"id\":\"REG-" + r + "\",\"label\":\"" + r + "\"}")) + "]}"),
        ("bolge2", """{"kind":"text"}"""),
    });

    private const string MapSolution =
        """{"il1":{"accept":["İzmir"]},"bolge1":{"optionIds":["REG-marmara"]},"bolge2":{"accept":["Doğu Anadolu"],"optionalSuffixes":["Bölgesi"]}}""";

    // ---- Pilot 2: Fen s.84 hücre — 10 hayvan + 10 bitki hücresi etiketi (metin), ilk etikette palet yok.
    private static readonly string[] Organelles =
        { "çekirdek", "hücre zarı", "sitoplazma", "mitokondri", "ribozom", "golgi cisimciği", "lizozom", "koful", "sentrozom", "endoplazmik retikulum" };

    private static readonly (string Id, string Accept)[] CellLabels =
        Organelles.Select((o, i) => ($"h{i + 1}", o))
            .Concat(Organelles.Select((o, i) => ($"b{i + 1}", i == 8 ? "kloroplast" : i == 6 ? "hücre çeperi" : o)))
            .ToArray();

    private static readonly string CellPlan = PlanOf(CellLabels.Select(c => (c.Id, """{"kind":"text"}""")));

    private static readonly string CellSolution =
        "{" + string.Join(",", CellLabels.Select(c => "\"" + c.Id + "\":{\"accept\":[\"" + c.Accept + "\"]}")) + "}";

    // ---- Pilot 3: Matematik s.124 kesir işlemleri — kesir (denklik açık), tam sayılı kesir (biçim + sadelik), birimli uzunluk.
    private static readonly string FractionPlan = PlanOf(new[]
    {
        ("k1", """{"kind":"number","entry":"fraction"}"""),
        ("k2", """{"kind":"number","entry":"mixed"}"""),
        ("k3", """{"kind":"number","entry":"plain","unit":"cm"}"""),
        ("k4", """{"kind":"palette","symbols":["<",">","="]}"""),
    });

    private const string FractionSolution =
        """{"k1":{"value":"3/4","equivalence":true},"k2":{"value":"2 3/5","equivalence":false,"requireSimplest":true},"k3":{"value":"12,5","units":["cm","santimetre"],"unitMode":"required"},"k4":{"accept":[">"]}}""";

    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private sealed record Seeded(int InstanceId, int MapRow, int CellRow, int FractionRow);

    private async Task<Seeded> SeedAsync(bool withSolutions = true)
    {
        // Pilotlar kayıt yolunda da geçerli tanımlar.
        var grader = new SlotFillGrader();
        grader.ValidateDefinition(MapPlan, MapSolution).ShouldBeTrue();
        grader.ValidateDefinition(CellPlan, CellSolution).ShouldBeTrue();
        grader.ValidateDefinition(FractionPlan, FractionSolution).ShouldBeTrue();

        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();

        var student = new Student { UserId = OwnerUserId, StudentNumber = "S", SchoolName = "Sch", GradeId = grade.Id };
        var worksheet = new Worksheet { Name = "Pilotlar", Description = "", GradeId = grade.Id, MaxDurationSeconds = 600, Kind = WorksheetKind.Activity };
        Question Q(string text, string plan, string solution) => new()
        {
            Text = text, Point = 10, InteractionType = SlotFillGrader.Type, InteractionPlan = plan,
            InteractionSolution = withSolutions ? solution : null,
        };
        var questions = new[]
        {
            Q("Dilsiz harita", MapPlan, MapSolution),
            Q("Hücre", CellPlan, CellSolution),
            Q("Kesirler", FractionPlan, FractionSolution),
        };
        ctx.AddRange(student, worksheet);
        ctx.Questions.AddRange(questions);
        await ctx.SaveChangesAsync();

        var wqs = questions.Select((q, i) => new WorksheetQuestion { TestId = worksheet.Id, QuestionId = q.Id, Order = i + 1 }).ToList();
        ctx.TestQuestions.AddRange(wqs);
        await ctx.SaveChangesAsync();

        var instance = new WorksheetInstance
        {
            StudentId = student.Id, WorksheetId = worksheet.Id, Status = WorksheetInstanceStatus.Started,
            StartTime = DateTime.UtcNow.AddMinutes(-5), MaxDurationSeconds = 600,
        };
        ctx.TestInstances.Add(instance);
        await ctx.SaveChangesAsync();

        var rows = wqs.Select(wq => new WorksheetInstanceQuestion { WorksheetInstanceId = instance.Id, WorksheetQuestionId = wq.Id }).ToList();
        ctx.TestInstanceQuestions.AddRange(rows);
        await ctx.SaveChangesAsync();
        return new Seeded(instance.Id, rows[0].Id, rows[1].Id, rows[2].Id);
    }

    private static ActivityCheckRequestDto Request(params (int RowId, string? Payload)[] answers) => new()
    {
        Answers = answers.Select(a => new ActivityCheckAnswerDto { InstanceQuestionId = a.RowId, AnswerPayload = a.Payload }).ToList(),
    };

    private async Task<ActivityCheckResultDto> CheckAsync(int instanceId, ActivityCheckRequestDto request)
    {
        await using var ctx = _db.NewContext();
        return await new TestSessionService(ctx).CheckActivityAsync(instanceId, request, OwnerUserId);
    }

    private async Task<WorksheetInstanceQuestion> RowAsync(int rowId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.TestInstanceQuestions.AsNoTracking().SingleAsync(x => x.Id == rowId);
    }

    private static List<(string Id, bool Correct)> FeedbackRows(string? feedback)
    {
        feedback.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(feedback!);
        return doc.RootElement.GetProperty("rows").EnumerateArray()
            .Select(r => (r.GetProperty("id").GetString()!, r.GetProperty("correct").GetBoolean()))
            .ToList();
    }

    // Öğrencinin tamamen doğru (ama normalleştirme gerektiren) cevapları.
    private static string MapCorrect => Payload(new[] { ("il1", "İZMİR"), ("bolge1", "REG-marmara"), ("bolge2", "doğu anadolu bölgesi.") });

    private static string CellCorrect => Payload(CellLabels.Select(c => (c.Id, "  " + c.Accept.ToUpper(new System.Globalization.CultureInfo("tr-TR")) + " ")));

    private static string FractionCorrect => Payload(new[] { ("k1", "%75"), ("k2", "2 tam 3/5"), ("k3", "12,5 CM"), ("k4", " > ") });

    [Fact]
    public void CellPilot_Has10Plus10Labels() => CellLabels.Length.ShouldBe(20);

    [Fact]
    public async Task CheckActivity_ThreePilotsFullyCorrect_AreCorrectAndStored()
    {
        var s = await SeedAsync();

        var result = await CheckAsync(s.InstanceId, Request((s.MapRow, MapCorrect), (s.CellRow, CellCorrect), (s.FractionRow, FractionCorrect)));

        result.Success.ShouldBeTrue(result.Message);
        result.Questions.Select(q => (q.InstanceQuestionId, q.IsCorrect, q.CorrectCount, q.TotalCount))
            .ShouldBe(new[] { (s.MapRow, true, 3, 3), (s.CellRow, true, 20, 20), (s.FractionRow, true, 4, 4) }, ignoreOrder: true);
        result.CorrectCount.ShouldBe(27);
        result.TotalCount.ShouldBe(27);
        var row = await RowAsync(s.FractionRow);
        row.AnswerPayload.ShouldBe(FractionCorrect);
        row.IsCorrect.ShouldBeTrue();
    }

    [Fact]
    public async Task CheckActivity_Partial_ReturnsSlotResultsInPlanOrderWithUnansweredWrong()
    {
        var s = await SeedAsync();
        // il1: ı/i hatası (IZMIR → ızmır); bolge1 doğru; bolge2 boş.
        var map = Payload(new[] { ("il1", "IZMIR"), ("bolge1", "REG-marmara"), ("bolge2", " ") });
        // k1 doğru (6/8 denk); k2 biçim farkı (13/5, equivalence kapalı); k3 birimsiz (zorunlu); k4 yok.
        var fraction = Payload(new[] { ("k1", "6/8"), ("k2", "13/5"), ("k3", "12,5") });

        var result = await CheckAsync(s.InstanceId, Request((s.MapRow, map), (s.FractionRow, fraction)));

        result.Success.ShouldBeTrue(result.Message);
        var mapResult = result.Questions.Single(q => q.InstanceQuestionId == s.MapRow);
        mapResult.IsCorrect.ShouldBeFalse();
        FeedbackRows(mapResult.Feedback).ShouldBe(new[] { ("il1", false), ("bolge1", true), ("bolge2", false) });
        var fractionResult = result.Questions.Single(q => q.InstanceQuestionId == s.FractionRow);
        FeedbackRows(fractionResult.Feedback).ShouldBe(new[] { ("k1", true), ("k2", false), ("k3", false), ("k4", false) });
    }

    [Fact]
    public async Task CheckActivity_ResponseSerialized_NeverContainsTheSolution()
    {
        var s = await SeedAsync();
        var map = Payload(new[] { ("bolge1", "REG-ege") });

        var result = await CheckAsync(s.InstanceId, Request((s.MapRow, map), (s.FractionRow, Payload(new[] { ("k1", "1/2") }))));

        var json = JsonSerializer.Serialize(result);
        foreach (var secret in new[] { "REG-marmara", "Doğu Anadolu", "Bölgesi", "İzmir", "3/4", "2 3/5", "santimetre", "accept", "optionIds" })
            json.ShouldNotContain(secret);
        json.ShouldNotContain("interactionSolution", Case.Insensitive);
    }

    [Theory]
    [InlineData("""{"version":1,"type":"slotFill","values":{"bolge1":"REG-mars"}}""")]   // plan dışı seçenek
    [InlineData("""{"version":1,"type":"slotFill","values":{"il9":"İzmir"}}""")]         // plan dışı kutu
    [InlineData("""{"version":1,"type":"slotFill","values":{"il1":42}}""")]              // string değil
    [InlineData("""{"version":1,"type":"matrixChoice","selections":{}}""")]              // başka tipin payload'u
    public async Task CheckActivity_InvalidPayload_IsInvalidInputAndWritesNothing(string payload)
    {
        var s = await SeedAsync();

        var result = await CheckAsync(s.InstanceId, Request((s.MapRow, payload)));

        result.Success.ShouldBeFalse();
        result.InvalidInput.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.InvalidAnswerPayload);
        var row = await RowAsync(s.MapRow);
        row.AnswerRevision.ShouldBe(0);
        row.AnswerPayload.ShouldBeNull();
    }

    [Fact]
    public async Task CheckActivity_AllBlankValues_IsStoredAsBlank()
    {
        var s = await SeedAsync();

        var result = await CheckAsync(s.InstanceId, Request((s.MapRow, Payload(new[] { ("il1", ""), ("bolge2", "  ") }))));

        result.Success.ShouldBeTrue();
        (await RowAsync(s.MapRow)).AnswerPayload.ShouldBeNull();
        result.Questions.Single().CorrectCount.ShouldBe(0);
        result.Questions.Single().TotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task CheckActivity_QuestionWithoutSolution_IsUngradable()
    {
        var s = await SeedAsync(withSolutions: false);

        var result = await CheckAsync(s.InstanceId, Request((s.MapRow, MapCorrect)));

        result.Success.ShouldBeTrue();
        var q = result.Questions.Single();
        q.IsCorrect.ShouldBeFalse();
        q.TotalCount.ShouldBe(0);
        q.Feedback.ShouldBeNull();
    }

    [Fact]
    public async Task PlayerView_BeforeAndAfterCheck_NeverCarriesTheSolution()
    {
        var s = await SeedAsync();

        async Task<WorksheetInstanceResultDto> LoadAsync()
        {
            await using var ctx = _db.NewContext();
            return (await new TestSessionService(ctx).GetCanvasTestResultAsync(s.InstanceId, OwnerUserId))!;
        }

        var before = (await LoadAsync()).TestInstanceQuestions.Single(x => x.Question.InteractionPlan == FractionPlan);
        before.Question.InteractionType.ShouldBe(SlotFillGrader.Type);
        before.Question.InteractionSolution.ShouldBeNull();

        (await CheckAsync(s.InstanceId, Request((s.FractionRow, Payload(new[] { ("k1", "3/4") }))))).Success.ShouldBeTrue();

        var after = (await LoadAsync()).TestInstanceQuestions.Single(x => x.Question.InteractionPlan == FractionPlan);
        after.Question.InteractionSolution.ShouldBeNull();
        FeedbackRows(after.Feedback).ShouldBe(new[] { ("k1", true), ("k2", false), ("k3", false), ("k4", false) });
        after.Feedback!.ShouldNotContain("2 3/5");
    }

    [Fact]
    public async Task GetActivitySolutions_AfterCheck_ReturnsTheSlotSolutions()
    {
        var s = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            var early = await new TestSessionService(ctx).GetActivitySolutionsAsync(s.InstanceId, OwnerUserId);
            early.Success.ShouldBeFalse();
            early.ErrorCode.ShouldBe(TestSessionErrorCodes.ActivityCheckRequired);
        }

        (await CheckAsync(s.InstanceId, Request((s.MapRow, MapCorrect)))).Success.ShouldBeTrue();

        await using var ctx2 = _db.NewContext();
        var result = await new TestSessionService(ctx2).GetActivitySolutionsAsync(s.InstanceId, OwnerUserId);
        result.Success.ShouldBeTrue();
        result.Questions.Single(q => q.InstanceQuestionId == s.MapRow).InteractionSolution.ShouldBe(MapSolution);
    }
}
