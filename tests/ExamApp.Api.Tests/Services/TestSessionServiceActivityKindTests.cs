using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using ExamApp.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #573: etkinlik oturumu — (1) ilk kontrol sayaçları yalnız ilk kontrolde yazılır; (2) oynatıcı yanıtında satır
/// sonucu yalnız kontrol edilip cevap değişmediyse görünür (hiç kontrol yoksa / save-answer sonrası gizli); (3) çözüm
/// sızmaz; (4) etkinlik en az bir kontrol olmadan bitmez (409 <c>ActivityCheckRequired</c>), boş maddeler engellemez;
/// (5) Test oturumu kuralları aynı kalır; (6) <c>PassageDto</c> tür + yönerge alanlarını taşır (canvas ve canvas-dışı).
/// </summary>
public class TestSessionServiceActivityKindTests : IDisposable
{
    private const int StudentUserId = 4401;
    private const string Instruction = "Resimdeki varlıkları adlarıyla eşleştir.";

    private static readonly string Plan =
        """{"version":1,"type":"dragDropLabeling","dropZones":[{"id":"zone-a"},{"id":"zone-b"}],"draggables":[{"id":"card-1","label":"Bir"},{"id":"card-2","label":"İki"}]}""";

    private static readonly string Solution =
        """{"placements":[{"draggableId":"card-1","dropZoneId":"zone-a"},{"draggableId":"card-2","dropZoneId":"zone-b"}]}""";

    private static readonly string FullPayload =
        """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"zone-a","draggableId":"card-1"},{"dropZoneId":"zone-b","draggableId":"card-2"}]}""";

    private static readonly string PartialPayload =
        """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"zone-a","draggableId":"card-1"}]}""";

    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private static readonly UserProfileDto Student = new() { Id = StudentUserId, KeycloakId = "kc-4401", Role = "Student" };

    /// <summary>
    /// Bir öğrenci oturumu: MCQ (doğru A / yanlış B, passage'lı) + dragDropLabeling + boş bir MCQ satırı (cevapsız).
    /// Passage: etkinlikte Activity + yönerge; testte varsayılan (Reading).
    /// </summary>
    private sealed record Seed(int InstanceId, int McqRowId, int DragRowId, int BlankRowId, int CorrectAnswerId, int WrongAnswerId, int McqQuestionId);

    private async Task<Seed> SeedAsync(WorksheetKind kind, WorksheetInstanceStatus status = WorksheetInstanceStatus.Started)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();

        var student = new Student { UserId = StudentUserId, StudentNumber = "S4401", SchoolName = "Sch", GradeId = grade.Id };
        var worksheet = new Worksheet { Name = "Sayfa", Description = "", GradeId = grade.Id, MaxDurationSeconds = 0, Kind = kind };
        ctx.AddRange(student, worksheet);
        await ctx.SaveChangesAsync();

        var passage = new Passage
        {
            Title = "Görsel", Text = "metin",
            Kind = kind == WorksheetKind.Activity ? PassageKind.Activity : PassageKind.Reading,
            Instruction = kind == WorksheetKind.Activity ? Instruction : null,
        };
        ctx.Passage.Add(passage);
        await ctx.SaveChangesAsync();

        var mcq = new Question { Text = "2 + 2 = ?", Point = 10, PassageId = passage.Id };
        var drag = new Question { Text = "Kartları yerleştir", Point = 10, InteractionType = "dragDropLabeling", InteractionPlan = Plan, InteractionSolution = Solution };
        var blank = new Question { Text = "Boş bırakılacak", Point = 10 };
        ctx.Questions.AddRange(mcq, drag, blank);
        await ctx.SaveChangesAsync();

        var right = new Answer { QuestionId = mcq.Id, Text = "4", Tag = "A" };
        var wrong = new Answer { QuestionId = mcq.Id, Text = "5", Tag = "B" };
        var blankAnswer = new Answer { QuestionId = blank.Id, Text = "7", Tag = "A" };
        ctx.Answers.AddRange(right, wrong, blankAnswer);
        await ctx.SaveChangesAsync();
        mcq.CorrectAnswerId = right.Id;
        blank.CorrectAnswerId = blankAnswer.Id;
        await ctx.SaveChangesAsync();

        var wqs = new[] { mcq, drag, blank }
            .Select((q, i) => new WorksheetQuestion { TestId = worksheet.Id, QuestionId = q.Id, Order = i + 1 })
            .ToList();
        ctx.TestQuestions.AddRange(wqs);
        await ctx.SaveChangesAsync();

