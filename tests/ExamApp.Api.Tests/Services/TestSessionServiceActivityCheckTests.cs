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
/// issue #571: etkinlik "Cevapları kontrol et" (<c>CheckActivityAsync</c>) ve "Doğru cevapları göster"
/// (<c>GetActivitySolutionsAsync</c>). Sahiplik (başkası/yok → 404), yalnız etkinlik (409 NotAnActivity), yalnız Started
/// (409 TestNotInProgress, süre dolumu dahil), 400 istek/cevap doğrulaması (hiçbir şey yazılmaz), kontrol yanıtında çözüm
/// sızıntısı yok, çözüm yalnız en az bir kontrolden sonra (409 ActivityCheckRequired).
/// </summary>
public class TestSessionServiceActivityCheckTests : IDisposable
{
    private const int OwnerUserId = 7201;
    private const int OtherStudentUserId = 7202;
    private const string SecretCardId = "SECRET-CARD-99";

    private static readonly string Plan =
        """{"version":1,"type":"dragDropLabeling","dropZones":[{"id":"zone-a"},{"id":"zone-b"}],"draggables":[{"id":"card-1","label":"Bir"},{"id":"card-2","label":"İki"},{"id":"SECRET-CARD-99","label":"Üç"}]}""";

    private static readonly string Solution =
        """{"placements":[{"draggableId":"card-1","dropZoneId":"zone-a"},{"draggableId":"card-2","dropZoneId":"zone-b"}]}""";

    private static readonly string FullPayload =
        """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"zone-a","draggableId":"card-1"},{"dropZoneId":"zone-b","draggableId":"card-2"}]}""";

    private static readonly string PartialPayload =
        """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"zone-a","draggableId":"card-1"}]}""";

    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private sealed record Seeded(
        int InstanceId, int McqRowId, int DragRowId, int OtherInstanceRowId, int CorrectAnswerId, int WrongAnswerId, int ForeignAnswerId, int DeletedAnswerId);

    /// <summary>
    /// Bir öğrencinin oturumu: MCQ (doğru şık A, yanlış şık B) + dragDropLabeling. Ayrıca aynı öğrencinin ikinci oturumunda
    /// bir satır (başka oturumun satırı) ve başka soruya ait bir şık.
    /// </summary>
    private async Task<Seeded> SeedAsync(
        WorksheetKind kind = WorksheetKind.Activity,
        WorksheetInstanceStatus status = WorksheetInstanceStatus.Started,
        int maxDurationSeconds = 600,
        DateTime? startTime = null)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();

        var student = new Student { UserId = OwnerUserId, StudentNumber = "S", SchoolName = "Sch", GradeId = grade.Id };
        var worksheet = new Worksheet { Name = "Etkinlik", Description = "", GradeId = grade.Id, MaxDurationSeconds = 600, Kind = kind };
        var mcq = new Question { Text = "2 + 2 = ?", Point = 10 };
        var drag = new Question { Text = "Kartları yerleştir", Point = 10, InteractionType = "dragDropLabeling", InteractionPlan = Plan, InteractionSolution = Solution };
        var foreign = new Question { Text = "Başka soru", Point = 10 };
        ctx.AddRange(student, worksheet, mcq, drag, foreign);
        await ctx.SaveChangesAsync();

        var right = new Answer { QuestionId = mcq.Id, Text = "4", Tag = "A" };
        var wrong = new Answer { QuestionId = mcq.Id, Text = "5", Tag = "B" };
        var foreignAnswer = new Answer { QuestionId = foreign.Id, Text = "x", Tag = "A" };
        var deletedAnswer = new Answer { QuestionId = mcq.Id, Text = "silinmiş", Tag = "C" };
        ctx.Answers.AddRange(right, wrong, foreignAnswer, deletedAnswer);
        await ctx.SaveChangesAsync();
        mcq.CorrectAnswerId = right.Id;
        await ctx.SaveChangesAsync();

