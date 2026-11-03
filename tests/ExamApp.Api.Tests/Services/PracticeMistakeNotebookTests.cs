using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Practice;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #698: yanlış defteri listesi, oturumu ve neden etiketi.</summary>
public class PracticeMistakeNotebookTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    private static DateTime DaysAgo(int days) => DateTime.UtcNow.AddDays(-days);

    private sealed record World(int StudentId, int GradeId, int SubjectId, int WorksheetId);

    private static StudentProfileDto Profile(World w) => new() { Id = w.StudentId, GradeId = w.GradeId };

    private async Task<World> SeedAsync()
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

    private async Task<int> AddQuestionAsync(World w)
    {
        await using var ctx = _db.NewContext();
        var q = new Question { Text = "?", SubjectId = w.SubjectId, Point = 1, DifficultyLevel = 1 };
        ctx.Questions.Add(q);
        await ctx.SaveChangesAsync();
        var correct = new Answer { QuestionId = q.Id, Text = "ok", Tag = "A", Order = 0 };
        ctx.Answers.Add(correct);
        await ctx.SaveChangesAsync();
        q.CorrectAnswerId = correct.Id;
        ctx.TestQuestions.Add(new WorksheetQuestion { TestId = w.WorksheetId, QuestionId = q.Id, Order = 1 });
        await ctx.SaveChangesAsync();
        return q.Id;
    }

    private async Task AnswerAsync(World w, int questionId, params (int DaysAgo, bool Correct)[] answers)
    {
        await using var ctx = _db.NewContext();
        foreach (var (days, correct) in answers)
        {
            var session = new PracticeSession { StudentId = w.StudentId, GradeId = w.GradeId, StartTime = DaysAgo(days), Status = PracticeSessionStatus.Ended };
            session.Questions.Add(new PracticeSessionQuestion { QuestionId = questionId, ShownAt = DaysAgo(days), AnsweredAt = DaysAgo(days), IsCorrect = correct });
            ctx.PracticeSessions.Add(session);
        }
        await ctx.SaveChangesAsync();
    }

    private async Task<MistakeNotebookDto> ListAsync(World w)
    {
        await using var ctx = _db.NewContext();
        return await new PracticeSessionService(ctx).GetMistakesAsync(Profile(w), null, null);
    }

    [Fact]
    public async Task WrongAnswer_IsListedImmediately_WithCounts()
    {
        var w = await SeedAsync();
        var q = await AddQuestionAsync(w);
        await AnswerAsync(w, q, (3, false), (0, false));

        var item = (await ListAsync(w)).Items.ShouldHaveSingleItem();
        item.QuestionId.ShouldBe(q);
        item.WrongCount.ShouldBe(2);
    }

    [Fact]
    public async Task OneCorrect_Stays_TwoInARow_Leaves_NewWrong_Returns()
    {
        var w = await SeedAsync();
        var q = await AddQuestionAsync(w);
        await AnswerAsync(w, q, (5, false), (4, true));
        (await ListAsync(w)).TotalCount.ShouldBe(1);

        await AnswerAsync(w, q, (3, true));
        (await ListAsync(w)).TotalCount.ShouldBe(0);

        await AnswerAsync(w, q, (2, false));
        (await ListAsync(w)).TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task NeverWrong_IsNotListed()
    {
        var w = await SeedAsync();
        var q = await AddQuestionAsync(w);
        await AnswerAsync(w, q, (1, true));
        (await ListAsync(w)).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task QuestionInATestInProgress_IsNotListed()
    {
        var w = await SeedAsync();
        var q = await AddQuestionAsync(w);
        await AnswerAsync(w, q, (2, false));
        await using (var ctx = _db.NewContext())
        {
            ctx.TestInstances.Add(new WorksheetInstance { StudentId = w.StudentId, WorksheetId = w.WorksheetId, Status = WorksheetInstanceStatus.Started, StartTime = DaysAgo(0) });
            await ctx.SaveChangesAsync();
        }

        (await ListAsync(w)).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task RemovedQuestion_IsNotListed()
    {
        var w = await SeedAsync();
        var q = await AddQuestionAsync(w);
        await AnswerAsync(w, q, (2, false));
        await using (var ctx = _db.NewContext())
        {
            ctx.Questions.Remove(await ctx.Questions.FirstAsync(x => x.Id == q));
            await ctx.SaveChangesAsync();
        }

        (await ListAsync(w)).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Session_AsksOnlyNotebookQuestions()
    {
        var w = await SeedAsync();
        var q = await AddQuestionAsync(w);
        await AddQuestionAsync(w);
        await AnswerAsync(w, q, (0, false));

        await using var ctx = _db.NewContext();
        var service = new PracticeSessionService(ctx);
        var session = await service.StartAsync(Profile(w), new PracticeSessionStartDto { Mistakes = true });

        (await service.NextQuestionAsync(session.Id, w.StudentId))!.Question!.Id.ShouldBe(q);
    }

    [Fact]
    public async Task Session_EmptyNotebook_Throws()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();
        await Should.ThrowAsync<InvalidOperationException>(() =>
            new PracticeSessionService(ctx).StartAsync(Profile(w), new PracticeSessionStartDto { Mistakes = true }));
    }

    [Fact]
    public async Task Reason_CanBeSet_Cleared_AndSetAgain()
    {
        var w = await SeedAsync();
        var q = await AddQuestionAsync(w);
        await AnswerAsync(w, q, (1, false));

        async Task SetAsync(MistakeReason? reason)
        {
            await using var ctx = _db.NewContext();
            (await new PracticeSessionService(ctx).SetMistakeReasonAsync(Profile(w), q, reason)).ShouldBeTrue();
        }

        await SetAsync(MistakeReason.Calculation);
        (await ListAsync(w)).Items[0].Reason.ShouldBe("Calculation");

        await SetAsync(null);
        (await ListAsync(w)).Items[0].Reason.ShouldBeNull();

        await SetAsync(MistakeReason.Time);
        (await ListAsync(w)).Items[0].Reason.ShouldBe("Time");
    }

    [Fact]
    public async Task Reason_QuestionNotInNotebook_ReturnsFalse()
    {
        var w = await SeedAsync();
        var q = await AddQuestionAsync(w);

        await using var ctx = _db.NewContext();
        (await new PracticeSessionService(ctx).SetMistakeReasonAsync(Profile(w), q, MistakeReason.Time)).ShouldBeFalse();
    }

    public void Dispose() => _db.Dispose();
}
