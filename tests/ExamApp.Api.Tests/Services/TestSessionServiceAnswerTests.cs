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

    // ---- issue #279 review (blocker): AnswerRevision — DB-generated, atomically incrementing ----

    [Fact]
    public async Task First_SaveAnswer_bumps_AnswerRevision_from_zero_to_one_and_carries_it_on_the_event()
    {
        var s = await SeedInstanceAsync();

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).SaveAnswer(Dto(s.InstanceId, s.TiqId, s.CorrectAnswerId), User)).Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        (await check.TestInstanceQuestions.FirstAsync(x => x.Id == s.TiqId)).AnswerRevision.ShouldBe(1);

        var evt = JsonSerializer.Deserialize<AnswerSubmittedEvent>((await check.OutboxMessages.SingleAsync()).Content)!;
        evt.Revision.ShouldBe(1);
        evt.TestInstanceQuestionId.ShouldBe(s.TiqId);
    }

    [Fact]
    public async Task Repeated_SaveAnswer_calls_for_the_same_question_strictly_increase_AnswerRevision()
    {
        var s = await SeedInstanceAsync();

        var revisions = new List<int>();
        foreach (var selected in new[] { s.CorrectAnswerId, s.WrongAnswerId, s.CorrectAnswerId })
        {
            await using var ctx = _db.NewContext();
            await NewService(ctx).SaveAnswer(Dto(s.InstanceId, s.TiqId, selected), User);
            await using var check = _db.NewContext();
            var evt = JsonSerializer.Deserialize<AnswerSubmittedEvent>(
                (await check.OutboxMessages.OrderBy(m => m.CreatedAt).LastAsync()).Content)!;
            revisions.Add(evt.Revision);
        }

        revisions.ShouldBe(new[] { 1, 2, 3 });
    }

    [Fact]
    public async Task A_failed_SaveAnswer_for_an_unknown_question_does_not_touch_any_AnswerRevision()
    {
        await using var ctx = _db.NewContext();
        (await NewService(ctx).SaveAnswer(Dto(999, 999, 1), User)).Success.ShouldBeFalse();
        // Nothing to assert on a row that was never created — this test documents that the atomic
        // revision increment only runs after the ownership/existence check succeeds (no wasted UPDATE).
    }

    // ---- issue #279 review (critical fix): SaveAnswer must not open a transaction OUTSIDE an execution
    // strategy — Aspire's AddNpgsqlDbContext enables Npgsql retry-on-failure in production, and EF Core
    // refuses to run a command inside a user-initiated transaction once a retrying strategy is configured
    // ("does not support user-initiated transactions"). SQLite's own default strategy never retries, so a
    // regression here is invisible against a plain TestDb.NewContext() — this test attaches
    // TestRetryingExecutionStrategy (RetriesOnFailure = true) to reproduce that failure mode. ----

    [Fact]
    public async Task SaveAnswer_succeeds_under_a_retrying_execution_strategy()
    {
        // Would throw InvalidOperationException ("... does not support user-initiated transactions ...")
        // if SaveAnswer opened Database.BeginTransactionAsync() outside CreateExecutionStrategy().ExecuteAsync.
        var s = await SeedInstanceAsync();

        await using (var ctx = _db.NewContextWithRetryingExecutionStrategy())
        {
            var result = await NewService(ctx).SaveAnswer(Dto(s.InstanceId, s.TiqId, s.CorrectAnswerId), User);
            result.Success.ShouldBeTrue();
        }

        // Fresh context for the read-back: `ctx` above still has the row tracked with its ORIGINAL
        // (pre-increment) in-memory AnswerRevision — the atomic increment only ever touched the DB via raw
        // SQL, so re-querying on the SAME context would return the stale cached instance (identity
        // resolution), not the persisted value. Same technique the other AnswerRevision tests use.
        await using var check = _db.NewContext();
        (await check.TestInstanceQuestions.FirstAsync(x => x.Id == s.TiqId)).AnswerRevision.ShouldBe(1);
    }

    [Fact]
    public async Task Repeated_SaveAnswer_calls_under_a_retrying_execution_strategy_still_increment_the_revision()
    {
        var s = await SeedInstanceAsync();

        await using (var ctx = _db.NewContextWithRetryingExecutionStrategy())
        {
            await NewService(ctx).SaveAnswer(Dto(s.InstanceId, s.TiqId, s.CorrectAnswerId), User);
            var second = await NewService(ctx).SaveAnswer(Dto(s.InstanceId, s.TiqId, s.WrongAnswerId), User);
            second.Success.ShouldBeTrue();
        }

        await using var check = _db.NewContext();
        (await check.TestInstanceQuestions.FirstAsync(x => x.Id == s.TiqId)).AnswerRevision.ShouldBe(2);
    }

    public void Dispose() => _db.Dispose();
}
