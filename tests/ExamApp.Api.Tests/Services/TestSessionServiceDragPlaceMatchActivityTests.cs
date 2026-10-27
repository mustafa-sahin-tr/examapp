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
/// issue #576: <c>dragPlace</c> ve <c>match</c> etkinlikleri uçtan uca, dört pilotla — Süper Combo Fen s.162 (12 olay metni → 6 hâl
/// değişimi kovası), Fen s.212 (16 atık görsel kırpması → 4 kutu), Yeni Nesil Sosyal Ustası s.115 (8 afet fotoğrafı ↔ 8 ad),
/// Fen s.127 (varlık ↔ gölge, 1 örnek çift). Her pilot kayıt yolundan geçen tanımla oluşturulur, çözülür, "Cevapları kontrol et"
/// yerleşim/eşleşme bazlı sonuç verir; yanlış kart işaretlenir ve geri alınıp yeniden denenince doğru olur. Aynı oturumdaki eski
/// <c>dragDropLabeling</c> sorusu değişmeden kontrol edilir (regresyon). Çözüm kontrol/oynatıcı yanıtlarında gitmez.
/// </summary>
public class TestSessionServiceDragPlaceMatchActivityTests : IDisposable
{
    private const int OwnerUserId = 7601;

    private static string Box(int i) => "{\"x\":" + (i % 4 * 20) + ",\"y\":" + (i / 4 * 20) + ",\"width\":18,\"height\":18}";

    // ---- Pilot 1: Fen s.162 #7 — 12 olay (metin) → 6 hâl değişimi (kutusuz kova, kapasite 4); her kovada 2 olay.
    private static readonly string[] Changes = { "erime", "donma", "buharlasma", "yogusma", "sublimlesme", "kiragilasma" };

    private static readonly string PhasePlan =
        "{\"version\":1,\"type\":\"dragPlace\",\"mode\":\"place\",\"reuse\":false,\"targets\":["
        + string.Join(",", Changes.Select(c => "{\"id\":\"HAL-" + c + "\",\"label\":\"" + c + "\",\"capacity\":4}"))
        + "],\"cards\":[" + string.Join(",", Enumerable.Range(1, 12).Select(i => "{\"id\":\"olay" + i + "\",\"label\":\"Olay " + i + "\"}")) + "]}";

    // olay(2k-1), olay(2k) → Changes[k-1].
    private static readonly (string Target, string Card)[] PhasePairs =
        Enumerable.Range(1, 12).Select(i => ("HAL-" + Changes[(i - 1) / 2], "olay" + i)).ToArray();

    private static readonly string PhaseSolution = SolutionOf(Changes.Select(c => "HAL-" + c), PhasePairs);

    // ---- Pilot 2: Fen s.212 — 16 atık görsel kırpması → 4 geri dönüşüm kutusu (görsel üstü bölge, kapasite 6); her kutuya 4.
    private static readonly string[] Bins = { "kagit", "plastik", "cam", "metal" };

    private static readonly string WastePlan =
        "{\"version\":1,\"type\":\"dragPlace\",\"targets\":["
        + string.Join(",", Bins.Select((b, i) => "{\"id\":\"KUTU-" + b + "\",\"label\":\"" + b + "\",\"box\":" + Box(16 + i) + ",\"capacity\":6}"))
        + "],\"cards\":[" + string.Join(",", Enumerable.Range(1, 16).Select(i => "{\"id\":\"atik" + i + "\",\"box\":" + Box(i - 1) + "}")) + "]}";

    private static readonly (string Target, string Card)[] WastePairs =
        Enumerable.Range(1, 16).Select(i => ("KUTU-" + Bins[(i - 1) % 4], "atik" + i)).ToArray();

    private static readonly string WasteSolution = SolutionOf(Bins.Select(b => "KUTU-" + b), WastePairs);

