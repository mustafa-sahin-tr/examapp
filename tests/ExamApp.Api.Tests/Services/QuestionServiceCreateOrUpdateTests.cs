using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

public class QuestionServiceCreateOrUpdateTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();
    private QuestionService NewService(AppDbContext ctx) => new(ctx, new ImageHelper(), _minio);

    [Fact]
    public async Task Creating_a_new_question_persists_answers_the_correct_answer_and_an_outbox_event()
    {
        int worksheetId;
        await using (var ctx = _db.NewContext())
        {
            var grade = new Grade { Name = "5" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();
            var ws = new Worksheet { Name = "W", Description = "", GradeId = grade.Id };
            ctx.Worksheets.Add(ws);
            await ctx.SaveChangesAsync();
            worksheetId = ws.Id;
        }

        var dto = new QuestionDto
        {
            Id = 0,
            Text = "2 + 2 = ?",
            Point = 5,
            TestId = worksheetId,
            Answers = new List<AnswerDto>
            {
                new() { Text = "3", IsCorrect = false },
                new() { Text = "4", IsCorrect = true },
                new() { Text = "", IsCorrect = false }, // blank -> skipped
            },
        };

        QuestionSavedDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).CreateOrUpdateQuestion(dto);

        result.Success.ShouldBeFalse(); // Success is only true for updates in this service
        result.Message.ShouldContain("kaydedildi");

        await using var check = _db.NewContext();
        var q = await check.Questions.Include(x => x.Answers).SingleAsync(x => x.Id == result.QuestionId);
        q.Answers.Select(a => a.Text).ShouldBe(new[] { "3", "4" });
        q.CorrectAnswerId.ShouldBe(q.Answers.Single(a => a.Text == "4").Id);

        (await check.TestQuestions.CountAsync(tq => tq.TestId == worksheetId && tq.QuestionId == q.Id)).ShouldBe(1);

        var outbox = await check.OutboxMessages.SingleAsync();
        outbox.Type.ShouldBe(OutboxEventRegistry.NameFor<QuestionCreatedEvent>());
    }

    [Fact]
    public async Task Updating_an_existing_question_rewrites_scalar_fields_and_answers()
    {
        int qId;
        await using (var ctx = _db.NewContext())
        {
            var existing = new Question { Text = "eski", SubText = "s", Point = 1, BookName = "b" };
            ctx.Questions.Add(existing);
            await ctx.SaveChangesAsync();
            qId = existing.Id;
            ctx.Answers.Add(new Answer { QuestionId = existing.Id, Text = "eski cevap" });
            await ctx.SaveChangesAsync();
        }

        var dto = new QuestionDto
        {
            Id = qId,
            Text = "yeni",
            SubText = "yeni alt",
            Point = 9,
            BookName = "yeni kitap",
            Answers = new List<AnswerDto> { new() { Text = "A", IsCorrect = true }, new() { Text = "B" } },
        };

        QuestionSavedDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).CreateOrUpdateQuestion(dto);

        result.Success.ShouldBeTrue();
        result.Message.ShouldContain("güncellendi");

        await using var check = _db.NewContext();
        var q = await check.Questions.Include(x => x.Answers).SingleAsync(x => x.Id == qId);
        q.Text.ShouldBe("yeni");
        q.Point.ShouldBe(9);
        q.Answers.Select(a => a.Text).OrderBy(t => t).ShouldBe(new[] { "A", "B" });
    }

    [Fact]
    public async Task Updating_a_missing_question_returns_a_failure_result()
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateOrUpdateQuestion(new QuestionDto { Id = 999999, Text = "x" });
        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("bulunamadı");
    }

    [Fact]
    public async Task An_example_question_stores_the_practice_answer_instead_of_answer_rows()
    {
        var dto = new QuestionDto
        {
            Id = 0,
            Text = "örnek",
            IsExample = true,
            PracticeCorrectAnswer = "B",
            Answers = new List<AnswerDto>(),
        };

        QuestionSavedDto result;
        await using (var ctx = _db.NewContext())
            result = await NewService(ctx).CreateOrUpdateQuestion(dto);

        await using var check = _db.NewContext();
        var q = await check.Questions.Include(x => x.Answers).SingleAsync(x => x.Id == result.QuestionId);
        q.IsExample.ShouldBeTrue();
        q.PracticeCorrectAnswer.ShouldBe("B");
        q.Answers.ShouldBeEmpty();
    }

    public void Dispose() => _db.Dispose();
}
