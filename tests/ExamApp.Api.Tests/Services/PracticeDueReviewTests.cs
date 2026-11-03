using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Practice;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #529: aralıklı tekrar kuralı, tekrar listesi ve sabit listeli tekrar oturumu.</summary>
public class PracticeDueReviewTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    private static PracticeSessionService NewService(AppDbContext ctx) => new(ctx);

    private static DateTime DaysAgo(int days) => DateTime.UtcNow.AddDays(-days);

    private sealed record World(int StudentId, int GradeId, int SubjectId, int WorksheetId);

    private static StudentProfileDto Profile(World w) => new() { Id = w.StudentId, GradeId = w.GradeId };

    private async Task<World> SeedWorldAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "7" };
        var subject = new Subject { Name = "Matematik" };
        ctx.AddRange(grade, subject);
        await ctx.SaveChangesAsync();

        var student = new Student { UserId = 55, StudentNumber = "S1", SchoolName = "S", GradeId = grade.Id };
        var worksheet = new Worksheet { Name = "WS", Description = "", GradeId = grade.Id, SubjectId = subject.Id };
        ctx.AddRange(student, worksheet);
        await ctx.SaveChangesAsync();
        return new World(student.Id, grade.Id, subject.Id, worksheet.Id);
    }

    private async Task<int> AddQuestionAsync(World w, int? worksheetId = null)
    {
        await using var ctx = _db.NewContext();
        var q = new Question { Text = "?", SubjectId = w.SubjectId, Point = 1, DifficultyLevel = 1 };
        ctx.Questions.Add(q);
        await ctx.SaveChangesAsync();
        var correct = new Answer { QuestionId = q.Id, Text = "ok", Tag = "A", Order = 0 };
        ctx.Answers.Add(correct);
        await ctx.SaveChangesAsync();
        q.CorrectAnswerId = correct.Id;
        ctx.TestQuestions.Add(new WorksheetQuestion { TestId = worksheetId ?? w.WorksheetId, QuestionId = q.Id, Order = 1 });
        await ctx.SaveChangesAsync();
        return q.Id;
    }

    private async Task AnswerAsync(World w, int questionId, params (int DaysAgo, bool Correct)[] answers)
    {
        await using var ctx = _db.NewContext();
        foreach (var (days, correct) in answers)
        {
            var session = new PracticeSession { StudentId = w.StudentId, GradeId = w.GradeId, StartTime = DaysAgo(days), Status = PracticeSessionStatus.Ended };
            session.Questions.Add(new PracticeSessionQuestion
            {
                QuestionId = questionId,
                ShownAt = DaysAgo(days),
                AnsweredAt = DaysAgo(days),
                IsCorrect = correct
            });
            ctx.PracticeSessions.Add(session);
        }
        await ctx.SaveChangesAsync();
    }

    private async Task<List<int>> DueIdsAsync(World w)
    {
        await using var ctx = _db.NewContext();
        var due = await NewService(ctx).GetDueReviewAsync(Profile(w), null, 15);
        return due.Items.Select(i => i.QuestionId).ToList();
    }

    // ---- kural ----

    private static DueReviewStatus? Evaluate(params (DateTime At, bool Correct)[] attempts) =>
        DueReviewSchedule.Evaluate(attempts.Select(a => new DueReviewAttempt(a.At, a.Correct)), TimeZoneInfo.Utc);

    private static readonly DateTime Day0 = new(2026, 1, 1, 10, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void Evaluate_NeverWrong_IsNotDue() => Evaluate((Day0, true)).ShouldBeNull();

    [Fact]
    public void Evaluate_WrongOnce_IsDueNextDay()
    {
        var status = Evaluate((Day0, false))!.Value;
        status.Step.ShouldBe(0);
        status.DueDay.ShouldBe(new DateOnly(2026, 1, 2));
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(2, 7)]
    [InlineData(3, 21)]
    public void Evaluate_IntervalGrowsFromTheLastAnswer(int corrects, int interval)
    {
        var attempts = new List<(DateTime, bool)> { (Day0, false) };
        for (var i = 1; i <= corrects; i++)
            attempts.Add((Day0.AddDays(i), true));

        var status = Evaluate(attempts.ToArray())!.Value;
        status.Step.ShouldBe(corrects);
        status.DueDay.ShouldBe(DateOnly.FromDateTime(Day0.AddDays(corrects + interval)));
    }

    [Fact]
    public void Evaluate_FourCorrectAfterWrong_LeavesTheList() =>
        Evaluate((Day0, false), (Day0.AddDays(1), true), (Day0.AddDays(2), true), (Day0.AddDays(3), true), (Day0.AddDays(4), true))
            .ShouldBeNull();

    [Fact]
    public void Evaluate_WrongAgain_RestartsTheSteps()
    {
        var status = Evaluate((Day0, false), (Day0.AddDays(1), true), (Day0.AddDays(2), false))!.Value;
        status.Step.ShouldBe(0);
        status.WrongCount.ShouldBe(2);
    }

    // ---- liste ----

    [Fact]
    public async Task WrongYesterday_IsListed_WrongToday_IsNot()
    {
        var w = await SeedWorldAsync();
        var yesterday = await AddQuestionAsync(w);
        var today = await AddQuestionAsync(w);
        await AnswerAsync(w, yesterday, (1, false));
        await AnswerAsync(w, today, (0, false));

        (await DueIdsAsync(w)).ShouldBe([yesterday]);
    }

    [Fact]
    public async Task QuestionInAnActiveAssignment_IsNotListed()
    {
        var w = await SeedWorldAsync();
        int assignedWorksheet;
        await using (var ctx = _db.NewContext())
        {
            var ws = new Worksheet { Name = "A", Description = "", GradeId = w.GradeId, SubjectId = w.SubjectId };
            ctx.Worksheets.Add(ws);
            await ctx.SaveChangesAsync();
            assignedWorksheet = ws.Id;
            ctx.WorksheetAssignments.Add(new WorksheetAssignment { WorksheetId = ws.Id, StudentId = w.StudentId, StartAt = DaysAgo(1) });
            await ctx.SaveChangesAsync();
        }
        var question = await AddQuestionAsync(w, assignedWorksheet);
        await AnswerAsync(w, question, (2, false));

        (await DueIdsAsync(w)).ShouldBeEmpty();
    }

    [Fact]
    public async Task WrongAnswerInACompletedTest_IsListed_ButNotInAnUnfinishedOne()
    {
        var w = await SeedWorldAsync();
        var question = await AddQuestionAsync(w);
        int instanceId;
        await using (var ctx = _db.NewContext())
        {
            var wq = await ctx.TestQuestions.FirstAsync(q => q.QuestionId == question);
            var instance = new WorksheetInstance { StudentId = w.StudentId, WorksheetId = w.WorksheetId, Status = WorksheetInstanceStatus.Started, StartTime = DaysAgo(3) };
            ctx.TestInstances.Add(instance);
            await ctx.SaveChangesAsync();
            instanceId = instance.Id;
            ctx.TestInstanceQuestions.Add(new WorksheetInstanceQuestion { WorksheetInstanceId = instance.Id, WorksheetQuestionId = wq.Id, IsCorrect = false });
            await ctx.SaveChangesAsync();
        }

        (await DueIdsAsync(w)).ShouldBeEmpty();

        await using (var ctx = _db.NewContext())
        {
            var instance = await ctx.TestInstances.FirstAsync(i => i.Id == instanceId);
            instance.Status = WorksheetInstanceStatus.Completed;
            instance.EndTime = DaysAgo(2);
            await ctx.SaveChangesAsync();
        }

        (await DueIdsAsync(w)).ShouldBe([question]);
    }

    // ---- oturum ----

    [Fact]
    public async Task StartDueReview_NothingDue_Throws()
    {
        var w = await SeedWorldAsync();
        await using var ctx = _db.NewContext();
        await Should.ThrowAsync<InvalidOperationException>(() =>
            NewService(ctx).StartAsync(Profile(w), new PracticeSessionStartDto { DueReview = true }));
    }

    [Fact]
    public async Task DueReviewSession_AsksOnlyDueQuestions_ThenExhausts()
    {
        var w = await SeedWorldAsync();
        var due = await AddQuestionAsync(w);
        await AddQuestionAsync(w);
        await AnswerAsync(w, due, (1, false));

        await using var ctx = _db.NewContext();
        var service = NewService(ctx);
        var session = await service.StartAsync(Profile(w), new PracticeSessionStartDto { DueReview = true });
        session.IsDueReview.ShouldBeTrue();

        var first = await service.NextQuestionAsync(session.Id, w.StudentId);
        first!.Question!.Id.ShouldBe(due);

        var correctId = (await ctx.Questions.FirstAsync(q => q.Id == due)).CorrectAnswerId!.Value;
        await service.SubmitAnswerAsync(session.Id, w.StudentId, new PracticeAnswerSubmitDto { QuestionId = due, SelectedAnswerId = correctId });

        var second = await service.NextQuestionAsync(session.Id, w.StudentId);
        second!.PoolExhausted.ShouldBeTrue();
    }

    [Fact]
    public async Task CorrectAnswerInReview_AdvancesTheStep()
    {
        var w = await SeedWorldAsync();
        var question = await AddQuestionAsync(w);
        await AnswerAsync(w, question, (5, false), (4, true));

        await using var ctx = _db.NewContext();
        var due = await NewService(ctx).GetDueReviewAsync(Profile(w), null, 15);

        due.Items.ShouldHaveSingleItem().Step.ShouldBe(1);
    }

    public void Dispose() => _db.Dispose();
}