    // ---- Pilot 3: Sosyal Ustası s.115 — 8 afet fotoğrafı (kırpma) ↔ 8 ad; çözüm karışık sıralı.
    private static readonly string[] Disasters = { "deprem", "sel", "heyelan", "cig", "kasirga", "yangin", "tsunami", "kuraklik" };

    private static readonly string DisasterPlan =
        "{\"version\":1,\"type\":\"match\",\"left\":["
        + string.Join(",", Enumerable.Range(1, 8).Select(i => "{\"id\":\"foto" + i + "\",\"box\":" + Box(i - 1) + "}"))
        + "],\"right\":[" + string.Join(",", Disasters.Select(d => "{\"id\":\"AD-" + d + "\",\"label\":\"" + d + "\"}")) + "]}";

    // foto i ↔ Disasters[(i*3) % 8] (karışık).
    private static readonly (string Left, string Right)[] DisasterPairs =
        Enumerable.Range(1, 8).Select(i => ("foto" + i, "AD-" + Disasters[i * 3 % 8])).ToArray();

    private static readonly string DisasterSolution =
        "{" + string.Join(",", DisasterPairs.Select(p => "\"" + p.Left + "\":\"" + p.Right + "\"")) + "}";

    // ---- Pilot 4: Fen s.127 — 5 varlık ↔ 6 gölge (1 çeldirici), varlik1–golge1 örnek çift (bağlı gösterilir, puanlanmaz).
    private static readonly string ShadowPlan =
        "{\"version\":1,\"type\":\"match\",\"left\":["
        + string.Join(",", Enumerable.Range(1, 5).Select(i => "{\"id\":\"varlik" + i + "\",\"box\":" + Box(i - 1) + "}"))
        + "],\"right\":[" + string.Join(",", Enumerable.Range(1, 6).Select(i => "{\"id\":\"GOLGE-" + i + "\",\"box\":" + Box(8 + i) + "}"))
        + "],\"prelinked\":[{\"leftId\":\"varlik1\",\"rightId\":\"GOLGE-1\"}]}";

    private const string ShadowSolution = """{"varlik2":"GOLGE-4","varlik3":"GOLGE-2","varlik4":"GOLGE-5","varlik5":"GOLGE-3"}""";

    // ---- Regresyon: mevcut dragDropLabeling (#571) sorusu.
    private const string LegacyPlan =
        """{"version":1,"type":"dragDropLabeling","dropZones":[{"id":"zone-a"},{"id":"zone-b"}],"draggables":[{"id":"card-1","label":"Bir"},{"id":"card-2","label":"İki"},{"id":"SECRET-CARD-99","label":"Üç"}]}""";

    private const string LegacySolution =
        """{"placements":[{"draggableId":"card-1","dropZoneId":"zone-a"},{"draggableId":"card-2","dropZoneId":"zone-b"}]}""";

    private static string SolutionOf(IEnumerable<string> targets, IEnumerable<(string Target, string Card)> pairs)
    {
        var list = pairs.ToList();
        return "{" + string.Join(",", targets.Select(t => "\"" + t + "\":[" + string.Join(",", list.Where(p => p.Target == t).Select(p => "\"" + p.Card + "\"")) + "]")) + "}";
    }

    private static string Place(IEnumerable<(string Target, string Card)> placements) => JsonSerializer.Serialize(new
    {
        version = 1, type = "dragPlace", placements = placements.Select(p => new { targetId = p.Target, cardId = p.Card }),
    });

    private static string Match(IEnumerable<(string Left, string Right)> pairs) => JsonSerializer.Serialize(new
    {
        version = 1, type = "match", pairs = pairs.Select(p => new { leftId = p.Left, rightId = p.Right }),
    });

    private static string Legacy(params (string Zone, string Card)[] placements) => JsonSerializer.Serialize(new
    {
        version = 1, type = "dragDropLabeling", placements = placements.Select(p => new { dropZoneId = p.Zone, draggableId = p.Card }),
    });

    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private sealed record Seeded(int InstanceId, int PhaseRow, int WasteRow, int DisasterRow, int ShadowRow, int LegacyRow);

