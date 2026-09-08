using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Practice;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #62 — havuz filtresi (sınıf eşleşmesi: Topic.GradeId ya da Worksheet.GradeId; Restricted hariç),
/// aynı oturumda tekrar yok, boş havuzda 200-uyumlu boş sonuç.
/// </summary>
public class PracticeSessionServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    private static PracticeSessionService NewService(AppDbContext ctx) => new(ctx);

    private sealed record World(int StudentId, int GradeId, int OtherGradeId, int SubjectId);

    private async Task<World> SeedWorldAsync()
    {
        await using var ctx = _db.NewContext();

        var grade = new Grade { Name = "7" };
        var other = new Grade { Name = "8" };
        var subject = new Subject { Name = "Matematik" };
        ctx.AddRange(grade, other, subject);
        await ctx.SaveChangesAsync();

        var student = new Student { UserId = 55, StudentNumber = "S1", SchoolName = "S", GradeId = grade.Id };
        ctx.Students.Add(student);
        await ctx.SaveChangesAsync();

        return new World(student.Id, grade.Id, other.Id, subject.Id);
    }

    private static StudentProfileDto Profile(World w) => new() { Id = w.StudentId, GradeId = w.GradeId };

    private async Task<int> AddTopicAsync(int subjectId, int gradeId)
    {
        await using var ctx = _db.NewContext();
        var topic = new Topic { Name = "T", SubjectId = subjectId, GradeId = gradeId };
        ctx.Topics.Add(topic);
        await ctx.SaveChangesAsync();
        return topic.Id;
    }

    private async Task<int> AddWorksheetAsync(int gradeId, int subjectId,
        WorksheetStudentVisibility visibility = WorksheetStudentVisibility.Normal)
    {
        await using var ctx = _db.NewContext();
        var ws = new Worksheet { Name = "WS", Description = "", GradeId = gradeId, SubjectId = subjectId, StudentVisibility = visibility };
        ctx.Worksheets.Add(ws);
        await ctx.SaveChangesAsync();
        return ws.Id;
    }

    private sealed record SeededQuestion(int QuestionId, int CorrectAnswerId, int WrongAnswerId);

    /// <summary>MCQ soru + 2 şık; verilen worksheet'lere bağlar.</summary>
    private async Task<SeededQuestion> AddQuestionAsync(int subjectId, int? topicId, params int[] worksheetIds)
    {
        await using var ctx = _db.NewContext();

        var q = new Question { Text = "?", SubjectId = subjectId, TopicId = topicId, Point = 1, DifficultyLevel = 1 };
        ctx.Questions.Add(q);
        await ctx.SaveChangesAsync();

        var correct = new Answer { QuestionId = q.Id, Text = "ok", Tag = "A", Order = 0 };
        var wrong = new Answer { QuestionId = q.Id, Text = "no", Tag = "B", Order = 1 };
        ctx.Answers.AddRange(correct, wrong);
        await ctx.SaveChangesAsync();

        q.CorrectAnswerId = correct.Id;
        foreach (var wsId in worksheetIds)
            ctx.TestQuestions.Add(new WorksheetQuestion { TestId = wsId, QuestionId = q.Id, Order = 1 });
        await ctx.SaveChangesAsync();

        return new SeededQuestion(q.Id, correct.Id, wrong.Id);
    }

    private async Task<int> StartSessionAsync(World w, List<int>? subjectIds = null, List<int>? topicIds = null)
    {
        await using var ctx = _db.NewContext();
        var dto = await NewService(ctx).StartAsync(Profile(w), new PracticeSessionStartDto { SubjectIds = subjectIds, TopicIds = topicIds });
        return dto.Id;
    }

    // ---- start ----

    [Fact]
    public async Task StartAsync_StudentWithoutGrade_ThrowsInvalidOperation()
    {
        var w = await SeedWorldAsync();
        await using var ctx = _db.NewContext();

        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            NewService(ctx).StartAsync(new StudentProfileDto { Id = w.StudentId, GradeId = null }, new PracticeSessionStartDto()));
        ex.Message.ShouldContain("sınıf");
    }

    [Fact]
    public async Task StartAsync_PersistsScopeAndGrade()
    {
        var w = await SeedWorldAsync();
        await using var ctx = _db.NewContext();

        var dto = await NewService(ctx).StartAsync(Profile(w),
            new PracticeSessionStartDto { SubjectIds = new() { 3, 1, 3 }, TopicIds = new() { 9 } });

        dto.Status.ShouldBe("Active");
        dto.GradeId.ShouldBe(w.GradeId);
        dto.SubjectIds.ShouldBe(new List<int> { 1, 3 });
        dto.TopicIds.ShouldBe(new List<int> { 9 });

        var row = await ctx.PracticeSessions.AsNoTracking().SingleAsync(s => s.Id == dto.Id);
        row.StudentId.ShouldBe(w.StudentId);
        row.SubjectIdsJson.ShouldBe("[1,3]");
    }

    // ---- grade / visibility filtering ----

    [Fact]
    public async Task Next_TopicGradeMatches_QuestionIncluded()
    {
        var w = await SeedWorldAsync();
        var topic = await AddTopicAsync(w.SubjectId, w.GradeId);
        var ws = await AddWorksheetAsync(w.GradeId, w.SubjectId);
        var q = await AddQuestionAsync(w.SubjectId, topic, ws);
        var sessionId = await StartSessionAsync(w);

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).NextQuestionAsync(sessionId, w.StudentId);

        result.ShouldNotBeNull();
        result.PoolExhausted.ShouldBeFalse();
        result.Question.ShouldNotBeNull();
        result.Question.Id.ShouldBe(q.QuestionId);
        result.Question.CorrectAnswerId.ShouldBeNull();
        result.Question.Answers.Count.ShouldBe(2);
    }

    [Fact]
    public async Task Next_TopicGradeDiffers_QuestionExcludedEvenIfWorksheetGradeMatches()
    {
        var w = await SeedWorldAsync();
        var otherGradeTopic = await AddTopicAsync(w.SubjectId, w.OtherGradeId);
        var ws = await AddWorksheetAsync(w.GradeId, w.SubjectId);
        await AddQuestionAsync(w.SubjectId, otherGradeTopic, ws);
        var sessionId = await StartSessionAsync(w);

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).NextQuestionAsync(sessionId, w.StudentId);

        result.ShouldNotBeNull();
        result.PoolExhausted.ShouldBeTrue();
        result.Question.ShouldBeNull();
    }

    [Fact]
    public async Task Next_NoTopic_WorksheetGradeMatches_QuestionIncluded()
    {
        var w = await SeedWorldAsync();
        var ws = await AddWorksheetAsync(w.GradeId, w.SubjectId);
        var q = await AddQuestionAsync(w.SubjectId, topicId: null, ws);
        var sessionId = await StartSessionAsync(w);

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).NextQuestionAsync(sessionId, w.StudentId);

        result.ShouldNotBeNull();
        result.Question.ShouldNotBeNull();
        result.Question.Id.ShouldBe(q.QuestionId);
    }

    [Fact]
    public async Task Next_NoTopic_WorksheetGradeDiffers_QuestionExcluded()
    {
        var w = await SeedWorldAsync();
        var otherWs = await AddWorksheetAsync(w.OtherGradeId, w.SubjectId);
        await AddQuestionAsync(w.SubjectId, topicId: null, otherWs);
        var sessionId = await StartSessionAsync(w);

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).NextQuestionAsync(sessionId, w.StudentId);

        result.ShouldNotBeNull();
        result.PoolExhausted.ShouldBeTrue();
        result.Question.ShouldBeNull();
    }

    [Fact]
    public async Task Next_OnlyInRestrictedWorksheet_QuestionExcluded()
    {
        var w = await SeedWorldAsync();
        var restricted = await AddWorksheetAsync(w.GradeId, w.SubjectId, WorksheetStudentVisibility.Restricted);
        await AddQuestionAsync(w.SubjectId, topicId: null, restricted);
        var sessionId = await StartSessionAsync(w);

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).NextQuestionAsync(sessionId, w.StudentId);

        result.ShouldNotBeNull();
        result.PoolExhausted.ShouldBeTrue();
    }

    [Fact]
    public async Task Next_InRestrictedAndNormalWorksheet_QuestionIncluded()
    {
        var w = await SeedWorldAsync();
        var restricted = await AddWorksheetAsync(w.GradeId, w.SubjectId, WorksheetStudentVisibility.Restricted);
        var normal = await AddWorksheetAsync(w.GradeId, w.SubjectId);
        var q = await AddQuestionAsync(w.SubjectId, topicId: null, restricted, normal);
        var sessionId = await StartSessionAsync(w);

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).NextQuestionAsync(sessionId, w.StudentId);

        result!.Question.ShouldNotBeNull();
        result.Question.Id.ShouldBe(q.QuestionId);
    }

    [Fact]
    public async Task Next_SubjectScope_FiltersOtherSubjects()
    {
        var w = await SeedWorldAsync();
        int otherSubjectId;
        await using (var seed = _db.NewContext())
        {
            var s = new Subject { Name = "Türkçe" };
            seed.Subjects.Add(s);
            await seed.SaveChangesAsync();
            otherSubjectId = s.Id;
        }
        var ws = await AddWorksheetAsync(w.GradeId, w.SubjectId);
        await AddQuestionAsync(otherSubjectId, topicId: null, ws);
        var sessionId = await StartSessionAsync(w, subjectIds: new() { w.SubjectId });

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).NextQuestionAsync(sessionId, w.StudentId);

        result!.PoolExhausted.ShouldBeTrue();
    }

    // ---- no-repeat / empty pool ----

    [Fact]
    public async Task Next_EmptyPool_ReturnsPoolExhaustedNotException()
    {
        var w = await SeedWorldAsync();
        var sessionId = await StartSessionAsync(w);

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).NextQuestionAsync(sessionId, w.StudentId);

        result.ShouldNotBeNull();
        result.SessionId.ShouldBe(sessionId);
        result.Question.ShouldBeNull();
        result.PoolExhausted.ShouldBeTrue();
        result.AnsweredCount.ShouldBe(0);
    }

    [Fact]
    public async Task Next_NeverReturnsSameQuestionTwiceInOneSession_ThenExhausts()
    {
        var w = await SeedWorldAsync();
        var ws = await AddWorksheetAsync(w.GradeId, w.SubjectId);
        var seeded = new List<SeededQuestion>();
        for (var i = 0; i < 5; i++)
            seeded.Add(await AddQuestionAsync(w.SubjectId, topicId: null, ws));
        var sessionId = await StartSessionAsync(w);

        var served = new List<int>();
        for (var i = 0; i < 5; i++)
        {
            await using var ctx = _db.NewContext();
            var svc = NewService(ctx);

            var next = await svc.NextQuestionAsync(sessionId, w.StudentId);
            next!.Question.ShouldNotBeNull();
            served.Add(next.Question.Id);

            var q = seeded.Single(s => s.QuestionId == next.Question.Id);
            await svc.SubmitAnswerAsync(sessionId, w.StudentId,
                new PracticeAnswerSubmitDto { QuestionId = q.QuestionId, SelectedAnswerId = q.CorrectAnswerId, TimeTaken = 3 });
        }

        served.Distinct().Count().ShouldBe(5);
        served.ShouldBe(seeded.Select(s => s.QuestionId), ignoreOrder: true);

        await using var last = _db.NewContext();
        var exhausted = await NewService(last).NextQuestionAsync(sessionId, w.StudentId);
        exhausted!.PoolExhausted.ShouldBeTrue();
        exhausted.Question.ShouldBeNull();
        exhausted.AnsweredCount.ShouldBe(5);
        exhausted.CorrectCount.ShouldBe(5);
    }

    [Fact]
    public async Task Next_UnansweredPendingQuestion_IsReturnedAgainNotANewOne()
    {
        var w = await SeedWorldAsync();
        var ws = await AddWorksheetAsync(w.GradeId, w.SubjectId);
        await AddQuestionAsync(w.SubjectId, topicId: null, ws);
        await AddQuestionAsync(w.SubjectId, topicId: null, ws);
        var sessionId = await StartSessionAsync(w);

        await using var ctx = _db.NewContext();
        var svc = NewService(ctx);
        var first = await svc.NextQuestionAsync(sessionId, w.StudentId);
        var second = await svc.NextQuestionAsync(sessionId, w.StudentId);

        second!.Question!.Id.ShouldBe(first!.Question!.Id);
        (await ctx.PracticeSessionQuestions.CountAsync(p => p.PracticeSessionId == sessionId)).ShouldBe(1);
    }

    // ---- answer logging ----

    [Fact]
    public async Task SubmitAnswer_Correct_PersistsRowWithTimeTaken()
    {
        var w = await SeedWorldAsync();
        var ws = await AddWorksheetAsync(w.GradeId, w.SubjectId);
        var q = await AddQuestionAsync(w.SubjectId, topicId: null, ws);
        var sessionId = await StartSessionAsync(w);

        await using var ctx = _db.NewContext();
        var svc = NewService(ctx);
        await svc.NextQuestionAsync(sessionId, w.StudentId);

        var result = await svc.SubmitAnswerAsync(sessionId, w.StudentId,
            new PracticeAnswerSubmitDto { QuestionId = q.QuestionId, SelectedAnswerId = q.CorrectAnswerId, TimeTaken = 12 });

        result.ShouldNotBeNull();
        result.IsCorrect.ShouldBeTrue();
        result.Skipped.ShouldBeFalse();
        result.CorrectAnswerId.ShouldBe(q.CorrectAnswerId);
        result.AnsweredCount.ShouldBe(1);
        result.CorrectCount.ShouldBe(1);

        await using var verify = _db.NewContext();
        var row = await verify.PracticeSessionQuestions.AsNoTracking().SingleAsync(p => p.PracticeSessionId == sessionId);
        row.QuestionId.ShouldBe(q.QuestionId);
        row.SelectedAnswerId.ShouldBe(q.CorrectAnswerId);
        row.IsCorrect.ShouldBeTrue();
        row.IsSkipped.ShouldBeFalse();
        row.TimeTaken.ShouldBe(12);
        row.AnsweredAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task SubmitAnswer_Wrong_IsCorrectFalse()
    {
        var w = await SeedWorldAsync();
        var ws = await AddWorksheetAsync(w.GradeId, w.SubjectId);
        var q = await AddQuestionAsync(w.SubjectId, topicId: null, ws);
        var sessionId = await StartSessionAsync(w);

        await using var ctx = _db.NewContext();
        var svc = NewService(ctx);
        await svc.NextQuestionAsync(sessionId, w.StudentId);

        var result = await svc.SubmitAnswerAsync(sessionId, w.StudentId,
            new PracticeAnswerSubmitDto { QuestionId = q.QuestionId, SelectedAnswerId = q.WrongAnswerId, TimeTaken = 5 });

        result!.IsCorrect.ShouldBeFalse();
        result.CorrectAnswerId.ShouldBe(q.CorrectAnswerId);
        result.CorrectCount.ShouldBe(0);
    }

    [Fact]
    public async Task SubmitAnswer_Skipped_PersistsSkipAndDoesNotRepeat()
    {
        var w = await SeedWorldAsync();
        var ws = await AddWorksheetAsync(w.GradeId, w.SubjectId);
        var q = await AddQuestionAsync(w.SubjectId, topicId: null, ws);
        var sessionId = await StartSessionAsync(w);

        await using var ctx = _db.NewContext();
        var svc = NewService(ctx);
        await svc.NextQuestionAsync(sessionId, w.StudentId);

        var result = await svc.SubmitAnswerAsync(sessionId, w.StudentId,
            new PracticeAnswerSubmitDto { QuestionId = q.QuestionId, Skipped = true, TimeTaken = 2 });

        result!.Skipped.ShouldBeTrue();
        result.IsCorrect.ShouldBeFalse();

        var after = await svc.NextQuestionAsync(sessionId, w.StudentId);
        after!.PoolExhausted.ShouldBeTrue();

        var summary = await svc.GetAsync(sessionId, w.StudentId);
        summary!.SkippedCount.ShouldBe(1);
        summary.AnsweredCount.ShouldBe(1);
    }

    [Fact]
    public async Task SubmitAnswer_QuestionNotShownInSession_Throws()
    {
        var w = await SeedWorldAsync();
        var ws = await AddWorksheetAsync(w.GradeId, w.SubjectId);
        var q = await AddQuestionAsync(w.SubjectId, topicId: null, ws);
        var sessionId = await StartSessionAsync(w);

        await using var ctx = _db.NewContext();
        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            NewService(ctx).SubmitAnswerAsync(sessionId, w.StudentId,
                new PracticeAnswerSubmitDto { QuestionId = q.QuestionId, SelectedAnswerId = q.CorrectAnswerId }));
        ex.Message.ShouldContain("gösterilmedi");
    }

    [Fact]
    public async Task SubmitAnswer_AnswerFromAnotherQuestion_Throws()
    {
        var w = await SeedWorldAsync();
        var ws = await AddWorksheetAsync(w.GradeId, w.SubjectId);
        var q1 = await AddQuestionAsync(w.SubjectId, topicId: null, ws);
        var q2 = await AddQuestionAsync(w.SubjectId, topicId: null, ws);
        var sessionId = await StartSessionAsync(w);

        await using var ctx = _db.NewContext();
        var svc = NewService(ctx);
        var next = await svc.NextQuestionAsync(sessionId, w.StudentId);
        var shown = next!.Question!.Id == q1.QuestionId ? q1 : q2;
        var other = shown == q1 ? q2 : q1;

        var ex = await Should.ThrowAsync<InvalidOperationException>(() =>
            svc.SubmitAnswerAsync(sessionId, w.StudentId,
                new PracticeAnswerSubmitDto { QuestionId = shown.QuestionId, SelectedAnswerId = other.CorrectAnswerId }));
        ex.Message.ShouldContain("ait değil");
    }

    // ---- ownership / lifecycle ----

    [Fact]
    public async Task Next_SessionOfAnotherStudent_ReturnsNull()
    {
        var w = await SeedWorldAsync();
        var sessionId = await StartSessionAsync(w);

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).NextQuestionAsync(sessionId, studentId: w.StudentId + 1000);

        result.ShouldBeNull();
    }

    [Fact]
    public async Task EndAsync_MarksEnded_AndNextThenThrows()
    {
        var w = await SeedWorldAsync();
        var sessionId = await StartSessionAsync(w);

        await using var ctx = _db.NewContext();
        var svc = NewService(ctx);
        var ended = await svc.EndAsync(sessionId, w.StudentId);

        ended!.Status.ShouldBe("Ended");
        ended.EndTime.ShouldNotBeNull();

        var ex = await Should.ThrowAsync<InvalidOperationException>(() => svc.NextQuestionAsync(sessionId, w.StudentId));
        ex.Message.ShouldContain("sonlandırılmış");

        // idempotent
        var again = await svc.EndAsync(sessionId, w.StudentId);
        again!.EndTime.ShouldBe(ended.EndTime);
    }

    public void Dispose() => _db.Dispose();
}
