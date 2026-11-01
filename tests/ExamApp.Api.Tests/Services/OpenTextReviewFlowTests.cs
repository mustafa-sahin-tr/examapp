using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.OpenText;
using ExamApp.Api.Services.Interactions;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #583: açık uçlu cevap uçtan uca (AI'sız) — öğrenci yazar (save-answer taslak, kuyruğa düşmez), "Cevapları kontrol et" gönderir
/// (satır 0/0, sayımlara girmez, "pending"), atamayı yapan öğretmen kuyrukta görür ve değerlendirir, öğrenci değerlendirmeyi ve geri
/// bildirimi görür; metin değişince değerlendirme eskir ve yeniden kuyruğa düşer; yeniden çözme değerlendirmeleri siler. Yetki: atamasız
/// öğretmen göremez/değerlendiremez, sınıf ataması yapan öğretmen sınıfı görür, admin hepsini; öğrenci başkasının oturumunu göremez.
/// Hiçbir puan olayı (outbox) yazılmaz.
/// </summary>
public class OpenTextReviewFlowTests : IDisposable
{
    private const int StudentUserId = 58301;
    private const int OtherStudentUserId = 58302;
    private const int TeacherUserId = 58310;      // öğrenci hedefli atama
    private const int StrangerTeacherUserId = 58311; // atamasız, aynı okul
    private const int GradeTeacherUserId = 58312; // sınıf hedefli atama
    private const int AdminUserId = 58399;

    private const string Plan = """{"version":1,"type":"openText","minWords":3,"maxWords":40,"lines":4}""";
    private const string Solution = """{"sampleAnswer":"Su ısınınca buharlaşır.","rubric":"Isı + buharlaşma."}""";

    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private sealed record Seed(int SchoolId, int WorksheetId, int InstanceId, int OpenRow, int McqRow, int RightAnswerId,
        int OtherInstanceId, int OtherOpenRow);

    private async Task<Seed> SeedAsync(WorksheetKind kind = WorksheetKind.Activity)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        var school = new School { Name = "Okul" };
        ctx.AddRange(grade, school);
        await ctx.SaveChangesAsync();

        Student NewStudent(int userId, string number) => new()
        {
            UserId = userId, StudentNumber = number, SchoolName = "s", SchoolId = school.Id, SchoolVerifiedAt = DateTime.UtcNow,
            GradeId = grade.Id,
        };
        var student = NewStudent(StudentUserId, "S1");
        var other = NewStudent(OtherStudentUserId, "S2");
        ctx.AddRange(student, other);
        await ctx.SaveChangesAsync();

        ctx.SetCurrentUser(TeacherUserId);
        var worksheet = new Worksheet { Name = "Su döngüsü", Description = "", GradeId = grade.Id, MaxDurationSeconds = 0, Kind = kind };
        ctx.Worksheets.Add(worksheet);
        var open = new Question
        {
            Text = "Neden?", Point = 10, InteractionType = OpenTextGrader.Type, InteractionPlan = Plan, InteractionSolution = Solution,
        };
        var mcq = new Question { Text = "2 + 2", Point = 10 };
        ctx.Questions.AddRange(open, mcq);
        await ctx.SaveChangesAsync();
        var right = new Answer { QuestionId = mcq.Id, Text = "4", Tag = "A" };
        ctx.Answers.Add(right);
        await ctx.SaveChangesAsync();
        mcq.CorrectAnswerId = right.Id;
        var wqs = new[] { open, mcq }
            .Select((q, i) => new WorksheetQuestion { TestId = worksheet.Id, QuestionId = q.Id, Order = i + 1 }).ToList();
        ctx.TestQuestions.AddRange(wqs);
        ctx.WorksheetAssignments.Add(new WorksheetAssignment
        {
            WorksheetId = worksheet.Id, StudentId = student.Id, StartAt = DateTime.UtcNow.AddDays(-1),
        });
        await ctx.SaveChangesAsync();

        ctx.SetCurrentUser(GradeTeacherUserId);
        ctx.WorksheetAssignments.Add(new WorksheetAssignment
        {
            WorksheetId = worksheet.Id, GradeId = grade.Id, SchoolId = school.Id, StartAt = DateTime.UtcNow.AddDays(-1),
        });
        await ctx.SaveChangesAsync();

        WorksheetInstance NewInstance(Student s) => new()
        {
            StudentId = s.Id, WorksheetId = worksheet.Id, Status = WorksheetInstanceStatus.Started,
            StartTime = DateTime.UtcNow.AddMinutes(-10), MaxDurationSeconds = 0,
        };
        var instance = NewInstance(student);
        var otherInstance = NewInstance(other);
        ctx.TestInstances.AddRange(instance, otherInstance);
        await ctx.SaveChangesAsync();