    private async Task<Seeded> SeedAsync(bool withSolutions = true)
    {
        // Pilotlar kayıt yolunda da geçerli tanımlar.
        new DragPlaceGrader().ValidateDefinition(PhasePlan, PhaseSolution).ShouldBeTrue();
        new DragPlaceGrader().ValidateDefinition(WastePlan, WasteSolution).ShouldBeTrue();
        new MatchGrader().ValidateDefinition(DisasterPlan, DisasterSolution).ShouldBeTrue();
        new MatchGrader().ValidateDefinition(ShadowPlan, ShadowSolution).ShouldBeTrue();

        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "6" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();

        var student = new Student { UserId = OwnerUserId, StudentNumber = "S", SchoolName = "Sch", GradeId = grade.Id };
        var worksheet = new Worksheet { Name = "Pilotlar", Description = "", GradeId = grade.Id, MaxDurationSeconds = 600, Kind = WorksheetKind.Activity };
        Question Q(string text, string type, string plan, string solution) => new()
        {
            Text = text, Point = 10, InteractionType = type, InteractionPlan = plan,
            InteractionSolution = withSolutions ? solution : null,
        };
        var questions = new[]
        {
            Q("Hâl değişimleri", DragPlaceGrader.Type, PhasePlan, PhaseSolution),
            Q("Atıklar", DragPlaceGrader.Type, WastePlan, WasteSolution),
            Q("Afetler", MatchGrader.Type, DisasterPlan, DisasterSolution),
            Q("Gölgeler", MatchGrader.Type, ShadowPlan, ShadowSolution),
            Q("Eski etiketleme", DragDropLabelingGrader.Type, LegacyPlan, LegacySolution),
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
        return new Seeded(instance.Id, rows[0].Id, rows[1].Id, rows[2].Id, rows[3].Id, rows[4].Id);
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

    // Öğrencinin tamamen doğru cevapları (sıra karışık: sıra önemsiz).
    private static string PhaseCorrect => Place(PhasePairs.Reverse());
    private static string WasteCorrect => Place(WastePairs.OrderBy(p => p.Card, StringComparer.Ordinal));
    private static string DisasterCorrect => Match(DisasterPairs.Reverse());
    private static string ShadowCorrect => Match(new[] { ("varlik5", "GOLGE-3"), ("varlik2", "GOLGE-4"), ("varlik3", "GOLGE-2"), ("varlik4", "GOLGE-5") });

    [Fact]
    public void Pilots_HaveTheExpectedShape()
    {
        PhasePairs.Length.ShouldBe(12);
        PhasePairs.Select(p => p.Target).Distinct().Count().ShouldBe(6);
        WastePairs.Length.ShouldBe(16);
        WastePairs.Select(p => p.Target).Distinct().Count().ShouldBe(4);
        DisasterPairs.Select(p => p.Right).Distinct().Count().ShouldBe(8);
        WastePlan.ShouldNotContain("\"label\":\"Atık");   // kartlar yalnız görsel kırpması
    }

    [Fact]
    public async Task CheckActivity_FourPilotsFullyCorrect_AreCorrectAndStored()
    {
        var s = await SeedAsync();

        var result = await CheckAsync(s.InstanceId, Request(
            (s.PhaseRow, PhaseCorrect), (s.WasteRow, WasteCorrect), (s.DisasterRow, DisasterCorrect), (s.ShadowRow, ShadowCorrect)));

        result.Success.ShouldBeTrue(result.Message);
        result.Questions.Select(q => (q.InstanceQuestionId, q.IsCorrect, q.CorrectCount, q.TotalCount))
            .ShouldBe(new[]
            {
                (s.PhaseRow, true, 12, 12), (s.WasteRow, true, 16, 16), (s.DisasterRow, true, 8, 8), (s.ShadowRow, true, 4, 4),
            }, ignoreOrder: true);
        var row = await RowAsync(s.WasteRow);
        row.AnswerPayload.ShouldBe(WasteCorrect);
        row.IsCorrect.ShouldBeTrue();
        // Örnek çift satır üretmez.
        FeedbackRows(result.Questions.Single(q => q.InstanceQuestionId == s.ShadowRow).Feedback)
            .Select(r => r.Id).ShouldBe(new[] { "varlik2", "varlik3", "varlik4", "varlik5" });
    }

    [Fact]
    public async Task CheckActivity_WrongCard_IsMarkedThenRetriedFromPoolAndBecomesCorrect()
    {
        var s = await SeedAsync();
        // olay1 (erime) donma kovasına; erime kovası eksik kalır.
        var wrong = Place(PhasePairs.Where(p => p.Card != "olay1").Append(("HAL-donma", "olay1")));

        var first = await CheckAsync(s.InstanceId, Request((s.PhaseRow, wrong)));

        first.Success.ShouldBeTrue(first.Message);
        var q = first.Questions.Single();
        q.IsCorrect.ShouldBeFalse();
        q.CorrectCount.ShouldBe(11);
        q.TotalCount.ShouldBe(12);
        var rows = FeedbackRows(q.Feedback);
        rows.Where(r => !r.Correct).ShouldBe(new[] { ("HAL-erime:", false), ("HAL-donma:olay1", false) });
        rows.Count(r => r.Correct).ShouldBe(11);

        // Yanlış kart havuza geri alınıp doğru kovaya konur.
        var retry = await CheckAsync(s.InstanceId, Request((s.PhaseRow, PhaseCorrect)));

        retry.Success.ShouldBeTrue(retry.Message);
        retry.Questions.Single().IsCorrect.ShouldBeTrue();
        (await RowAsync(s.PhaseRow)).IsCorrect.ShouldBeTrue();
    }

    [Fact]
    public async Task CheckActivity_Partial_MatchRowsInPlanOrderWithUnpairedWrong()
    {
        var s = await SeedAsync();
        // varlik2 doğru, varlik3 çeldirici gölgeye, varlik4/5 bağsız.
        var shadow = Match(new[] { ("varlik2", "GOLGE-4"), ("varlik3", "GOLGE-6") });
        // foto1 ↔ foto2'nin adı (yer değiştirmiş iki fotoğraf).
        var swapped = DisasterPairs.Select(p => p.Left switch
        {
            "foto1" => ("foto1", DisasterPairs[1].Right),
            "foto2" => ("foto2", DisasterPairs[0].Right),
            _ => p,
        });

        var result = await CheckAsync(s.InstanceId, Request((s.ShadowRow, shadow), (s.DisasterRow, Match(swapped))));

        result.Success.ShouldBeTrue(result.Message);
        FeedbackRows(result.Questions.Single(q => q.InstanceQuestionId == s.ShadowRow).Feedback)
            .ShouldBe(new[] { ("varlik2", true), ("varlik3", false), ("varlik4", false), ("varlik5", false) });
        var disaster = result.Questions.Single(q => q.InstanceQuestionId == s.DisasterRow);
        disaster.CorrectCount.ShouldBe(6);
        disaster.TotalCount.ShouldBe(8);
        FeedbackRows(disaster.Feedback).Take(2).ShouldBe(new[] { ("foto1", false), ("foto2", false) });
    }

    [Fact]
    public async Task CheckActivity_ResponseSerialized_NeverContainsTheSolution()
    {
        var s = await SeedAsync();
        // Yalnız birer yerleşim / eşleşme: beklenen diğer kartlar ve sağlar yanıtta geçmemeli.
        var result = await CheckAsync(s.InstanceId, Request(
            (s.WasteRow, Place(new[] { ("KUTU-kagit", "atik1") })),
            (s.ShadowRow, Match(new[] { ("varlik2", "GOLGE-6") }))));

        result.Success.ShouldBeTrue(result.Message);
        var json = JsonSerializer.Serialize(result);
        foreach (var card in WastePairs.Where(p => p.Card != "atik1").Select(p => p.Card))
            json.ShouldNotContain("\"KUTU-" + card);
        json.ShouldNotContain(":atik5");      // kağıt kutusunun beklenen diğer kartı
        foreach (var secret in new[] { "GOLGE-2", "GOLGE-3", "GOLGE-4", "GOLGE-5", "GOLGE-6" })
            json.ShouldNotContain(secret);
        json.ShouldNotContain("interactionSolution", Case.Insensitive);
    }

    [Theory]
    [InlineData(0, """{"version":1,"type":"dragPlace","placements":[{"targetId":"HAL-erime","cardId":"olay1"},{"targetId":"HAL-donma","cardId":"olay1"}]}""")] // kart iki kez
    [InlineData(0, """{"version":1,"type":"dragPlace","placements":[{"targetId":"HAL-yok","cardId":"olay1"}]}""")]                                              // plan dışı hedef
    [InlineData(1, """{"version":1,"type":"dragPlace","placements":[{"targetId":"KUTU-cam","cardId":"atik1"},{"targetId":"KUTU-cam","cardId":"atik2"},{"targetId":"KUTU-cam","cardId":"atik3"},{"targetId":"KUTU-cam","cardId":"atik4"},{"targetId":"KUTU-cam","cardId":"atik5"},{"targetId":"KUTU-cam","cardId":"atik6"},{"targetId":"KUTU-cam","cardId":"atik7"}]}""")] // kapasite 6
    [InlineData(3, """{"version":1,"type":"match","pairs":[{"leftId":"varlik1","rightId":"GOLGE-2"}]}""")]                                                      // örnek sol
    [InlineData(2, """{"version":1,"type":"match","pairs":[{"leftId":"foto1","rightId":"AD-sel"},{"leftId":"foto2","rightId":"AD-sel"}]}""")]                  // sağ iki kez
    [InlineData(2, """{"version":1,"type":"dragPlace","placements":[]}""")]                                                                                     // başka tipin payload'u
    public async Task CheckActivity_InvalidPayload_IsInvalidInputAndWritesNothing(int question, string payload)
    {
        var s = await SeedAsync();
        var rowId = new[] { s.PhaseRow, s.WasteRow, s.DisasterRow, s.ShadowRow }[question];

        var result = await CheckAsync(s.InstanceId, Request((rowId, payload)));

        result.Success.ShouldBeFalse();
        result.InvalidInput.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.InvalidAnswerPayload);
        var row = await RowAsync(rowId);
        row.AnswerRevision.ShouldBe(0);
        row.AnswerPayload.ShouldBeNull();
    }

    [Fact]
    public async Task CheckActivity_EmptyPlacementsAndPairs_AreStoredAsBlank()
    {
        var s = await SeedAsync();

        var result = await CheckAsync(s.InstanceId, Request(
            (s.PhaseRow, Place(Array.Empty<(string, string)>())), (s.ShadowRow, Match(Array.Empty<(string, string)>()))));

        result.Success.ShouldBeTrue(result.Message);
        (await RowAsync(s.PhaseRow)).AnswerPayload.ShouldBeNull();
        (await RowAsync(s.ShadowRow)).AnswerPayload.ShouldBeNull();
        result.Questions.Single(q => q.InstanceQuestionId == s.PhaseRow).TotalCount.ShouldBe(12);
        result.Questions.Single(q => q.InstanceQuestionId == s.ShadowRow).TotalCount.ShouldBe(4);
    }

    [Fact]
    public async Task CheckActivity_QuestionsWithoutSolution_AreUngradable()
    {
        var s = await SeedAsync(withSolutions: false);

        var result = await CheckAsync(s.InstanceId, Request((s.PhaseRow, PhaseCorrect), (s.DisasterRow, DisasterCorrect)));

        result.Success.ShouldBeTrue(result.Message);
        result.Questions.ShouldAllBe(q => !q.IsCorrect && q.TotalCount == 0 && q.Feedback == null);
    }

    [Fact]
    public async Task CheckActivity_LegacyDragDropLabeling_StillChecksIdentically()
    {
        var s = await SeedAsync();
        var partial = Legacy(("zone-a", "card-1"), ("zone-b", "SECRET-CARD-99"));
        var expected = new DragDropLabelingGrader().Grade(new InteractionGradeInput(LegacyPlan, LegacySolution, partial, null, null));

        var result = await CheckAsync(s.InstanceId, Request((s.LegacyRow, partial), (s.PhaseRow, PhaseCorrect)));

        result.Success.ShouldBeTrue(result.Message);
        var legacy = result.Questions.Single(q => q.InstanceQuestionId == s.LegacyRow);
        (legacy.IsCorrect, legacy.CorrectCount, legacy.TotalCount, legacy.Feedback)
            .ShouldBe((expected.IsCorrect, expected.CorrectCount, expected.TotalCount, expected.Feedback));
        FeedbackRows(legacy.Feedback).ShouldBe(new[] { ("zone-a", true), ("zone-b", false) });

        var full = await CheckAsync(s.InstanceId, Request((s.LegacyRow, Legacy(("zone-a", "card-1"), ("zone-b", "card-2")))));
        full.Questions.Single().IsCorrect.ShouldBeTrue();
        full.Questions.Single().TotalCount.ShouldBe(2);

        // Eski payload biçimi yeni tiplerin kuralına takılmaz; yeni payload eski soruda reddedilir.
        var cross = await CheckAsync(s.InstanceId, Request((s.LegacyRow, Place(new[] { ("zone-a", "card-1") }))));
        cross.InvalidInput.ShouldBeTrue();
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

        var before = await LoadAsync();
        foreach (var q in before.TestInstanceQuestions)
            q.Question.InteractionSolution.ShouldBeNull();
        before.TestInstanceQuestions.Single(x => x.Question.InteractionPlan == ShadowPlan).Question.InteractionType.ShouldBe(MatchGrader.Type);

        (await CheckAsync(s.InstanceId, Request((s.ShadowRow, Match(new[] { ("varlik2", "GOLGE-4") }))))).Success.ShouldBeTrue();

        var after = (await LoadAsync()).TestInstanceQuestions.Single(x => x.Question.InteractionPlan == ShadowPlan);
        after.Question.InteractionSolution.ShouldBeNull();
        FeedbackRows(after.Feedback).ShouldBe(new[] { ("varlik2", true), ("varlik3", false), ("varlik4", false), ("varlik5", false) });
        after.Feedback!.ShouldNotContain("GOLGE");
    }

    [Fact]
    public async Task GetActivitySolutions_AfterCheck_ReturnsTheSolutions()
    {
        var s = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            var early = await new TestSessionService(ctx).GetActivitySolutionsAsync(s.InstanceId, OwnerUserId);
            early.Success.ShouldBeFalse();
            early.ErrorCode.ShouldBe(TestSessionErrorCodes.ActivityCheckRequired);
        }

        (await CheckAsync(s.InstanceId, Request((s.PhaseRow, PhaseCorrect)))).Success.ShouldBeTrue();

        await using var ctx2 = _db.NewContext();
        var result = await new TestSessionService(ctx2).GetActivitySolutionsAsync(s.InstanceId, OwnerUserId);
        result.Success.ShouldBeTrue();
        result.Questions.Single(q => q.InstanceQuestionId == s.PhaseRow).InteractionSolution.ShouldBe(PhaseSolution);
        result.Questions.Single(q => q.InstanceQuestionId == s.ShadowRow).InteractionSolution.ShouldBe(ShadowSolution);
    }
}
