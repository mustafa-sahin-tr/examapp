using System.Text.Json;
using ExamApp.Api.Data;

using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

public class TestSessionServiceAnswerTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    private TestSessionService NewService(AppDbContext ctx) => new(ctx);

    private const int UserId = 55;

    private sealed record Seeded(int InstanceId, int TiqId, int CorrectAnswerId, int WrongAnswerId);

    /// <summary>Seeds a started test instance with one MCQ question and two answers.</summary>
    private async Task<Seeded> SeedInstanceAsync(string? interactionType = null, int? subTopicId = null)
    {
        await using var ctx = _db.NewContext();

        var subject = new Subject { Name = "Matematik" };
        var grade = new Grade { Name = "3" };
        ctx.AddRange(subject, grade);
        await ctx.SaveChangesAsync();

        var question = new Question
        {
            Text = "1 + 1 = ?",
            SubjectId = subject.Id,
            TopicId = null,
            Point = 10,
            DifficultyLevel = 2,
            InteractionType = interactionType,
        };
        ctx.Questions.Add(question);
        await ctx.SaveChangesAsync();

        var correct = new Answer { QuestionId = question.Id, Text = "2", Tag = "A" };
        var wrong = new Answer { QuestionId = question.Id, Text = "3", Tag = "B" };
        ctx.Answers.AddRange(correct, wrong);
        await ctx.SaveChangesAsync();
        question.CorrectAnswerId = correct.Id;
        await ctx.SaveChangesAsync();

        if (subTopicId is { } stId)
        {
            var topic = new Topic { Name = "T", SubjectId = subject.Id, GradeId = grade.Id };
            ctx.Topics.Add(topic);
            await ctx.SaveChangesAsync();
            var st = new SubTopic { Id = stId, Name = "ST", TopicId = topic.Id };
            ctx.SubTopics.Add(st);
            await ctx.SaveChangesAsync();
            ctx.QuestionSubTopics.Add(new QuestionSubTopic { QuestionId = question.Id, SubTopicId = stId });
            await ctx.SaveChangesAsync();
        }

        var worksheet = new Worksheet { Name = "WS", Description = "", SubjectId = subject.Id, GradeId = grade.Id };
        ctx.Worksheets.Add(worksheet);
        await ctx.SaveChangesAsync();

        var student = new Student { UserId = UserId, StudentNumber = "S1", SchoolName = "School" };
        ctx.Students.Add(student);
        await ctx.SaveChangesAsync();

        var instance = new WorksheetInstance
        {
            StudentId = student.Id, WorksheetId = worksheet.Id,
            StartTime = DateTime.UtcNow, Status = WorksheetInstanceStatus.Started,
        };
        ctx.Add(instance);
        var wq = new WorksheetQuestion { TestId = worksheet.Id, QuestionId = question.Id, Order = 1 };
        ctx.TestQuestions.Add(wq);
        await ctx.SaveChangesAsync();

        var tiq = new WorksheetInstanceQuestion { WorksheetInstanceId = instance.Id, WorksheetQuestionId = wq.Id };
        ctx.TestInstanceQuestions.Add(tiq);
        await ctx.SaveChangesAsync();

        return new Seeded(instance.Id, tiq.Id, correct.Id, wrong.Id);
    }

    private static SaveAnswerDto Dto(int instanceId, int tiqId, int selected, int timeTaken = 30) => new()
    {
        TestInstanceId = instanceId,
        TestQuestionId = tiqId,
        SelectedAnswerId = selected,
        TimeTaken = timeTaken,
    };

    private static UserProfileDto User => new() { Id = UserId, KeycloakId = "kc-55" };

    [Fact]
    public async Task Unknown_instance_question_returns_a_failure()
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).SaveAnswer(Dto(999, 999, 1), User);
        result.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task A_correct_answer_is_scored_and_an_outbox_event_is_written()
    {
        var s = await SeedInstanceAsync(subTopicId: 7);

        await using (var ctx = _db.NewContext())
        {
            var result = await NewService(ctx).SaveAnswer(Dto(s.InstanceId, s.TiqId, selected: s.CorrectAnswerId, timeTaken: 25), User);
            result.Success.ShouldBeTrue();
        }

        await using var check = _db.NewContext();
        var tiq = await check.TestInstanceQuestions.FirstAsync(x => x.Id == s.TiqId);
        tiq.IsCorrect.ShouldBeTrue();
        tiq.SelectedAnswerId.ShouldBe(s.CorrectAnswerId);
        tiq.TimeTaken.ShouldBe(25);

        var outbox = await check.OutboxMessages.SingleAsync();
        outbox.Type.ShouldBe("ExamApp.Foundation.Contracts.AnswerSubmittedEvent");
        outbox.ProcessedAt.ShouldBeNull();

        var evt = JsonSerializer.Deserialize<AnswerSubmittedEvent>(outbox.Content)!;
        evt.UserId.ShouldBe(UserId);
        evt.IsCorrect.ShouldBeTrue();
        evt.QuestionPoint.ShouldBe(10);
        evt.DifficultyLevel.ShouldBe(2);
        evt.TimeTakenInSeconds.ShouldBe(25);
        evt.ClientId.ShouldBe("kc-55");
        evt.SubTopicId.ShouldBe(7);
        evt.Subject.ShouldBe("Matematik");
    }

    [Fact]
    public async Task A_wrong_answer_is_marked_incorrect_but_still_emits_an_event()
    {
        var s = await SeedInstanceAsync();

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).SaveAnswer(Dto(s.InstanceId, s.TiqId, selected: s.WrongAnswerId), User)).Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        (await check.TestInstanceQuestions.FirstAsync(x => x.Id == s.TiqId)).IsCorrect.ShouldBeFalse();
        var evt = JsonSerializer.Deserialize<AnswerSubmittedEvent>((await check.OutboxMessages.SingleAsync()).Content)!;
        evt.IsCorrect.ShouldBeFalse();
    }

    [Fact]
    public async Task DragDropLabeling_answers_are_never_auto_scored_correct()
    {
        var s = await SeedInstanceAsync(interactionType: "dragDropLabeling");

        await using (var ctx = _db.NewContext())
            await NewService(ctx).SaveAnswer(Dto(s.InstanceId, s.TiqId, selected: s.CorrectAnswerId), User);

        await using var check = _db.NewContext();
        (await check.TestInstanceQuestions.FirstAsync(x => x.Id == s.TiqId)).IsCorrect.ShouldBeFalse();
    }

    [Fact]
    public async Task Another_students_answer_cannot_touch_this_instance()
    {
        var s = await SeedInstanceAsync();
        var otherUser = new UserProfileDto { Id = 999, KeycloakId = "kc-999" };

        await using var ctx = _db.NewContext();
        (await NewService(ctx).SaveAnswer(Dto(s.InstanceId, s.TiqId, s.CorrectAnswerId), otherUser)).Success.ShouldBeFalse();
    }

    public void Dispose() => _db.Dispose();
}