        var rows = wqs.Select(wq => new WorksheetInstanceQuestion { WorksheetInstanceId = instance.Id, WorksheetQuestionId = wq.Id }).ToList();
        var otherRows = wqs.Select(wq => new WorksheetInstanceQuestion { WorksheetInstanceId = otherInstance.Id, WorksheetQuestionId = wq.Id }).ToList();
        ctx.TestInstanceQuestions.AddRange(rows.Concat(otherRows));
        await ctx.SaveChangesAsync();

        return new Seed(school.Id, worksheet.Id, instance.Id, rows[0].Id, rows[1].Id, right.Id, otherInstance.Id, otherRows[0].Id);
    }

    private static string Payload(string text) => JsonSerializer.Serialize(new { version = 1, type = "openText", text });

    private static SchoolScope TeacherScope(Seed s, int userId) => SchoolScope.For(userId, s.SchoolId);

    private async Task<TestSessionResultDto> SaveAsync(int instanceId, int rowId, string payload, int userId = StudentUserId)
    {
        await using var ctx = _db.NewContext();
        return await new TestSessionService(ctx).SaveAnswer(
            new SaveAnswerDto { TestInstanceId = instanceId, TestQuestionId = rowId, AnswerPayload = payload, TimeTaken = 5 },
            new UserProfileDto { Id = userId, KeycloakId = "kc-" + userId, Role = "Student" });
    }

    private async Task<ActivityCheckResultDto> CheckAsync(int instanceId, int userId, params ActivityCheckAnswerDto[] answers)
    {
        await using var ctx = _db.NewContext();
        return await new TestSessionService(ctx).CheckActivityAsync(instanceId, new ActivityCheckRequestDto { Answers = answers.ToList() }, userId);
    }

    private Task<ActivityCheckResultDto> CheckTextAsync(Seed s, string text, bool withMcq = true)
    {
        var answers = new List<ActivityCheckAnswerDto> { new() { InstanceQuestionId = s.OpenRow, AnswerPayload = Payload(text) } };
        if (withMcq)
            answers.Add(new ActivityCheckAnswerDto { InstanceQuestionId = s.McqRow, SelectedAnswerId = s.RightAnswerId });
        return CheckAsync(s.InstanceId, StudentUserId, answers.ToArray());
    }

    private async Task<WorksheetInstanceQuestionDto> StudentRowAsync(Seed s, int rowId, bool includeCorrectAnswer = false)
    {
        await using var ctx = _db.NewContext();
        var result = await new TestSessionService(ctx).GetCanvasTestResultAsync(s.InstanceId, StudentUserId, includeCorrectAnswer);
        return result!.TestInstanceQuestions.Single(q => q.Id == rowId);
    }

    private async Task<Paged<OpenTextReviewItemDto>> QueueAsync(SchoolScope scope, string? status = null, int? worksheetId = null,
        int page = 1, int pageSize = 20)
    {
        await using var ctx = _db.NewContext();
        return await new OpenTextReviewService(ctx, new SchoolAccessPolicy(ctx)).GetQueueAsync(scope,
            new OpenTextReviewQueueQuery { Status = status, WorksheetId = worksheetId, Page = page, PageSize = pageSize });
    }

    private async Task<OpenTextReviewResultDto> ReviewAsync(SchoolScope scope, int rowId, string evaluation, string? feedback,
        string? answerHash)
    {
        await using var ctx = _db.NewContext();
        return await new OpenTextReviewService(ctx, new SchoolAccessPolicy(ctx)).ReviewAsync(scope, rowId,
            new OpenTextReviewRequestDto { Evaluation = evaluation, Feedback = feedback, AnswerHash = answerHash });
    }

    private async Task<int> OutboxCountAsync()
    {
        await using var ctx = _db.NewContext();
        return await ctx.OutboxMessages.CountAsync();
    }

    // ---------- öğrenci yolu ----------

    [Fact]
    public async Task SaveAnswer_StoresDraftWithoutQueueingOrPointEvents()
    {
        var s = await SeedAsync();

        (await SaveAsync(s.InstanceId, s.OpenRow, Payload("  Su ısınınca\r\nbuharlaşır.  "))).Success.ShouldBeTrue();

        await using (var ctx = _db.NewContext())
            (await ctx.TestInstanceQuestions.AsNoTracking().SingleAsync(q => q.Id == s.OpenRow)).AnswerPayload
                .ShouldBe("""{"version":1,"type":"openText","text":"Su ısınınca\nbuharlaşır."}""");
        (await StudentRowAsync(s, s.OpenRow)).OpenTextReview.ShouldBeNull();       // gönderilmedi
        (await QueueAsync(TeacherScope(s, TeacherUserId))).TotalCount.ShouldBe(0);
        (await OutboxCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task SaveAnswer_TextFarOverMaxWords_IsRejected()
    {
        var s = await SeedAsync();

        var result = await SaveAsync(s.InstanceId, s.OpenRow, Payload(string.Join(' ', Enumerable.Repeat("söz", 81))));

        result.InvalidInput.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.InvalidAnswerPayload);
    }

    [Fact]
    public async Task Check_OpenTextRowIsPendingAndExcludedFromCounts()
    {
        var s = await SeedAsync();

        var result = await CheckTextAsync(s, "Su ısınınca buharlaşır.");

        result.Success.ShouldBeTrue(result.Message);
        (result.CorrectCount, result.TotalCount).ShouldBe((1, 1));                      // yalnız MCQ
        var open = result.Questions.Single(q => q.InstanceQuestionId == s.OpenRow);
        (open.IsCorrect, open.CorrectCount, open.TotalCount).ShouldBe((false, 0, 0));
        open.Feedback.ShouldBe("""{"rows":[{"id":"text","status":"pending"}]}""");
        open.OpenTextReview!.Status.ShouldBe(OpenTextReviewStatuses.Pending);
        open.OpenTextReview.Evaluation.ShouldBeNull();
        result.Questions.Single(q => q.InstanceQuestionId == s.McqRow).OpenTextReview.ShouldBeNull();

        await using (var ctx = _db.NewContext())
        {
            var instance = await ctx.TestInstances.AsNoTracking().SingleAsync(i => i.Id == s.InstanceId);
            (instance.FirstCheckCorrectCount, instance.FirstCheckTotalCount).ShouldBe((1, 1));
        }
        (await StudentRowAsync(s, s.OpenRow)).OpenTextReview!.Status.ShouldBe(OpenTextReviewStatuses.Pending);
        (await OutboxCountAsync()).ShouldBe(0);
    }

    // ---------- kuyruk + değerlendirme ----------

    [Fact]
    public async Task TeacherReviews_StudentSeesEvaluationAndFeedback_AndNoPointEvent()
    {
        var s = await SeedAsync();
        await CheckTextAsync(s, "Su ısınınca buharlaşır.");

        var queue = await QueueAsync(TeacherScope(s, TeacherUserId));
        var item = queue.Items.ShouldHaveSingleItem();
        item.InstanceQuestionId.ShouldBe(s.OpenRow);
        item.Text.ShouldBe("Su ısınınca buharlaşır.");
        item.WordCount.ShouldBe(3);
        (item.MinWords, item.MaxWords).ShouldBe(((int?)3, (int?)40));
        (item.SampleAnswer, item.Rubric).ShouldBe(("Su ısınınca buharlaşır.", "Isı + buharlaşma."));
        (item.Status, item.StudentNumber, item.WorksheetName).ShouldBe((OpenTextReviewStatuses.Pending, "S1", "Su döngüsü"));

        var saved = await ReviewAsync(TeacherScope(s, TeacherUserId), s.OpenRow, "partial", " Isıyı da yaz.\r\n", item.AnswerHash);

        saved.Success.ShouldBeTrue(saved.Message);
        saved.Item!.Status.ShouldBe(OpenTextReviewStatuses.Reviewed);
        saved.Item.Review!.Evaluation.ShouldBe("partial");
        saved.Item.Review.Source.ShouldBe("teacher");
        saved.Item.Review.ReviewedByUserId.ShouldBe(TeacherUserId);

        var row = await StudentRowAsync(s, s.OpenRow);
        row.OpenTextReview!.Status.ShouldBe(OpenTextReviewStatuses.Reviewed);
        (row.OpenTextReview.Evaluation, row.OpenTextReview.Feedback).ShouldBe(("partial", "Isıyı da yaz."));
        row.Feedback.ShouldBe("""{"rows":[{"id":"text","status":"partial"}]}""");

        (await QueueAsync(TeacherScope(s, TeacherUserId))).TotalCount.ShouldBe(0);
        (await QueueAsync(TeacherScope(s, TeacherUserId), "reviewed")).Items.ShouldHaveSingleItem();
        (await OutboxCountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task TeacherCanCorrectReview_SameRowIsUpdated()
    {
        var s = await SeedAsync();
        await CheckTextAsync(s, "Su ısınınca buharlaşır.");
        var hash = (await QueueAsync(TeacherScope(s, TeacherUserId))).Items.Single().AnswerHash;
        await ReviewAsync(TeacherScope(s, TeacherUserId), s.OpenRow, "insufficient", "Eksik.", hash);

        var corrected = await ReviewAsync(TeacherScope(s, TeacherUserId), s.OpenRow, "sufficient", null, hash);

        corrected.Success.ShouldBeTrue(corrected.Message);
        await using var ctx = _db.NewContext();
        var review = await ctx.OpenTextReviews.AsNoTracking().SingleAsync();
        (review.Evaluation, review.Feedback, review.Stale).ShouldBe((OpenTextEvaluation.Sufficient, (string?)null, false));
    }

    [Fact]
    public async Task RecheckWithSameText_KeepsReviewCurrent()
    {
        var s = await SeedAsync();
        await CheckTextAsync(s, "Su ısınınca buharlaşır.");
        var hash = (await QueueAsync(TeacherScope(s, TeacherUserId))).Items.Single().AnswerHash;
        await ReviewAsync(TeacherScope(s, TeacherUserId), s.OpenRow, "sufficient", "Güzel.", hash);

        var recheck = await CheckTextAsync(s, "Su ısınınca buharlaşır.\n");

        var open = recheck.Questions.Single(q => q.InstanceQuestionId == s.OpenRow);
        open.OpenTextReview!.Status.ShouldBe(OpenTextReviewStatuses.Reviewed);
        open.Feedback.ShouldBe("""{"rows":[{"id":"text","status":"sufficient"}]}""");
        (await QueueAsync(TeacherScope(s, TeacherUserId))).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task ChangedAnswer_MakesReviewOutdated_RequeuesOnSubmit_AndOldHashConflicts()
    {
        var s = await SeedAsync();
        await CheckTextAsync(s, "Su ısınınca buharlaşır.");
        var oldHash = (await QueueAsync(TeacherScope(s, TeacherUserId))).Items.Single().AnswerHash;
        await ReviewAsync(TeacherScope(s, TeacherUserId), s.OpenRow, "partial", "Isıyı yaz.", oldHash);

        // Taslak değişikliği: değerlendirme eskir (öğrenci eski geri bildirimi "outdated" ile görür) ama gönderilmeden kuyruğa düşmez.
        (await SaveAsync(s.InstanceId, s.OpenRow, Payload("Güneş suyu ısıtır, su buharlaşır."))).Success.ShouldBeTrue();
        var draft = await StudentRowAsync(s, s.OpenRow);
        (draft.OpenTextReview!.Status, draft.OpenTextReview.Feedback).ShouldBe((OpenTextReviewStatuses.Outdated, "Isıyı yaz."));
        (await QueueAsync(TeacherScope(s, TeacherUserId))).TotalCount.ShouldBe(0);

        await CheckTextAsync(s, "Güneş suyu ısıtır, su buharlaşır.", withMcq: false);
        var item = (await QueueAsync(TeacherScope(s, TeacherUserId))).Items.ShouldHaveSingleItem();
        item.Status.ShouldBe(OpenTextReviewStatuses.Outdated);
        item.Review!.Stale.ShouldBeTrue();

        var stale = await ReviewAsync(TeacherScope(s, TeacherUserId), s.OpenRow, "sufficient", null, oldHash);
        (stale.Conflict, stale.ErrorCode).ShouldBe((true, OpenTextReviewErrorCodes.AnswerChanged));

        (await ReviewAsync(TeacherScope(s, TeacherUserId), s.OpenRow, "sufficient", null, item.AnswerHash)).Success.ShouldBeTrue();
        (await StudentRowAsync(s, s.OpenRow)).OpenTextReview!.Status.ShouldBe(OpenTextReviewStatuses.Reviewed);
    }

    [Fact]
    public async Task Retake_DeletesTheSessionsReviews()
    {
        var s = await SeedAsync();
        await CheckTextAsync(s, "Su ısınınca buharlaşır.");
        var hash = (await QueueAsync(TeacherScope(s, TeacherUserId))).Items.Single().AnswerHash;
        await ReviewAsync(TeacherScope(s, TeacherUserId), s.OpenRow, "sufficient", null, hash);
        await using (var ctx = _db.NewContext())
            (await new TestSessionService(ctx).EndTest(s.InstanceId, StudentUserId)).Success.ShouldBeTrue();

        await using (var ctx = _db.NewContext())
            (await new TestSessionService(ctx).RetakeActivityAsync(s.InstanceId, StudentUserId)).Success.ShouldBeTrue();

        await using (var ctx = _db.NewContext())
            (await ctx.OpenTextReviews.CountAsync()).ShouldBe(0);
        (await StudentRowAsync(s, s.OpenRow)).OpenTextReview.ShouldBeNull();
        (await QueueAsync(TeacherScope(s, TeacherUserId), "all")).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task CompletedSessionWithUncheckedEdit_IsQueued()
    {
        var s = await SeedAsync();
        await CheckTextAsync(s, "İlk metin burada yazılı.");
        (await SaveAsync(s.InstanceId, s.OpenRow, Payload("Son metin burada yazılı."))).Success.ShouldBeTrue();
        await using (var ctx = _db.NewContext())
            (await new TestSessionService(ctx).EndTest(s.InstanceId, StudentUserId)).Success.ShouldBeTrue();

        (await QueueAsync(TeacherScope(s, TeacherUserId))).Items.ShouldHaveSingleItem().Text.ShouldBe("Son metin burada yazılı.");
    }

    // ---------- yetki ----------

    [Fact]
    public async Task TeacherWithoutAssignment_CannotSeeOrReview()
    {
        var s = await SeedAsync();
        await CheckTextAsync(s, "Su ısınınca buharlaşır.");
        var hash = (await QueueAsync(TeacherScope(s, TeacherUserId))).Items.Single().AnswerHash;

        (await QueueAsync(TeacherScope(s, StrangerTeacherUserId), "all")).TotalCount.ShouldBe(0);
        var denied = await ReviewAsync(TeacherScope(s, StrangerTeacherUserId), s.OpenRow, "sufficient", null, hash);
        (denied.NotFound, denied.ErrorCode).ShouldBe((true, OpenTextReviewErrorCodes.NotFound));

        // Başka okuldan öğretmen, kendi ataması olsa bile kapsam dışı öğrenciyi göremez.
        (await QueueAsync(SchoolScope.For(TeacherUserId, s.SchoolId + 1000), "all")).TotalCount.ShouldBe(0);
        await using var ctx = _db.NewContext();
        (await ctx.OpenTextReviews.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task GradeAssignmentTeacherSeesClass_StudentTeacherOnlyTheirStudent_AdminSeesAll()
    {
        var s = await SeedAsync();
        await CheckTextAsync(s, "Su ısınınca buharlaşır.");
        (await CheckAsync(s.OtherInstanceId, OtherStudentUserId,
            new ActivityCheckAnswerDto { InstanceQuestionId = s.OtherOpenRow, AnswerPayload = Payload("Sıcaktan buharlaşır su.") })).Success.ShouldBeTrue();

        (await QueueAsync(TeacherScope(s, TeacherUserId))).Items.Select(i => i.InstanceQuestionId).ShouldBe([s.OpenRow]);
        (await QueueAsync(TeacherScope(s, GradeTeacherUserId))).Items.Select(i => i.InstanceQuestionId)
            .ShouldBe([s.OpenRow, s.OtherOpenRow], ignoreOrder: true);
        (await QueueAsync(SchoolScope.Unrestricted(AdminUserId))).TotalCount.ShouldBe(2);
        (await QueueAsync(SchoolScope.Unrestricted(AdminUserId), worksheetId: s.WorksheetId + 999)).TotalCount.ShouldBe(0);

        var paged = await QueueAsync(TeacherScope(s, GradeTeacherUserId), pageSize: 1, page: 2);
        (paged.TotalCount, paged.Items.Count, paged.PageNumber).ShouldBe((2, 1, 2));
    }

    [Fact]
    public async Task Student_CannotReadAnotherStudentsSession()
    {
        var s = await SeedAsync();
        await CheckTextAsync(s, "Su ısınınca buharlaşır.");

        await using var ctx = _db.NewContext();
        (await new TestSessionService(ctx).GetCanvasTestResultAsync(s.InstanceId, OtherStudentUserId)).ShouldBeNull();
    }

    // ---------- doğrulama ----------

    [Theory]
    [InlineData("good", null)]
    [InlineData("", null)]
    [InlineData("partial", "bad\u0001char")]
    public async Task Review_InvalidRequest_IsRejected(string evaluation, string? feedback)
    {
        var s = await SeedAsync();
        await CheckTextAsync(s, "Su ısınınca buharlaşır.");
        var hash = (await QueueAsync(TeacherScope(s, TeacherUserId))).Items.Single().AnswerHash;

        var result = await ReviewAsync(TeacherScope(s, TeacherUserId), s.OpenRow, evaluation, feedback, hash);

        (result.InvalidInput, result.ErrorCode).ShouldBe((true, OpenTextReviewErrorCodes.InvalidReview));
    }

    [Fact]
    public async Task Review_FeedbackTooLongOrMissingHash_IsRejected()
    {
        var s = await SeedAsync();
        await CheckTextAsync(s, "Su ısınınca buharlaşır.");
        var hash = (await QueueAsync(TeacherScope(s, TeacherUserId))).Items.Single().AnswerHash;

        (await ReviewAsync(TeacherScope(s, TeacherUserId), s.OpenRow, "partial", new string('a', 1001), hash)).InvalidInput.ShouldBeTrue();
        (await ReviewAsync(TeacherScope(s, TeacherUserId), s.OpenRow, "partial", null, null)).InvalidInput.ShouldBeTrue();
        (await ReviewAsync(TeacherScope(s, TeacherUserId), s.OpenRow, "partial", new string('a', 1000), hash)).Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Review_NotSubmittedDraft_IsConflict()
    {
        var s = await SeedAsync();
        (await SaveAsync(s.InstanceId, s.OpenRow, Payload("Taslak metin yazılı burada."))).Success.ShouldBeTrue();
        string hash;
        await using (var ctx = _db.NewContext())
            hash = OpenTextGrader.AnswerHash((await ctx.TestInstanceQuestions.AsNoTracking().SingleAsync(q => q.Id == s.OpenRow)).AnswerPayload)!;

        var result = await ReviewAsync(TeacherScope(s, TeacherUserId), s.OpenRow, "partial", null, hash);

        (result.Conflict, result.ErrorCode).ShouldBe((true, OpenTextReviewErrorCodes.NotSubmitted));
    }

    // ---------- çözüm görünürlüğü / test oturumu ----------

    [Fact]
    public async Task StudentSolutions_ContainSampleAnswerButNeverRubric()
    {
        var s = await SeedAsync();
        await CheckTextAsync(s, "Su ısınınca buharlaşır.");

        await using var ctx = _db.NewContext();
        var solutions = await new TestSessionService(ctx).GetActivitySolutionsAsync(s.InstanceId, StudentUserId);

        var open = solutions.Questions.Single(q => q.InstanceQuestionId == s.OpenRow);
        open.InteractionSolution.ShouldBe("""{"sampleAnswer":"Su ısınınca buharlaşır."}""");
        open.InteractionSolution.ShouldNotContain("rubric");
    }

    [Fact]
    public async Task OpenTextInTest_WritesNoAnswerSubmittedEvent_AndIsNotQueued()
    {
        var s = await SeedAsync(WorksheetKind.Test);

        (await SaveAsync(s.InstanceId, s.OpenRow, Payload("Su ısınınca buharlaşır."))).Success.ShouldBeTrue();
        await using (var ctx = _db.NewContext())
            (await new TestSessionService(ctx).EndTest(s.InstanceId, StudentUserId)).Success.ShouldBeTrue();

        (await OutboxCountAsync()).ShouldBe(0);
        (await QueueAsync(SchoolScope.Unrestricted(AdminUserId), "all")).TotalCount.ShouldBe(0);
        var row = await StudentRowAsync(s, s.OpenRow, includeCorrectAnswer: true);
        row.OpenTextReview.ShouldBeNull();   // değerlendirme yalnız etkinlikte
        row.Question.InteractionSolution.ShouldBe("""{"sampleAnswer":"Su ısınınca buharlaşır."}""");
    }

    // ---------- review düzeltmeleri (O1, O2, 2, 7, 8, D3) ----------

    [Fact]
    public async Task ReviewByAnotherTeacher_IsReadOnly_OnlyReviewerOrAdminCanCorrect()
    {
        var s = await SeedAsync();
        await CheckTextAsync(s, "Su ısınınca buharlaşır.");
        var hash = (await QueueAsync(TeacherScope(s, GradeTeacherUserId))).Items.Single().AnswerHash;
        (await ReviewAsync(TeacherScope(s, GradeTeacherUserId), s.OpenRow, "partial", "Eksik.", hash)).Success.ShouldBeTrue();

        var other = (await QueueAsync(TeacherScope(s, TeacherUserId), "all")).Items.Single();
        other.CanEdit.ShouldBeFalse();
        (await QueueAsync(TeacherScope(s, GradeTeacherUserId), "all")).Items.Single().CanEdit.ShouldBeTrue();

        var denied = await ReviewAsync(TeacherScope(s, TeacherUserId), s.OpenRow, "sufficient", null, hash);
        (denied.Forbidden, denied.ErrorCode).ShouldBe((true, OpenTextReviewErrorCodes.Forbidden));

        (await QueueAsync(SchoolScope.Unrestricted(AdminUserId), "all")).Items.Single().CanEdit.ShouldBeTrue();
        var admin = await ReviewAsync(SchoolScope.Unrestricted(AdminUserId), s.OpenRow, "sufficient", null, hash);
        admin.Success.ShouldBeTrue(admin.Message);
        admin.Item!.Review!.ReviewedByUserId.ShouldBe(AdminUserId);
    }

    [Fact]
    public async Task Rubric_IsOnlyForQuestionOwnerAndAdmin_SampleAnswerForAll()
    {
        var s = await SeedAsync();
        await CheckTextAsync(s, "Su ısınınca buharlaşır.");

        var owner = (await QueueAsync(TeacherScope(s, TeacherUserId))).Items.Single();      // soruyu oluşturan
        var other = (await QueueAsync(TeacherScope(s, GradeTeacherUserId))).Items.Single();  // yalnız atama yapan
        var admin = (await QueueAsync(SchoolScope.Unrestricted(AdminUserId))).Items.Single();

        (owner.Rubric, admin.Rubric).ShouldBe(("Isı + buharlaşma.", "Isı + buharlaşma."));
        other.Rubric.ShouldBeNull();
        other.SampleAnswer.ShouldBe("Su ısınınca buharlaşır.");
    }

    [Fact]
    public async Task SessionOutsideAssignmentWindow_IsNotReviewable()
    {
        var s = await SeedAsync();
        await CheckTextAsync(s, "Su ısınınca buharlaşır.");
        await using (var ctx = _db.NewContext())
        {
            // Öğrenci hedefli atamanın penceresi oturum başlamadan bitmiş (StartTime > EndAt).
            await ctx.WorksheetAssignments.Where(a => a.StudentId != null)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.EndAt, DateTime.UtcNow.AddHours(-1)));
            // Sınıf ataması oturumdan SONRA başlıyor ve oturum bitmemiş (StartTime < StartAt, Started).
            await ctx.WorksheetAssignments.Where(a => a.GradeId != null)
                .ExecuteUpdateAsync(u => u.SetProperty(a => a.StartAt, DateTime.UtcNow.AddMinutes(-1)));
        }

        (await QueueAsync(TeacherScope(s, TeacherUserId), "all")).TotalCount.ShouldBe(0);
        (await QueueAsync(TeacherScope(s, GradeTeacherUserId), "all")).TotalCount.ShouldBe(0);

        // Bitmiş oturum, pencereden önce başlamış olsa da sınıf atamasında sayılır (AssignmentInstanceWindow).
        await using (var ctx = _db.NewContext())
            (await new TestSessionService(ctx).EndTest(s.InstanceId, StudentUserId)).Success.ShouldBeTrue();
        (await QueueAsync(TeacherScope(s, GradeTeacherUserId), "all")).TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task ItemNumber_FollowsThePlayersOrder()
    {
        var s = await SeedAsync();
        await CheckTextAsync(s, "Su ısınınca buharlaşır.");
        (await QueueAsync(TeacherScope(s, TeacherUserId))).Items.Single().ItemNumber.ShouldBe(1);

        // issue #685: sayfanın sırası değişse de başlamış oturumun madde numarası değişmez — numara oturumun kendi sırasından.
        await using (var ctx = _db.NewContext())
        {
            var openWq = await ctx.TestInstanceQuestions.Where(q => q.Id == s.OpenRow).Select(q => q.WorksheetQuestionId).SingleAsync();
            await ctx.TestQuestions.Where(wq => wq.Id == openWq).ExecuteUpdateAsync(u => u.SetProperty(wq => wq.Order, 9));
        }
        (await QueueAsync(TeacherScope(s, TeacherUserId))).Items.Single().ItemNumber.ShouldBe(1);

        await using (var ctx = _db.NewContext())
            await ctx.TestInstanceQuestions.Where(q => q.Id == s.OpenRow).ExecuteUpdateAsync(u => u.SetProperty(q => q.Order, 9));

        var item = (await QueueAsync(TeacherScope(s, TeacherUserId))).Items.Single();
        item.ItemNumber.ShouldBe(2);
        await using var check = _db.NewContext();
        var player = await new TestSessionService(check).GetCanvasTestResultAsync(s.InstanceId, StudentUserId);
        (player!.TestInstanceQuestions.FindIndex(q => q.Id == s.OpenRow) + 1).ShouldBe(item.ItemNumber);
    }

    [Fact]
    public async Task ClearedAnswer_HidesTheOldReviewFromTheStudent()
    {
        var s = await SeedAsync();
        await CheckTextAsync(s, "Su ısınınca buharlaşır.");
        var hash = (await QueueAsync(TeacherScope(s, TeacherUserId))).Items.Single().AnswerHash;
        await ReviewAsync(TeacherScope(s, TeacherUserId), s.OpenRow, "partial", "Isıyı yaz.", hash);

        (await SaveAsync(s.InstanceId, s.OpenRow, Payload("   "))).Success.ShouldBeTrue();

        (await StudentRowAsync(s, s.OpenRow)).OpenTextReview.ShouldBeNull();
        var check = await CheckAsync(s.InstanceId, StudentUserId,
            new ActivityCheckAnswerDto { InstanceQuestionId = s.OpenRow, AnswerPayload = Payload("") });
        check.Questions.Single().OpenTextReview.ShouldBeNull();
        (await QueueAsync(TeacherScope(s, TeacherUserId), "all")).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task TimedOutStartedSession_CountsAsSubmitted_LikeTheStudentView()
    {
        var s = await SeedAsync();
        (await SaveAsync(s.InstanceId, s.OpenRow, Payload("Taslak ama süre doldu."))).Success.ShouldBeTrue();
        (await QueueAsync(TeacherScope(s, TeacherUserId))).TotalCount.ShouldBe(0);

        // Süre sınırı (+ tolerans) dolmuş, DB'de hâlâ Started: etkin durum Expired → gönderilmiş.
        await using (var ctx = _db.NewContext())
            await ctx.TestInstances.Where(i => i.Id == s.InstanceId)
                .ExecuteUpdateAsync(u => u.SetProperty(i => i.MaxDurationSeconds, (int?)60));

        var item = (await QueueAsync(TeacherScope(s, TeacherUserId))).Items.ShouldHaveSingleItem();
        (await StudentRowAsync(s, s.OpenRow)).OpenTextReview!.Status.ShouldBe(OpenTextReviewStatuses.Pending);
        (await ReviewAsync(TeacherScope(s, TeacherUserId), s.OpenRow, "partial", null, item.AnswerHash)).Success.ShouldBeTrue();
    }

    [Theory]
    [InlineData("\u202Eters\u202C")]
    [InlineData("a\u2066b")]
    [InlineData("a\u0085b")]
    public async Task BidiOrC1Characters_AreRejectedInAnswerAndFeedback(string evil)
    {
        var s = await SeedAsync();

        var save = await SaveAsync(s.InstanceId, s.OpenRow, Payload("Metin " + evil + " burada"));
        save.InvalidInput.ShouldBeTrue();

        await CheckTextAsync(s, "Su ısınınca buharlaşır.");
        var hash = (await QueueAsync(TeacherScope(s, TeacherUserId))).Items.Single().AnswerHash;
        (await ReviewAsync(TeacherScope(s, TeacherUserId), s.OpenRow, "partial", "Not " + evil, hash)).InvalidInput.ShouldBeTrue();
    }

    [Fact]
    public async Task LegacyOpenTextRowInTest_IsExcludedFromScoreTotals()
    {
        var s = await SeedAsync(WorksheetKind.Test);
        (await SaveAsync(s.InstanceId, s.OpenRow, Payload("Su ısınınca buharlaşır."))).Success.ShouldBeTrue();
        await using (var ctx = _db.NewContext())
        {
            await new TestSessionService(ctx).SaveAnswer(
                new SaveAnswerDto { TestInstanceId = s.InstanceId, TestQuestionId = s.McqRow, SelectedAnswerId = s.RightAnswerId },
                new UserProfileDto { Id = StudentUserId, KeycloakId = "kc", Role = "Student" });
            (await new TestSessionService(ctx).EndTest(s.InstanceId, StudentUserId)).Success.ShouldBeTrue();
        }

        await using var read = _db.NewContext();
        var studentId = await read.Students.Where(x => x.UserId == StudentUserId).Select(x => x.Id).SingleAsync();
        var list = await new TestSessionService(read).GetCompletedTestsAsync(new StudentProfileDto { Id = studentId }, 1, 10);
        var summary = list.Items.Single();
        (summary.TotalQuestions, summary.CorrectAnswers, summary.WrongAnswers).ShouldBe((1, 1, 0));
    }
}
