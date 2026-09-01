using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

public class QuestionServiceCreateTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();

    private QuestionService NewService(AppDbContext ctx) => new(ctx, new ImageHelper(), _minio);

    private static QuestionDto NewQuestionDto(string text = "1 + 1 = ?") => new()
    {
        Id = 0,
        Text = text,
        Point = 5,
        AnswerColCount = 2,
        Answers = new()
        {
            new AnswerDto { Text = "2", IsCorrect = true },
            new AnswerDto { Text = "3", IsCorrect = false },
        },
    };

    [Fact]
    public async Task Creating_a_question_writes_a_single_QuestionCreated_outbox_event()
    {
        int questionId;
        await using (var ctx = _db.NewContext())
        {
            var result = await NewService(ctx).CreateOrUpdateQuestion(NewQuestionDto("Yeni soru"));
            result.QuestionId.ShouldNotBeNull();
            result.QuestionId!.Value.ShouldBeGreaterThan(0);
            questionId = result.QuestionId!.Value;
        }

        await using var check = _db.NewContext();
        var outbox = await check.OutboxMessages.SingleAsync();
        outbox.Type.ShouldBe("ExamApp.Foundation.Contracts.QuestionCreatedEvent");
        outbox.ProcessedAt.ShouldBeNull();

        var evt = JsonSerializer.Deserialize<QuestionCreatedEvent>(outbox.Content)!;
        evt.QuestionId.ShouldBe(questionId);
        evt.Text.ShouldBe("Yeni soru");
    }

    [Fact]
    public async Task Creating_a_question_persists_it_with_its_answers_and_correct_answer()
    {
        int questionId;
        await using (var ctx = _db.NewContext())
            questionId = (await NewService(ctx).CreateOrUpdateQuestion(NewQuestionDto())).QuestionId!.Value;

        await using var check = _db.NewContext();
        var q = await check.Questions.Include(x => x.Answers).FirstAsync(x => x.Id == questionId);
        q.Answers.Count.ShouldBe(2);
        q.CorrectAnswerId.ShouldNotBeNull();
        q.Answers.ShouldContain(a => a.Id == q.CorrectAnswerId && a.Text == "2");
    }

    [Fact]
    public async Task Updating_an_existing_question_does_not_emit_an_outbox_event()
    {
        int questionId;
        await using (var ctx = _db.NewContext())
            questionId = (await NewService(ctx).CreateOrUpdateQuestion(NewQuestionDto())).QuestionId!.Value;

        // drop the create event so the assertion is unambiguous
        await using (var ctx = _db.NewContext())
        {
            ctx.OutboxMessages.RemoveRange(ctx.OutboxMessages);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
        {
            var update = NewQuestionDto("değişti");
            update.Id = questionId;
            await NewService(ctx).CreateOrUpdateQuestion(update);
        }

        await using var check = _db.NewContext();
        (await check.OutboxMessages.AnyAsync()).ShouldBeFalse();
        (await check.Questions.FirstAsync(x => x.Id == questionId)).Text.ShouldBe("değişti");
    }

    public void Dispose() => _db.Dispose();
}
