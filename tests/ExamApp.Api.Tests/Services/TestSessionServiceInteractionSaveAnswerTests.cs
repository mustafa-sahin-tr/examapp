using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #571: <c>SaveAnswer</c> dragDropLabeling puanlaması — tam / kısmi doğru (satır bazlı geri bildirim, çözüm sızmaz) ve
/// geçersiz payload (400, hiçbir şey yazılmaz: AnswerRevision, outbox, kayıtlı cevap değişmez).
/// </summary>
public class TestSessionServiceInteractionSaveAnswerTests : IDisposable
{
    private const int UserId = 7101;
    private const string SecretDraggableId = "SECRET-DRAG-42";

    private static readonly string Plan =
        """{"version":1,"type":"dragDropLabeling","dropZones":[{"id":"zone-a"},{"id":"zone-b"}],"draggables":[{"id":"card-1","label":"Bir"},{"id":"card-2","label":"İki"},{"id":"SECRET-DRAG-42","label":"Üç"}]}""";

    private static readonly string Solution =
        """{"placements":[{"draggableId":"card-1","dropZoneId":"zone-a"},{"draggableId":"card-2","dropZoneId":"zone-b"}]}""";

    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private sealed record Seeded(int InstanceId, int TiqId);

    /// <summary>Başlamış (Started) test oturumu, tek dragDropLabeling sorusu; çözüm kolonda.</summary>
    private async Task<Seeded> SeedStartedDragDropAsync(WorksheetKind kind = WorksheetKind.Test)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();

        var student = new Student { UserId = UserId, StudentNumber = "S", SchoolName = "Sch", GradeId = grade.Id };
        var worksheet = new Worksheet { Name = "Eşleştirme", Description = "", GradeId = grade.Id, MaxDurationSeconds = 600, Kind = kind };
        var question = new Question
        {
            Text = "Kartları yerleştir", Point = 10, InteractionType = "dragDropLabeling",
            InteractionPlan = Plan, InteractionSolution = Solution,
        };
        ctx.AddRange(student, worksheet, question);
        await ctx.SaveChangesAsync();

        var wq = new WorksheetQuestion { TestId = worksheet.Id, QuestionId = question.Id, Order = 1 };
        ctx.TestQuestions.Add(wq);
        await ctx.SaveChangesAsync();

        var instance = new WorksheetInstance
        {
            StudentId = student.Id, WorksheetId = worksheet.Id, Status = WorksheetInstanceStatus.Started,
            StartTime = DateTime.UtcNow.AddMinutes(-5),
        };
        ctx.TestInstances.Add(instance);
        await ctx.SaveChangesAsync();