        var wqMcq = new WorksheetQuestion { TestId = worksheet.Id, QuestionId = mcq.Id, Order = 1 };
        var wqDrag = new WorksheetQuestion { TestId = worksheet.Id, QuestionId = drag.Id, Order = 2 };
        ctx.TestQuestions.AddRange(wqMcq, wqDrag);
        await ctx.SaveChangesAsync();

        // İkinci oturum başka bir sayfada (TestInstances: öğrenci + sayfa tekil).
        var otherWorksheet = new Worksheet { Name = "Başka etkinlik", Description = "", GradeId = grade.Id, MaxDurationSeconds = 600, Kind = kind };
        ctx.Worksheets.Add(otherWorksheet);
        await ctx.SaveChangesAsync();
        var wqOther = new WorksheetQuestion { TestId = otherWorksheet.Id, QuestionId = mcq.Id, Order = 1 };
        ctx.TestQuestions.Add(wqOther);
        await ctx.SaveChangesAsync();

        var instance = NewInstance(student.Id, worksheet.Id, status, maxDurationSeconds, startTime);
        var other = NewInstance(student.Id, otherWorksheet.Id, WorksheetInstanceStatus.Started, maxDurationSeconds, null);
        ctx.TestInstances.AddRange(instance, other);
        await ctx.SaveChangesAsync();

        var rowMcq = new WorksheetInstanceQuestion { WorksheetInstanceId = instance.Id, WorksheetQuestionId = wqMcq.Id };
        var rowDrag = new WorksheetInstanceQuestion { WorksheetInstanceId = instance.Id, WorksheetQuestionId = wqDrag.Id };
        var rowOther = new WorksheetInstanceQuestion { WorksheetInstanceId = other.Id, WorksheetQuestionId = wqOther.Id };
        ctx.TestInstanceQuestions.AddRange(rowMcq, rowDrag, rowOther);
        await ctx.SaveChangesAsync();

        await ctx.Answers.IgnoreQueryFilters().Where(a => a.Id == deletedAnswer.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(a => a.IsDeleted, true));