        var instance = new WorksheetInstance
        {
            StudentId = student.Id, WorksheetId = worksheet.Id, Status = status,
            StartTime = new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc),
            EndTime = WorksheetInstanceStatusRules.IsFinished(status) ? new DateTime(2020, 1, 2, 0, 5, 0, DateTimeKind.Utc) : null,
            MaxDurationSeconds = 0,
        };
        ctx.TestInstances.Add(instance);
        await ctx.SaveChangesAsync();

        var rows = wqs.Select(wq => new WorksheetInstanceQuestion { WorksheetInstanceId = instance.Id, WorksheetQuestionId = wq.Id }).ToList();
        ctx.TestInstanceQuestions.AddRange(rows);
        await ctx.SaveChangesAsync();

        return new Seed(instance.Id, rows[0].Id, rows[1].Id, rows[2].Id, right.Id, wrong.Id, mcq.Id);
    }

    private TestSessionService NewService(AppDbContext ctx) => new(ctx);

    private async Task<ActivityCheckResultDto> CheckAsync(int instanceId, params ActivityCheckAnswerDto[] answers)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).CheckActivityAsync(instanceId, new ActivityCheckRequestDto { Answers = answers.ToList() }, StudentUserId);
    }

    private async Task SaveAsync(Seed s, int rowId, SaveAnswerDto dto)
    {
        dto.TestInstanceId = s.InstanceId;
        dto.TestQuestionId = rowId;
        dto.TimeTaken = 5;
        await using var ctx = _db.NewContext();
        (await NewService(ctx).SaveAnswer(dto, Student)).Success.ShouldBeTrue();
    }

    private async Task<WorksheetInstance> InstanceAsync(int id)
    {
        await using var ctx = _db.NewContext();
        return await ctx.TestInstances.AsNoTracking().SingleAsync(x => x.Id == id);
    }

    private async Task<WorksheetInstanceResultDto> CanvasAsync(int instanceId, bool includeCorrectAnswer = false)
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).GetCanvasTestResultAsync(instanceId, StudentUserId, includeCorrectAnswer);
        result.ShouldNotBeNull();
        return result!;
    }

    private static WorksheetInstanceQuestionDto Row(WorksheetInstanceResultDto result, int questionId) =>
        result.TestInstanceQuestions.Single(r => r.Question.Id == questionId);

    private static ActivityCheckAnswerDto Mcq(int rowId, int selected) => new() { InstanceQuestionId = rowId, SelectedAnswerId = selected };

    private static ActivityCheckAnswerDto Drag(int rowId, string payload) => new() { InstanceQuestionId = rowId, AnswerPayload = payload };

    // ---------------------------------------------------------------- ilk kontrol sayaçları

    [Fact]
    public async Task CheckActivity_FirstCheck_StoresFirstCheckCorrectAndTotalCounts()
    {
        var s = await SeedAsync(WorksheetKind.Activity);

        (await CheckAsync(s.InstanceId, Mcq(s.McqRowId, s.CorrectAnswerId), Drag(s.DragRowId, PartialPayload))).Success.ShouldBeTrue();

        var instance = await InstanceAsync(s.InstanceId);
        instance.FirstCheckCorrectCount.ShouldBe(2); // MCQ 1/1 + drag 1/2
        instance.FirstCheckTotalCount.ShouldBe(3);
    }

    [Fact]
    public async Task CheckActivity_SecondCheck_DoesNotOverwriteFirstCheckCounts()
    {
        var s = await SeedAsync(WorksheetKind.Activity);
        (await CheckAsync(s.InstanceId, Mcq(s.McqRowId, s.WrongAnswerId))).Success.ShouldBeTrue();

        (await CheckAsync(s.InstanceId, Mcq(s.McqRowId, s.CorrectAnswerId), Drag(s.DragRowId, FullPayload))).Success.ShouldBeTrue();

        var instance = await InstanceAsync(s.InstanceId);
        instance.CheckCount.ShouldBe(2);
        instance.FirstCheckCorrectCount.ShouldBe(0); // ilk kontrol: MCQ yanlış
        instance.FirstCheckTotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task CheckActivity_FirstCheckSubmittingOneRow_CountsOnlyThatRow()
    {
        var s = await SeedAsync(WorksheetKind.Activity);

        (await CheckAsync(s.InstanceId, Drag(s.DragRowId, FullPayload))).Success.ShouldBeTrue();
        (await CheckAsync(s.InstanceId, Mcq(s.McqRowId, s.CorrectAnswerId))).Success.ShouldBeTrue();

        var instance = await InstanceAsync(s.InstanceId);
        instance.FirstCheckCorrectCount.ShouldBe(2); // yalnız drag satırı: 2/2
        instance.FirstCheckTotalCount.ShouldBe(2);
    }

    // ---------------------------------------------------------------- oynatıcı: satır sonucu görünürlüğü

    [Fact]
    public async Task CanvasResult_AfterCheck_ExposesRowResultsOfCheckedRows()
    {
        var s = await SeedAsync(WorksheetKind.Activity);
        (await CheckAsync(s.InstanceId, Mcq(s.McqRowId, s.CorrectAnswerId), Drag(s.DragRowId, PartialPayload))).Success.ShouldBeTrue();

        var result = await CanvasAsync(s.InstanceId);

        result.Kind.ShouldBe(WorksheetKind.Activity);
        result.CheckCount.ShouldBe(1);
        result.FirstCheckedAt.ShouldNotBeNull();
        var mcq = Row(result, s.McqQuestionId);
        mcq.IsCorrect.ShouldBe(true);
        var drag = result.TestInstanceQuestions.Single(r => r.Id == s.DragRowId);
        drag.IsCorrect.ShouldBe(false);
        drag.CorrectCount.ShouldBe(1);
        drag.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task CanvasResult_BeforeAnyCheckEvenWhenAnswered_HidesRowResults()
    {
        var s = await SeedAsync(WorksheetKind.Activity);
        await SaveAsync(s, s.McqRowId, new SaveAnswerDto { SelectedAnswerId = s.CorrectAnswerId });

        var result = await CanvasAsync(s.InstanceId);

        result.CheckCount.ShouldBe(0);
        result.FirstCheckedAt.ShouldBeNull();
        var row = result.TestInstanceQuestions.Single(r => r.Id == s.McqRowId);
        row.SelectedAnswerId.ShouldBe(s.CorrectAnswerId); // cevabın kendisi geri gelir
        row.IsCorrect.ShouldBeNull();
        row.CorrectCount.ShouldBeNull();
        row.TotalCount.ShouldBeNull();
        row.Feedback.ShouldBeNull();
    }

    [Fact]
    public async Task CanvasResult_SaveAnswerAfterCheck_HidesThatRowAgainButKeepsUnchangedRows()
    {
        var s = await SeedAsync(WorksheetKind.Activity);
        (await CheckAsync(s.InstanceId, Mcq(s.McqRowId, s.CorrectAnswerId), Drag(s.DragRowId, PartialPayload))).Success.ShouldBeTrue();

        await SaveAsync(s, s.McqRowId, new SaveAnswerDto { SelectedAnswerId = s.WrongAnswerId });

        var result = await CanvasAsync(s.InstanceId);
        result.TestInstanceQuestions.Single(r => r.Id == s.McqRowId).IsCorrect.ShouldBeNull();
        result.TestInstanceQuestions.Single(r => r.Id == s.DragRowId).IsCorrect.ShouldBe(false);
    }

    [Fact]
    public async Task CanvasResult_ActivityBeforeAndAfterCheck_NeverLeaksSolutionOrCorrectAnswer()
    {
        var s = await SeedAsync(WorksheetKind.Activity);

        var before = await CanvasAsync(s.InstanceId);
        foreach (var r in before.TestInstanceQuestions)
        {
            r.Question.CorrectAnswerId.ShouldBeNull();
            r.Question.InteractionSolution.ShouldBeNull();
        }

        (await CheckAsync(s.InstanceId, Mcq(s.McqRowId, s.WrongAnswerId), Drag(s.DragRowId, PartialPayload))).Success.ShouldBeTrue();

        var after = await CanvasAsync(s.InstanceId);
        foreach (var r in after.TestInstanceQuestions)
        {
            r.Question.CorrectAnswerId.ShouldBeNull();
            r.Question.InteractionSolution.ShouldBeNull();
        }
    }

    [Fact]
    public async Task CanvasResult_TestStartedWithAnswers_RowResultsStayHidden()
    {
        var s = await SeedAsync(WorksheetKind.Test);
        await SaveAsync(s, s.DragRowId, new SaveAnswerDto { AnswerPayload = PartialPayload });

        var result = await CanvasAsync(s.InstanceId);

        result.Kind.ShouldBe(WorksheetKind.Test);
        result.CheckCount.ShouldBeNull();
        result.FirstCheckedAt.ShouldBeNull();
        result.SolutionsRevealedAt.ShouldBeNull();
        result.TestInstanceQuestions.Single(r => r.Id == s.DragRowId).IsCorrect.ShouldBeNull();
    }

    [Fact]
    public async Task CanvasResult_CompletedTest_ExposureRulesUnchanged()
    {
        var s = await SeedAsync(WorksheetKind.Test);
        await SaveAsync(s, s.McqRowId, new SaveAnswerDto { SelectedAnswerId = s.CorrectAnswerId });
        await SaveAsync(s, s.DragRowId, new SaveAnswerDto { AnswerPayload = PartialPayload });
        await using (var ctx = _db.NewContext())
            (await NewService(ctx).EndTest(s.InstanceId, StudentUserId)).Success.ShouldBeTrue();

        // includeCorrectAnswer=false → hiçbir satır notu yok (kontrol kavramı Test'te yok).
        var hidden = await CanvasAsync(s.InstanceId, includeCorrectAnswer: false);
        hidden.TestInstanceQuestions.All(r => r.IsCorrect == null).ShouldBeTrue();

        // includeCorrectAnswer=true (bitmiş test): yalnız etkileşimli satır notu; MCQ satırı yine gizli (#571 kuralı).
        var revealed = await CanvasAsync(s.InstanceId, includeCorrectAnswer: true);
        revealed.TestInstanceQuestions.Single(r => r.Id == s.McqRowId).IsCorrect.ShouldBeNull();
        revealed.TestInstanceQuestions.Single(r => r.Id == s.DragRowId).IsCorrect.ShouldBe(false);
        revealed.Kind.ShouldBe(WorksheetKind.Test);
    }

    // ---------------------------------------------------------------- bitirme kapısı

    [Fact]
    public async Task EndTest_ActivityWithoutAnyCheck_IsConflictAndInstanceStaysStarted()
    {
        var s = await SeedAsync(WorksheetKind.Activity);
        await SaveAsync(s, s.McqRowId, new SaveAnswerDto { SelectedAnswerId = s.CorrectAnswerId });

        TestSessionResultDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).EndTest(s.InstanceId, StudentUserId);

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.ActivityCheckRequired);
        var instance = await InstanceAsync(s.InstanceId);
        instance.Status.ShouldBe(WorksheetInstanceStatus.Started);
        instance.EndTime.ShouldBeNull();
    }

    [Fact]
    public async Task EndTest_ActivityAfterCheckWithBlankItems_CompletesTheInstance()
    {
        var s = await SeedAsync(WorksheetKind.Activity);
        (await CheckAsync(s.InstanceId, Mcq(s.McqRowId, s.CorrectAnswerId))).Success.ShouldBeTrue();

        TestSessionResultDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).EndTest(s.InstanceId, StudentUserId);

        result.Success.ShouldBeTrue(result.Message);
        var instance = await InstanceAsync(s.InstanceId);
        instance.Status.ShouldBe(WorksheetInstanceStatus.Completed);
        instance.EndTime.ShouldNotBeNull();
        (await InstanceAsync(s.InstanceId)).CheckCount.ShouldBe(1);
    }

    [Fact]
    public async Task EndTest_TestWorksheetWithoutCheck_CompletesAsBefore()
    {
        var s = await SeedAsync(WorksheetKind.Test);
        await SaveAsync(s, s.McqRowId, new SaveAnswerDto { SelectedAnswerId = s.CorrectAnswerId });

        TestSessionResultDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).EndTest(s.InstanceId, StudentUserId);

        result.Success.ShouldBeTrue(result.Message);
        var instance = await InstanceAsync(s.InstanceId);
        instance.Status.ShouldBe(WorksheetInstanceStatus.Completed);
        instance.CheckCount.ShouldBe(0);
        instance.FirstCheckCorrectCount.ShouldBeNull();
        instance.FirstCheckTotalCount.ShouldBeNull();
    }

    // ---------------------------------------------------------------- PassageDto: tür + yönerge

    [Fact]
    public async Task CanvasResult_ActivityPassage_CarriesKindActivityAndInstruction()
    {
        var s = await SeedAsync(WorksheetKind.Activity);

        var passage = Row(await CanvasAsync(s.InstanceId), s.McqQuestionId).Question.Passage.ShouldNotBeNull();

        passage.Kind.ShouldBe(PassageKind.Activity);
        passage.Instruction.ShouldBe(Instruction);
    }

    [Fact]
    public async Task CanvasResult_ReadingPassage_DefaultsToReadingWithoutInstruction()
    {
        var s = await SeedAsync(WorksheetKind.Test);

        var passage = Row(await CanvasAsync(s.InstanceId), s.McqQuestionId).Question.Passage.ShouldNotBeNull();

        passage.Kind.ShouldBe(PassageKind.Reading);
        passage.Instruction.ShouldBeNull();
    }

    [Fact]
    public async Task NonCanvasQuestions_ActivityPassage_CarriesKindActivityAndInstruction()
    {
        var s = await SeedAsync(WorksheetKind.Activity);

        WorksheetInstanceDto? dto;
        await using (var ctx = _db.NewContext())
            dto = await NewService(ctx).GetTestInstanceQuestionsAsync(s.InstanceId, StudentUserId);

        dto.ShouldNotBeNull();
        var passage = dto!.TestInstanceQuestions.Single(r => r.Question.Id == s.McqQuestionId).Question.Passage.ShouldNotBeNull();
        passage.Kind.ShouldBe(PassageKind.Activity);
        passage.Instruction.ShouldBe(Instruction);
    }
}