        var row = new WorksheetInstanceQuestion { WorksheetInstanceId = instance.Id, WorksheetQuestionId = wq.Id };
        ctx.TestInstanceQuestions.Add(row);
        await ctx.SaveChangesAsync();
        return new Seeded(instance.Id, row.Id);
    }

    private static UserProfileDto User => new() { Id = UserId, KeycloakId = "kc-7101" };

    private static SaveAnswerDto Dto(Seeded s, string? payload) => new()
    {
        TestInstanceId = s.InstanceId,
        TestQuestionId = s.TiqId,
        SelectedAnswerId = 0,
        AnswerPayload = payload,
        TimeTaken = 12,
    };

    private TestSessionService NewService(AppDbContext ctx) => new(ctx);

    private async Task<WorksheetInstanceQuestion> RowAsync(int tiqId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.TestInstanceQuestions.AsNoTracking().SingleAsync(x => x.Id == tiqId);
    }

    private async Task<List<AnswerSubmittedEvent>> AnswerEventsAsync()
    {
        await using var ctx = _db.NewContext();
        var type = OutboxEventRegistry.NameFor<AnswerSubmittedEvent>();
        return (await ctx.OutboxMessages.AsNoTracking().Where(o => o.Type == type).ToListAsync())
            .Select(o => JsonSerializer.Deserialize<AnswerSubmittedEvent>(o.Content)!)
            .ToList();
    }

    private async Task<int> OutboxCountAsync()
    {
        await using var ctx = _db.NewContext();
        return await ctx.OutboxMessages.CountAsync();
    }

    [Fact]
    public async Task SaveAnswer_DragDropFullyCorrect_IsCorrectWithAllRowsCorrectAndOutboxIsCorrect()
    {
        var s = await SeedStartedDragDropAsync();
        var payload = """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"zone-a","draggableId":"card-1"},{"dropZoneId":"zone-b","draggableId":"card-2"}]}""";

        await using (var ctx = _db.NewContext())
        {
            var result = await NewService(ctx).SaveAnswer(Dto(s, payload), User);
            result.Success.ShouldBeTrue();
            result.InvalidInput.ShouldBeFalse();
        }

        var row = await RowAsync(s.TiqId);
        row.IsCorrect.ShouldBeTrue();
        row.CorrectCount.ShouldBe(2);
        row.TotalCount.ShouldBe(2);
        row.AnswerPayload.ShouldBe(payload);
        row.AnswerRevision.ShouldBe(1);
        row.Feedback.ShouldNotBeNull();

        var evt = (await AnswerEventsAsync()).ShouldHaveSingleItem();
        evt.IsCorrect.ShouldBeTrue();
        evt.Revision.ShouldBe(1);
        evt.TestInstanceQuestionId.ShouldBe(s.TiqId);
    }

    [Fact]
    public async Task SaveAnswer_DragDropPartiallyCorrect_IsWrongAndFeedbackNamesTheWrongZone()
    {
        var s = await SeedStartedDragDropAsync();
        // zone-a doğru; zone-b boş → kısmen doğru.
        var payload = """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"zone-a","draggableId":"card-1"}]}""";

        await using (var ctx = _db.NewContext())
        {
            (await NewService(ctx).SaveAnswer(Dto(s, payload), User)).Success.ShouldBeTrue();
        }

        var row = await RowAsync(s.TiqId);
        row.IsCorrect.ShouldBeFalse();
        row.CorrectCount.ShouldBe(1);
        row.TotalCount.ShouldBe(2);
        using var feedback = JsonDocument.Parse(row.Feedback!);
        var rows = feedback.RootElement.GetProperty("rows").EnumerateArray()
            .Select(r => (Id: r.GetProperty("id").GetString(), Correct: r.GetProperty("correct").GetBoolean()))
            .ToList();
        rows.ShouldBe(new[] { (Id: "zone-a", Correct: true), (Id: "zone-b", Correct: false) });
        (await AnswerEventsAsync()).ShouldHaveSingleItem().IsCorrect.ShouldBeFalse();
    }

    [Fact]
    public async Task SaveAnswer_DragDropFeedbackStoredAfterSave_NeverContainsTheExpectedCardId()
    {
        var s = await SeedStartedDragDropAsync();
        var payload = $$"""{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"zone-a","draggableId":"{{SecretDraggableId}}"}]}""";

        await using (var ctx = _db.NewContext())
        {
            (await NewService(ctx).SaveAnswer(Dto(s, payload), User)).Success.ShouldBeTrue();
        }

        var row = await RowAsync(s.TiqId);
        row.Feedback.ShouldNotBeNull();
        row.Feedback!.ShouldNotContain("card-1");
        row.Feedback.ShouldNotContain("card-2");
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"zone-zzz","draggableId":"card-1"}]}""")]
    [InlineData("""{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"zone-a","draggableId":"card-zzz"}]}""")]
    [InlineData("""{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"zone-a","draggableId":"card-1"},{"dropZoneId":"zone-b","draggableId":"card-1"}]}""")]
    [InlineData("""{"version":1,"type":"mcq","placements":[]}""")]
    public async Task SaveAnswer_InvalidDragDropPayload_IsInvalidInputAndWritesNothing(string payload)
    {
        var s = await SeedStartedDragDropAsync();

        TestSessionResultDto result;
        await using (var ctx = _db.NewContext())
        {
            result = await NewService(ctx).SaveAnswer(Dto(s, payload), User);
        }

        result.Success.ShouldBeFalse();
        result.InvalidInput.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.InvalidAnswerPayload);
        result.Conflict.ShouldBeFalse();

        var row = await RowAsync(s.TiqId);
        row.AnswerRevision.ShouldBe(0);
        row.AnswerPayload.ShouldBeNull();
        row.SelectedAnswerId.ShouldBeNull();
        row.IsCorrect.ShouldBeFalse();
        row.CorrectCount.ShouldBe(0);
        row.TotalCount.ShouldBe(0);
        row.Feedback.ShouldBeNull();
        (await OutboxCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task SaveAnswer_InvalidPayloadAfterValidAnswer_KeepsThePreviousAnswerAndRevision()
    {
        var s = await SeedStartedDragDropAsync();
        var good = """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"zone-a","draggableId":"card-1"},{"dropZoneId":"zone-b","draggableId":"card-2"}]}""";
        await using (var ctx = _db.NewContext())
        {
            (await NewService(ctx).SaveAnswer(Dto(s, good), User)).Success.ShouldBeTrue();
        }
        var before = await RowAsync(s.TiqId);

        TestSessionResultDto result;
        await using (var ctx = _db.NewContext())
        {
            result = await NewService(ctx).SaveAnswer(
                Dto(s, """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"zone-a","draggableId":"card-2"},{"dropZoneId":"zone-a","draggableId":"card-1"}]}"""),
                User);
        }

        result.InvalidInput.ShouldBeTrue();
        var after = await RowAsync(s.TiqId);
        after.AnswerPayload.ShouldBe(before.AnswerPayload);
        after.AnswerRevision.ShouldBe(before.AnswerRevision);
        after.IsCorrect.ShouldBe(before.IsCorrect);
        after.CorrectCount.ShouldBe(before.CorrectCount);
        after.Feedback.ShouldBe(before.Feedback);
        (await AnswerEventsAsync()).Count.ShouldBe(1); // yalnız ilk geçerli cevabın event'i
    }

    private static SaveAnswerDto DtoWith(Seeded s, int selected, string? payload) => new()
    {
        TestInstanceId = s.InstanceId,
        TestQuestionId = s.TiqId,
        SelectedAnswerId = selected,
        AnswerPayload = payload,
        TimeTaken = 5,
    };

    private sealed record McqSeed(int InstanceId, int TiqId, int CorrectId, int WrongId, int DeletedId, int ForeignId);

    /// <summary>Başlamış oturum, tek MCQ sorusu (doğru A, yanlış B, silinmiş şık C) + başka soruya ait şık F.</summary>
    private async Task<McqSeed> SeedStartedMcqAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();

        // Aynı test DB'sinde drag seed'i de öğrenci açtığı için mevcut öğrenci kullanılır (UserId tekil).
        var student = await ctx.Students.FirstOrDefaultAsync(x => x.UserId == UserId);
        if (student == null)
        {
            student = new Student { UserId = UserId, StudentNumber = "S", SchoolName = "Sch", GradeId = grade.Id };
            ctx.Students.Add(student);
        }
        var worksheet = new Worksheet { Name = "Test", Description = "", GradeId = grade.Id, MaxDurationSeconds = 600 };
        var mcq = new Question { Text = "2+2", Point = 10 };
        var other = new Question { Text = "3+3", Point = 10 };
        ctx.AddRange(worksheet, mcq, other);
        await ctx.SaveChangesAsync();

        var right = new Answer { QuestionId = mcq.Id, Text = "4", Tag = "A" };
        var wrong = new Answer { QuestionId = mcq.Id, Text = "5", Tag = "B" };
        var deleted = new Answer { QuestionId = mcq.Id, Text = "6", Tag = "C" };
        var foreign = new Answer { QuestionId = other.Id, Text = "6", Tag = "A" };
        ctx.Answers.AddRange(right, wrong, deleted, foreign);
        await ctx.SaveChangesAsync();
        mcq.CorrectAnswerId = right.Id;
        await ctx.SaveChangesAsync();

        var wq = new WorksheetQuestion { TestId = worksheet.Id, QuestionId = mcq.Id, Order = 1 };
        ctx.TestQuestions.Add(wq);
        await ctx.SaveChangesAsync();

        var instance = new WorksheetInstance
        {
            StudentId = student.Id, WorksheetId = worksheet.Id, Status = WorksheetInstanceStatus.Started,
            StartTime = DateTime.UtcNow.AddMinutes(-5),
        };
        ctx.TestInstances.Add(instance);
        await ctx.SaveChangesAsync();

        var row = new WorksheetInstanceQuestion { WorksheetInstanceId = instance.Id, WorksheetQuestionId = wq.Id };
        ctx.TestInstanceQuestions.Add(row);
        await ctx.SaveChangesAsync();

        // Soft-delete: şık filtreli sorguda görünmez (IgnoreQueryFilters ile işaretlenir).
        await ctx.Answers.IgnoreQueryFilters().Where(a => a.Id == deleted.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.IsDeleted, true));

        return new McqSeed(instance.Id, row.Id, right.Id, wrong.Id, deleted.Id, foreign.Id);
    }

    private async Task<WorksheetInstanceQuestion> RowByIdAsync(int tiqId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.TestInstanceQuestions.AsNoTracking().SingleAsync(x => x.Id == tiqId);
    }

    private async Task AssertNothingWrittenAsync(int tiqId)
    {
        var row = await RowByIdAsync(tiqId);
        row.AnswerRevision.ShouldBe(0);
        row.SelectedAnswerId.ShouldBeNull();
        row.AnswerPayload.ShouldBeNull();
        row.Feedback.ShouldBeNull();
        (await OutboxCountAsync()).ShouldBe(0);
    }

    // ---------------------------------------------------------------- review #571: etkinlik (Kind = Activity)

    [Fact]
    public async Task SaveAnswer_ActivityWorksheet_StoresAnswerGradeAndRevisionButWritesNoOutboxEvent()
    {
        var s = await SeedStartedDragDropAsync(WorksheetKind.Activity);
        var payload = """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"zone-a","draggableId":"card-1"},{"dropZoneId":"zone-b","draggableId":"card-2"}]}""";

        await using (var ctx = _db.NewContext())
        {
            (await NewService(ctx).SaveAnswer(Dto(s, payload), User)).Success.ShouldBeTrue();
        }

        var row = await RowAsync(s.TiqId);
        row.AnswerPayload.ShouldBe(payload);
        row.IsCorrect.ShouldBeTrue();
        row.CorrectCount.ShouldBe(2);
        row.TotalCount.ShouldBe(2);
        row.Feedback.ShouldNotBeNull();
        row.AnswerRevision.ShouldBe(1);
        (await AnswerEventsAsync()).ShouldBeEmpty(); // etkinlik puansız: AnswerSubmittedEvent yok (#572)
        (await OutboxCountAsync()).ShouldBe(0);
    }

    // ---------------------------------------------------------------- review #571: boş (placements: []) → blank, wrong değil

    [Fact]
    public async Task SaveAnswer_EmptyPlacements_IsStoredAsBlankAndCountsAsBlankNotWrong()
    {
        var s = await SeedStartedDragDropAsync();

        await using (var ctx = _db.NewContext())
        {
            (await NewService(ctx).SaveAnswer(Dto(s, """{"version":1,"type":"dragDropLabeling","placements":[]}"""), User))
                .Success.ShouldBeTrue();
        }

        var row = await RowAsync(s.TiqId);
        row.AnswerPayload.ShouldBeNull();
        row.SelectedAnswerId.ShouldBeNull();
        await using var check = _db.NewContext();
        var query = check.TestInstanceQuestions.Where(x => x.Id == s.TiqId);
        (await query.CountAsync(WorksheetScoring.IsBlankRow)).ShouldBe(1);
        (await query.CountAsync(WorksheetScoring.IsWrongRow)).ShouldBe(0);
        (await query.CountAsync(WorksheetScoring.IsCorrectRow)).ShouldBe(0);
    }

    // ---------------------------------------------------------------- review #571: 16 KB sınırı, MCQ payload'ı saklanmaz

    [Fact]
    public async Task SaveAnswer_PayloadAboveMaxLength_IsInvalidForDragDropAndMcq()
    {
        var dragSeed = await SeedStartedDragDropAsync();
        var mcqSeed = await SeedStartedMcqAsync();
        var oversized = "{" + new string(' ', InteractionPayload.MaxLength) + "}";

        await using (var ctx = _db.NewContext())
        {
            var drag = await NewService(ctx).SaveAnswer(Dto(dragSeed, oversized), User);
            drag.InvalidInput.ShouldBeTrue();
            drag.ErrorCode.ShouldBe(TestSessionErrorCodes.InvalidAnswerPayload);
        }
        await using (var ctx = _db.NewContext())
        {
            var mcq = await NewService(ctx).SaveAnswer(DtoWith(new Seeded(mcqSeed.InstanceId, mcqSeed.TiqId), mcqSeed.CorrectId, oversized), User);
            mcq.InvalidInput.ShouldBeTrue();
            mcq.ErrorCode.ShouldBe(TestSessionErrorCodes.InvalidAnswerPayload);
        }

        await AssertNothingWrittenAsync(dragSeed.TiqId);
        await AssertNothingWrittenAsync(mcqSeed.TiqId);
    }

    [Fact]
    public async Task SaveAnswer_PayloadSentToMcqQuestion_IsStoredAsNullAndTheSelectionIsGraded()
    {
        var s = await SeedStartedMcqAsync();

        await using (var ctx = _db.NewContext())
        {
            var result = await NewService(ctx).SaveAnswer(
                DtoWith(new Seeded(s.InstanceId, s.TiqId), s.CorrectId, """{"x":1}"""), User);
            result.Success.ShouldBeTrue();
        }

        var row = await RowByIdAsync(s.TiqId);
        row.AnswerPayload.ShouldBeNull();
        row.SelectedAnswerId.ShouldBe(s.CorrectId);
        row.IsCorrect.ShouldBeTrue();
    }

    // ---------------------------------------------------------------- review #571: şık sahipliği (400, yazma yok)

    [Fact]
    public async Task SaveAnswer_McqSelectionFromAnotherQuestion_IsInvalidAndWritesNothing()
    {
        var s = await SeedStartedMcqAsync();

        await using (var ctx = _db.NewContext())
        {
            var result = await NewService(ctx).SaveAnswer(DtoWith(new Seeded(s.InstanceId, s.TiqId), s.ForeignId, null), User);
            result.InvalidInput.ShouldBeTrue();
            result.ErrorCode.ShouldBe(TestSessionErrorCodes.InvalidAnswerPayload);
        }

        await AssertNothingWrittenAsync(s.TiqId);
    }

    [Fact]
    public async Task SaveAnswer_McqSelectionOfSoftDeletedOption_IsInvalidAndWritesNothing()
    {
        var s = await SeedStartedMcqAsync();

        await using (var ctx = _db.NewContext())
        {
            var result = await NewService(ctx).SaveAnswer(DtoWith(new Seeded(s.InstanceId, s.TiqId), s.DeletedId, null), User);
            result.InvalidInput.ShouldBeTrue();
            result.ErrorCode.ShouldBe(TestSessionErrorCodes.InvalidAnswerPayload);
        }

        await AssertNothingWrittenAsync(s.TiqId);
    }

    [Fact]
    public async Task SaveAnswer_SelectedAnswerOnNonMcqQuestion_IsForcedToNull()
    {
        var s = await SeedStartedDragDropAsync();
        var payload = """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"zone-a","draggableId":"card-1"}]}""";

        await using (var ctx = _db.NewContext())
        {
            (await NewService(ctx).SaveAnswer(DtoWith(s, selected: 987654, payload), User)).Success.ShouldBeTrue();
        }

        var row = await RowAsync(s.TiqId);
        row.SelectedAnswerId.ShouldBeNull();
        row.AnswerPayload.ShouldBe(payload);
    }
}