        return new Seeded(instance.Id, rowMcq.Id, rowDrag.Id, rowOther.Id, right.Id, wrong.Id, foreignAnswer.Id, deletedAnswer.Id);
    }

    private static WorksheetInstance NewInstance(int studentId, int worksheetId, WorksheetInstanceStatus status, int maxDurationSeconds, DateTime? startTime) => new()
    {
        StudentId = studentId,
        WorksheetId = worksheetId,
        Status = status,
        StartTime = startTime ?? DateTime.UtcNow.AddMinutes(-5),
        EndTime = WorksheetInstanceStatusRules.IsFinished(status) ? DateTime.UtcNow.AddMinutes(-1) : null,
        MaxDurationSeconds = maxDurationSeconds,
    };

    private static ActivityCheckRequestDto Request(params ActivityCheckAnswerDto[] answers) => new() { Answers = answers.ToList() };

    private static ActivityCheckAnswerDto Mcq(int rowId, int? selected) => new() { InstanceQuestionId = rowId, SelectedAnswerId = selected };

    private static ActivityCheckAnswerDto Drag(int rowId, string payload) => new() { InstanceQuestionId = rowId, AnswerPayload = payload };

    private TestSessionService NewService(AppDbContext ctx) => new(ctx);

    private async Task<ActivityCheckResultDto> CheckAsync(int instanceId, ActivityCheckRequestDto request, int userId = OwnerUserId)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).CheckActivityAsync(instanceId, request, userId);
    }

    private async Task<ActivitySolutionsResultDto> SolutionsAsync(int instanceId, int userId = OwnerUserId)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).GetActivitySolutionsAsync(instanceId, userId);
    }

    private async Task<WorksheetInstanceQuestion> RowAsync(int rowId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.TestInstanceQuestions.AsNoTracking().SingleAsync(x => x.Id == rowId);
    }

    private async Task<WorksheetInstance> InstanceAsync(int instanceId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.TestInstances.AsNoTracking().SingleAsync(x => x.Id == instanceId);
    }

    private async Task<int> OutboxCountAsync()
    {
        await using var ctx = _db.NewContext();
        return await ctx.OutboxMessages.CountAsync();
    }

    private async Task AssertNothingWrittenAsync(Seeded s)
    {
        var instance = await InstanceAsync(s.InstanceId);
        instance.CheckCount.ShouldBe(0);
        instance.FirstCheckedAt.ShouldBeNull();
        instance.SolutionsRevealedAt.ShouldBeNull();
        var mcq = await RowAsync(s.McqRowId);
        mcq.AnswerRevision.ShouldBe(0);
        mcq.SelectedAnswerId.ShouldBeNull();
        var drag = await RowAsync(s.DragRowId);
        drag.AnswerRevision.ShouldBe(0);
        drag.AnswerPayload.ShouldBeNull();
        (await OutboxCountAsync()).ShouldBe(0);
    }

    // ---------------------------------------------------------------- check: başarı ve yazılanlar

    [Fact]
    public async Task CheckActivity_MixedAnswers_ReturnsRowResultsAndCountsAcrossBothTypes()
    {
        var s = await SeedAsync();

        var result = await CheckAsync(s.InstanceId, Request(Mcq(s.McqRowId, s.CorrectAnswerId), Drag(s.DragRowId, PartialPayload)));

        result.Success.ShouldBeTrue();
        result.CorrectCount.ShouldBe(2); // MCQ 1/1 + drag 1/2
        result.TotalCount.ShouldBe(3);
        var mcq = result.Questions.Single(q => q.InstanceQuestionId == s.McqRowId);
        mcq.IsCorrect.ShouldBeTrue();
        mcq.Feedback.ShouldBeNull();
        var drag = result.Questions.Single(q => q.InstanceQuestionId == s.DragRowId);
        drag.IsCorrect.ShouldBeFalse();
        drag.CorrectCount.ShouldBe(1);
        drag.TotalCount.ShouldBe(2);
        drag.Feedback.ShouldNotBeNull();
    }

    [Fact]
    public async Task CheckActivity_McqWrongSelection_IsWrongByLiveComparison()
    {
        var s = await SeedAsync();

        var result = await CheckAsync(s.InstanceId, Request(Mcq(s.McqRowId, s.WrongAnswerId)));

        result.Success.ShouldBeTrue();
        result.Questions.Single().IsCorrect.ShouldBeFalse();
        result.CorrectCount.ShouldBe(0);
        result.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task CheckActivity_Success_StoresAnswersIncrementsCheckCountAndStampsFirstCheck()
    {
        var s = await SeedAsync();

        (await CheckAsync(s.InstanceId, Request(Mcq(s.McqRowId, s.CorrectAnswerId), Drag(s.DragRowId, FullPayload)))).Success.ShouldBeTrue();

        var instance = await InstanceAsync(s.InstanceId);
        instance.CheckCount.ShouldBe(1);
        instance.FirstCheckedAt.ShouldNotBeNull();
        var mcq = await RowAsync(s.McqRowId);
        mcq.SelectedAnswerId.ShouldBe(s.CorrectAnswerId);
        mcq.AnswerRevision.ShouldBe(1);
        var drag = await RowAsync(s.DragRowId);
        drag.AnswerPayload.ShouldBe(FullPayload);
        drag.IsCorrect.ShouldBeTrue();
        drag.CorrectCount.ShouldBe(2);
        drag.TotalCount.ShouldBe(2);
        drag.AnswerRevision.ShouldBe(1);
        // Etkinlik puansız: outbox event'i yazılmaz (tamamlama zinciri #572).
        (await OutboxCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task CheckActivity_SecondCheck_KeepsTheFirstCheckTimestampAndCountsAgain()
    {
        var s = await SeedAsync();
        (await CheckAsync(s.InstanceId, Request(Mcq(s.McqRowId, s.WrongAnswerId)))).Success.ShouldBeTrue();
        var firstStamp = (await InstanceAsync(s.InstanceId)).FirstCheckedAt;

        (await CheckAsync(s.InstanceId, Request(Mcq(s.McqRowId, s.CorrectAnswerId)))).Success.ShouldBeTrue();

        var instance = await InstanceAsync(s.InstanceId);
        instance.CheckCount.ShouldBe(2);
        instance.FirstCheckedAt.ShouldBe(firstStamp);
        (await RowAsync(s.McqRowId)).AnswerRevision.ShouldBe(2);
    }

    [Fact]
    public async Task CheckActivity_ResponseSerialized_NeverContainsTheSolutionOrCorrectAnswer()
    {
        var s = await SeedAsync();

        var result = await CheckAsync(s.InstanceId, Request(Mcq(s.McqRowId, s.WrongAnswerId), Drag(s.DragRowId, PartialPayload)));

        var json = JsonSerializer.Serialize(result).ToLowerInvariant();
        json.ShouldNotContain("correctanswerid");
        json.ShouldNotContain("interactionsolution");
        json.ShouldNotContain("placements");
        json.ShouldNotContain("card-1");
        json.ShouldNotContain("card-2");
        json.ShouldNotContain(SecretCardId.ToLowerInvariant());
    }

    // ---------------------------------------------------------------- check: sahiplik / tip / durum

    [Fact]
    public async Task CheckActivity_OtherStudentsInstance_IsNotFoundAndWritesNothing()
    {
        var s = await SeedAsync();

        var result = await CheckAsync(s.InstanceId, Request(Mcq(s.McqRowId, s.CorrectAnswerId)), userId: OtherStudentUserId);

        result.Success.ShouldBeFalse();
        result.NotFound.ShouldBeTrue();
        await AssertNothingWrittenAsync(s);
    }

    [Fact]
    public async Task CheckActivity_UnknownInstance_IsNotFound()
    {
        var result = await CheckAsync(987654, Request(new ActivityCheckAnswerDto { InstanceQuestionId = 1 }));

        result.NotFound.ShouldBeTrue();
    }

    [Fact]
    public async Task CheckActivity_TestWorksheet_IsConflictNotAnActivity()
    {
        var s = await SeedAsync(kind: WorksheetKind.Test);

        var result = await CheckAsync(s.InstanceId, Request(Mcq(s.McqRowId, s.CorrectAnswerId)));

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.NotAnActivity);
        await AssertNothingWrittenAsync(s);
    }

    [Theory]
    [InlineData(WorksheetInstanceStatus.Completed)]
    [InlineData(WorksheetInstanceStatus.Expired)]
    public async Task CheckActivity_FinishedInstance_IsConflictTestNotInProgressAndWritesNothing(WorksheetInstanceStatus status)
    {
        var s = await SeedAsync(status: status);

        var result = await CheckAsync(s.InstanceId, Request(Mcq(s.McqRowId, s.CorrectAnswerId)));

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.TestNotInProgress);
        await AssertNothingWrittenAsync(s);
    }

    [Fact]
    public async Task CheckActivity_TimeLimitPassed_ClosesInstanceAndReturnsTimeExpiredConflict()
    {
        // Başlangıç saatler önce, süre 60 sn: süre dolmuş (gerçek saate göre kesin geçmiş).
        var s = await SeedAsync(maxDurationSeconds: 60, startTime: DateTime.UtcNow.AddHours(-5));

        var result = await CheckAsync(s.InstanceId, Request(Mcq(s.McqRowId, s.CorrectAnswerId)));

        result.Conflict.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.TestNotInProgress);
        result.Reason.ShouldBe(TestSessionRejectReasons.TimeExpired);
        (await InstanceAsync(s.InstanceId)).Status.ShouldBe(WorksheetInstanceStatus.Expired);
        (await RowAsync(s.McqRowId)).AnswerRevision.ShouldBe(0);
    }

    // ---------------------------------------------------------------- check: 400 doğrulama (hiçbir şey yazılmaz)

    [Fact]
    public async Task CheckActivity_NullRequest_IsInvalidCheckRequest()
    {
        var s = await SeedAsync();

        var result = await CheckAsync(s.InstanceId, null!);

        result.InvalidInput.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.InvalidCheckRequest);
        await AssertNothingWrittenAsync(s);
    }

    [Fact]
    public async Task CheckActivity_EmptyAnswers_IsInvalidCheckRequest()
    {
        var s = await SeedAsync();

        var result = await CheckAsync(s.InstanceId, Request());

        result.InvalidInput.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.InvalidCheckRequest);
        await AssertNothingWrittenAsync(s);
    }

    [Fact]
    public async Task CheckActivity_MoreThanTheMaximumAnswers_IsInvalidCheckRequest()
    {
        var s = await SeedAsync();
        var tooMany = Enumerable.Range(1, ActivityCheckRequestDto.MaxAnswers + 1)
            .Select(i => new ActivityCheckAnswerDto { InstanceQuestionId = i })
            .ToArray();

        var result = await CheckAsync(s.InstanceId, Request(tooMany));

        result.InvalidInput.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.InvalidCheckRequest);
        await AssertNothingWrittenAsync(s);
    }

    [Fact]
    public async Task CheckActivity_DuplicateQuestionInRequest_IsInvalidCheckRequest()
    {
        var s = await SeedAsync();

        var result = await CheckAsync(s.InstanceId, Request(Mcq(s.McqRowId, s.CorrectAnswerId), Mcq(s.McqRowId, s.WrongAnswerId)));

        result.InvalidInput.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.InvalidCheckRequest);
        await AssertNothingWrittenAsync(s);
    }

    [Fact]
    public async Task CheckActivity_QuestionFromAnotherSession_IsInvalidCheckRequest()
    {
        var s = await SeedAsync();

        var result = await CheckAsync(s.InstanceId, Request(Mcq(s.McqRowId, s.CorrectAnswerId), Mcq(s.OtherInstanceRowId, s.CorrectAnswerId)));

        result.InvalidInput.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.InvalidCheckRequest);
        await AssertNothingWrittenAsync(s);
    }

    [Fact]
    public async Task CheckActivity_BadPayloadInOneAnswer_RejectsWholeRequestAndKeepsTheValidOne()
    {
        var s = await SeedAsync();
        var badPayload = """{"version":1,"type":"dragDropLabeling","placements":[{"dropZoneId":"zone-zzz","draggableId":"card-1"}]}""";

        var result = await CheckAsync(s.InstanceId, Request(Mcq(s.McqRowId, s.CorrectAnswerId), Drag(s.DragRowId, badPayload)));

        result.InvalidInput.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.InvalidAnswerPayload);
        await AssertNothingWrittenAsync(s); // geçerli MCQ cevabı da yazılmadı
    }

    [Fact]
    public async Task CheckActivity_McqSelectionFromAnotherQuestion_IsInvalidAnswerPayload()
    {
        var s = await SeedAsync();

        var result = await CheckAsync(s.InstanceId, Request(Mcq(s.McqRowId, s.ForeignAnswerId)));

        result.InvalidInput.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.InvalidAnswerPayload);
        await AssertNothingWrittenAsync(s);
    }

    [Fact]
    public async Task CheckActivity_NonexistentMcqSelection_IsInvalidAnswerPayload()
    {
        var s = await SeedAsync();

        var result = await CheckAsync(s.InstanceId, Request(Mcq(s.McqRowId, 987654)));

        result.InvalidInput.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.InvalidAnswerPayload);
        await AssertNothingWrittenAsync(s);
    }

    // ---------------------------------------------------------------- doğru cevapları göster

    [Fact]
    public async Task GetActivitySolutions_BeforeAnyCheck_IsConflictActivityCheckRequiredAndRevealsNothing()
    {
        var s = await SeedAsync();

        var result = await SolutionsAsync(s.InstanceId);

        result.Success.ShouldBeFalse();
        result.Conflict.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.ActivityCheckRequired);
        result.Questions.ShouldBeEmpty();
        (await InstanceAsync(s.InstanceId)).SolutionsRevealedAt.ShouldBeNull();
    }

    [Fact]
    public async Task GetActivitySolutions_AfterCheck_ReturnsMcqCorrectAnswerAndDragSolutionAndStampsReveal()
    {
        var s = await SeedAsync();
        (await CheckAsync(s.InstanceId, Request(Mcq(s.McqRowId, s.WrongAnswerId)))).Success.ShouldBeTrue();

        var result = await SolutionsAsync(s.InstanceId);

        result.Success.ShouldBeTrue();
        var mcq = result.Questions.Single(q => q.InstanceQuestionId == s.McqRowId);
        mcq.CorrectAnswerId.ShouldBe(s.CorrectAnswerId);
        mcq.InteractionSolution.ShouldBeNull();
        var drag = result.Questions.Single(q => q.InstanceQuestionId == s.DragRowId);
        drag.CorrectAnswerId.ShouldBeNull();
        drag.InteractionSolution.ShouldNotBeNull();
        drag.InteractionSolution!.ShouldContain("placements");
        drag.InteractionSolution.ShouldContain("card-1");
        (await InstanceAsync(s.InstanceId)).SolutionsRevealedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task GetActivitySolutions_CalledTwice_KeepsTheFirstRevealTimestamp()
    {
        var s = await SeedAsync();
        (await CheckAsync(s.InstanceId, Request(Mcq(s.McqRowId, s.WrongAnswerId)))).Success.ShouldBeTrue();
        (await SolutionsAsync(s.InstanceId)).Success.ShouldBeTrue();
        var firstReveal = (await InstanceAsync(s.InstanceId)).SolutionsRevealedAt;

        (await SolutionsAsync(s.InstanceId)).Success.ShouldBeTrue();

        (await InstanceAsync(s.InstanceId)).SolutionsRevealedAt.ShouldBe(firstReveal);
    }

    [Fact]
    public async Task GetActivitySolutions_AfterRevealRecheckIsAllowed()
    {
        var s = await SeedAsync();
        (await CheckAsync(s.InstanceId, Request(Mcq(s.McqRowId, s.WrongAnswerId)))).Success.ShouldBeTrue();
        (await SolutionsAsync(s.InstanceId)).Success.ShouldBeTrue();

        var recheck = await CheckAsync(s.InstanceId, Request(Mcq(s.McqRowId, s.CorrectAnswerId)));

        recheck.Success.ShouldBeTrue();
        recheck.Questions.Single().IsCorrect.ShouldBeTrue();
        (await InstanceAsync(s.InstanceId)).CheckCount.ShouldBe(2);
    }

    [Fact]
    public async Task GetActivitySolutions_OtherStudentsInstance_IsNotFound()
    {
        var s = await SeedAsync();
        (await CheckAsync(s.InstanceId, Request(Mcq(s.McqRowId, s.WrongAnswerId)))).Success.ShouldBeTrue();

        var result = await SolutionsAsync(s.InstanceId, OtherStudentUserId);

        result.NotFound.ShouldBeTrue();
        (await InstanceAsync(s.InstanceId)).SolutionsRevealedAt.ShouldBeNull();
    }

    [Fact]
    public async Task GetActivitySolutions_TestWorksheet_IsConflictNotAnActivity()
    {
        var s = await SeedAsync(kind: WorksheetKind.Test);

        var result = await SolutionsAsync(s.InstanceId);

        result.Conflict.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.NotAnActivity);
    }

    // ---------------------------------------------------------------- review #571: check yolu (normalize, 16 KB, şık sahipliği)

    [Fact]
    public async Task CheckActivity_EmptyPlacements_IsStoredAsBlankAndCountsAsBlankNotWrong()
    {
        var s = await SeedAsync();

        var result = await CheckAsync(s.InstanceId, Request(Drag(s.DragRowId, """{"version":1,"type":"dragDropLabeling","placements":[]}""")));

        result.Success.ShouldBeTrue();
        await using var ctx = _db.NewContext();
        var row = ctx.TestInstanceQuestions.Where(x => x.Id == s.DragRowId);
        (await row.SingleAsync()).AnswerPayload.ShouldBeNull();
        (await row.CountAsync(WorksheetScoring.IsBlankRow)).ShouldBe(1);
        (await row.CountAsync(WorksheetScoring.IsWrongRow)).ShouldBe(0);
    }

    [Fact]
    public async Task CheckActivity_PayloadAboveMaxLength_IsInvalidAndWritesNothing()
    {
        var s = await SeedAsync();
        var oversized = "{" + new string(' ', InteractionPayload.MaxLength) + "}";

        var result = await CheckAsync(s.InstanceId, Request(Mcq(s.McqRowId, s.CorrectAnswerId), Drag(s.DragRowId, oversized)));

        result.InvalidInput.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.InvalidAnswerPayload);
        await AssertNothingWrittenAsync(s);
    }

    [Fact]
    public async Task CheckActivity_PayloadSentToMcqQuestion_IsStoredAsNull()
    {
        var s = await SeedAsync();

        var result = await CheckAsync(s.InstanceId, Request(new ActivityCheckAnswerDto
        {
            InstanceQuestionId = s.McqRowId, SelectedAnswerId = s.CorrectAnswerId, AnswerPayload = """{"x":1}""",
        }));

        result.Success.ShouldBeTrue();
        var row = await RowAsync(s.McqRowId);
        row.AnswerPayload.ShouldBeNull();
        row.SelectedAnswerId.ShouldBe(s.CorrectAnswerId);
    }

    [Fact]
    public async Task CheckActivity_SoftDeletedOptionSelected_IsInvalidAndWritesNothing()
    {
        var s = await SeedAsync();

        var result = await CheckAsync(s.InstanceId, Request(Mcq(s.McqRowId, s.DeletedAnswerId)));

        result.InvalidInput.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.InvalidAnswerPayload);
        await AssertNothingWrittenAsync(s);
    }

    [Fact]
    public async Task CheckActivity_SelectedAnswerOnNonMcqQuestion_IsForcedToNull()
    {
        var s = await SeedAsync();

        var result = await CheckAsync(s.InstanceId, Request(new ActivityCheckAnswerDto
        {
            InstanceQuestionId = s.DragRowId, SelectedAnswerId = 987654, AnswerPayload = PartialPayload,
        }));

        result.Success.ShouldBeTrue();
        var row = await RowAsync(s.DragRowId);
        row.SelectedAnswerId.ShouldBeNull();
        row.AnswerPayload.ShouldBe(PartialPayload);
    }

    // ---------------------------------------------------------------- review #571: EndTest soft-delete + süre dolumu (#396)

    [Fact]
    public async Task EndTest_WorksheetSoftDeletedMidTestAndTimeLimitPassed_StaysExpiredNotCompleted()
    {
        var s = await SeedAsync(kind: WorksheetKind.Test, maxDurationSeconds: 60, startTime: DateTime.UtcNow.AddHours(-5));
        await using (var ctx = _db.NewContext())
        {
            var worksheetId = await ctx.TestInstances.AsNoTracking().Where(i => i.Id == s.InstanceId)
                .Select(i => i.WorksheetId).SingleAsync();
            await ctx.Worksheets.IgnoreQueryFilters().Where(w => w.Id == worksheetId)
                .ExecuteUpdateAsync(u => u.SetProperty(w => w.IsDeleted, true));
        }

        TestSessionResultDto result;
        await using (var ctx = _db.NewContext())
        {
            result = await new TestSessionService(ctx).EndTest(s.InstanceId, OwnerUserId);
        }

        result.Conflict.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.TestNotInProgress);
        result.Reason.ShouldBe(TestSessionRejectReasons.TimeExpired);
        (await InstanceAsync(s.InstanceId)).Status.ShouldBe(WorksheetInstanceStatus.Expired);
    }
}
