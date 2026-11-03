using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Practice;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #532: soru kaydetme kuralları ve kayıtlı sorulardan oturum.</summary>
public class PracticeSavedQuestionsTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    private sealed record World(int StudentId, int GradeId, int SubjectId, int WorksheetId);

    private static StudentProfileDto Profile(World w) => new() { Id = w.StudentId, GradeId = w.GradeId };

    private async Task<World> SeedAsync(int userId = 55)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "7" };
        var subject = new Subject { Name = "Matematik" };
        ctx.AddRange(grade, subject);
        await ctx.SaveChangesAsync();
        var student = new Student { UserId = userId, StudentNumber = "S" + userId, SchoolName = "S", GradeId = grade.Id };
        var worksheet = new Worksheet { Name = "WS", Description = "", GradeId = grade.Id, SubjectId = subject.Id };
        ctx.AddRange(student, worksheet);
        await ctx.SaveChangesAsync();
        return new World(student.Id, grade.Id, subject.Id, worksheet.Id);
    }

    private async Task<int> AddSeenQuestionAsync(World w, bool seen = true)
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
        if (seen)
        {
            var session = new PracticeSession { StudentId = w.StudentId, GradeId = w.GradeId, StartTime = DateTime.UtcNow, Status = PracticeSessionStatus.Ended };
            session.Questions.Add(new PracticeSessionQuestion { QuestionId = q.Id, ShownAt = DateTime.UtcNow, AnsweredAt = DateTime.UtcNow, IsCorrect = true });
            ctx.PracticeSessions.Add(session);
        }
        await ctx.SaveChangesAsync();
        return q.Id;
    }

    private async Task<SaveQuestionResult> SaveAsync(World w, int questionId)
    {
        await using var ctx = _db.NewContext();
        return await new PracticeSessionService(ctx).SaveQuestionAsync(Profile(w), questionId);
    }

    private async Task<SavedQuestionsDto> ListAsync(World w)
    {
        await using var ctx = _db.NewContext();
        return await new PracticeSessionService(ctx).GetSavedQuestionsAsync(Profile(w));
    }

    [Fact]
    public async Task SeenQuestion_IsSaved_AndSavingTwice_KeepsOneRow()
    {
        var w = await SeedAsync();
        var q = await AddSeenQuestionAsync(w);

        (await SaveAsync(w, q)).ShouldBe(SaveQuestionResult.Saved);
        (await SaveAsync(w, q)).ShouldBe(SaveQuestionResult.Saved);

        (await ListAsync(w)).Items.ShouldHaveSingleItem().QuestionId.ShouldBe(q);
    }

    [Fact]
    public async Task UnseenQuestion_IsRejected()
    {
        var w = await SeedAsync();
        var q = await AddSeenQuestionAsync(w, seen: false);

        (await SaveAsync(w, q)).ShouldBe(SaveQuestionResult.NotAllowed);
    }

    [Fact]
    public async Task QuestionInATestInProgress_IsRejected()
    {
        var w = await SeedAsync();
        var q = await AddSeenQuestionAsync(w);
        await using (var ctx = _db.NewContext())
        {
            ctx.TestInstances.Add(new WorksheetInstance { StudentId = w.StudentId, WorksheetId = w.WorksheetId, Status = WorksheetInstanceStatus.Started, StartTime = DateTime.UtcNow });
            await ctx.SaveChangesAsync();
        }

        (await SaveAsync(w, q)).ShouldBe(SaveQuestionResult.NotAllowed);
    }

    [Fact]
    public async Task Unsave_ThenSaveAgain_Works()
    {
        var w = await SeedAsync();
        var q = await AddSeenQuestionAsync(w);
        await SaveAsync(w, q);

        await using (var ctx = _db.NewContext())
            await new PracticeSessionService(ctx).UnsaveQuestionAsync(w.StudentId, q);
        (await ListAsync(w)).TotalCount.ShouldBe(0);

        (await SaveAsync(w, q)).ShouldBe(SaveQuestionResult.Saved);
        (await ListAsync(w)).TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task OtherStudentsSavedQuestions_AreNotReturned()
    {
        var w = await SeedAsync();
        var other = await SeedAsync(56);
        var q = await AddSeenQuestionAsync(w);
        await SaveAsync(w, q);

        (await ListAsync(other)).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task RemovedQuestion_LeavesTheList()
    {
        var w = await SeedAsync();
        var q = await AddSeenQuestionAsync(w);
        await SaveAsync(w, q);
        await using (var ctx = _db.NewContext())
        {
            ctx.Questions.Remove(await ctx.Questions.FirstAsync(x => x.Id == q));
            await ctx.SaveChangesAsync();
        }

        (await ListAsync(w)).TotalCount.ShouldBe(0);
    }

    [Fact]
    public async Task Session_AsksSavedQuestions_ButEmptyListThrows()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();
        var service = new PracticeSessionService(ctx);
        await Should.ThrowAsync<InvalidOperationException>(() => service.StartAsync(Profile(w), new PracticeSessionStartDto { Saved = true }));

        var q = await AddSeenQuestionAsync(w);
        await AddSeenQuestionAsync(w);
        await SaveAsync(w, q);

        var session = await service.StartAsync(Profile(w), new PracticeSessionStartDto { Saved = true });
        (await service.NextQuestionAsync(session.Id, w.StudentId))!.Question!.Id.ShouldBe(q);
    }

    public void Dispose() => _db.Dispose();
}
