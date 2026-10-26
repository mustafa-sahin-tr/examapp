using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Practice;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #570: alıştırma (practice) akışı — <c>dragDropLabeling</c> planı her zaman çözümsüz; çözüm yalnız örnek soruda
/// ya da soru cevaplanıp/pas geçilip gözden geçirmede (review) açılır. Günlük set de aynı <c>NextQuestionAsync</c> yolundan geçer.
/// </summary>
public class PracticeSessionInteractionSolutionTests : IDisposable
{
    private const int StudentUserId = 55;
    private const string Solution = """{"placements":[{"zone":"z1","label":"l1"}]}""";
    private static readonly string PlanWithoutSolution = """{"labels":["l1","l2"],"zones":[{"id":"z1"}]}""";
    private static readonly string PlanWithEmbeddedSolution =
        """{"labels":["l1","l2"],"zones":[{"id":"z1"}],"solution":{"placements":[{"zone":"z1","label":"l1"}]}}""";

    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private static void ShouldHaveNoSolutionKey(string? plan)
    {
        plan.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(plan!);
        doc.RootElement.TryGetProperty("solution", out _).ShouldBeFalse();
    }

    private sealed record World(int StudentId, int GradeId);

    /// <summary>Öğrenci + sınıfa bağlı konu; havuzda bir dragDrop sorusu (çözüm kolonu/planı parametreyle).</summary>
    private async Task<(World World, int QuestionId)> SeedAsync(bool isExample, string plan, string? solutionColumn)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "7" };
        var subject = new Subject { Name = "Fen" };
        ctx.AddRange(grade, subject);
        await ctx.SaveChangesAsync();

        var topic = new Topic { Name = "Kuvvet", SubjectId = subject.Id, GradeId = grade.Id };
        var student = new Student { UserId = StudentUserId, StudentNumber = "S1", SchoolName = "S", GradeId = grade.Id };
        ctx.AddRange(topic, student);
        await ctx.SaveChangesAsync();

        var question = new Question
        {
            Text = "Etiketleri yerleştir", Point = 1, DifficultyLevel = 1, SubjectId = subject.Id, TopicId = topic.Id,
            IsExample = isExample, IsCanvasQuestion = true,
            InteractionType = "dragDropLabeling", InteractionPlan = plan, InteractionSolution = solutionColumn,
        };
        ctx.Questions.Add(question);
        await ctx.SaveChangesAsync();

        // Havuz kuralı: sınıfı eşleşen Normal bir worksheet, CorrectAnswerId dolu olmalı (PracticeQuestionPool).
        var answer = new Answer { QuestionId = question.Id, Text = "ok", Tag = "A", Order = 0 };
        var worksheet = new Worksheet { Name = "WS", Description = "", GradeId = grade.Id, SubjectId = subject.Id };
        ctx.AddRange(answer, worksheet);
        await ctx.SaveChangesAsync();
        question.CorrectAnswerId = answer.Id;
        ctx.TestQuestions.Add(new WorksheetQuestion { TestId = worksheet.Id, QuestionId = question.Id, Order = 1 });
        await ctx.SaveChangesAsync();

        return (new World(student.Id, grade.Id), question.Id);
    }

    private async Task<int> StartSessionAsync(World w)
    {
        await using var ctx = _db.NewContext();
        var dto = await new PracticeSessionService(ctx).StartAsync(
            new StudentProfileDto { Id = w.StudentId, GradeId = w.GradeId }, new PracticeSessionStartDto());
        return dto.Id;
    }

    private async Task<QuestionDto> NextQuestionAsync(int sessionId, World w)
    {
        await using var ctx = _db.NewContext();
        var next = await new PracticeSessionService(ctx).NextQuestionAsync(sessionId, w.StudentId);
        next.ShouldNotBeNull();
        next!.Question.ShouldNotBeNull();
        return next.Question!;
    }

    [Fact]
    public async Task NextQuestion_DragDropNormalQuestion_PlanHasNoSolutionAndNoSolutionIsReturned()
    {
        var (w, _) = await SeedAsync(isExample: false, PlanWithoutSolution, Solution);
        var sessionId = await StartSessionAsync(w);

        var question = await NextQuestionAsync(sessionId, w);

        ShouldHaveNoSolutionKey(question.InteractionPlan);
        question.InteractionSolution.ShouldBeNull();
    }

    [Fact]
    public async Task NextQuestion_LegacyEmbeddedSolution_NeverLeaksEmbeddedSolution()
    {
        var (w, _) = await SeedAsync(isExample: false, PlanWithEmbeddedSolution, solutionColumn: null);
        var sessionId = await StartSessionAsync(w);

        var question = await NextQuestionAsync(sessionId, w);

        ShouldHaveNoSolutionKey(question.InteractionPlan);
        question.InteractionSolution.ShouldBeNull();
    }

    [Fact]
    public async Task NextQuestion_ExampleQuestion_ReturnsSolution()
    {
        var (w, _) = await SeedAsync(isExample: true, PlanWithoutSolution, Solution);
        var sessionId = await StartSessionAsync(w);

        var question = await NextQuestionAsync(sessionId, w);

        ShouldHaveNoSolutionKey(question.InteractionPlan);
        question.InteractionSolution.ShouldBe(Solution);
    }

    [Fact]
    public async Task GetReview_PendingDragDropQuestion_HidesSolution()
    {
        var (w, questionId) = await SeedAsync(isExample: false, PlanWithoutSolution, Solution);
        var sessionId = await StartSessionAsync(w);
        (await NextQuestionAsync(sessionId, w)).Id.ShouldBe(questionId);

        await using var ctx = _db.NewContext();
        var review = await new PracticeSessionService(ctx).GetReviewAsync(sessionId, w.StudentId);

        review.ShouldNotBeNull();
        var item = review!.Questions.ShouldHaveSingleItem();
        item.Status.ShouldBe("Pending");
        ShouldHaveNoSolutionKey(item.Question!.InteractionPlan);
        item.Question.InteractionSolution.ShouldBeNull();
    }

    [Fact]
    public async Task GetReview_SkippedDragDropQuestion_RevealsSolutionAfterAnswer()
    {
        var (w, questionId) = await SeedAsync(isExample: false, PlanWithoutSolution, Solution);
        var sessionId = await StartSessionAsync(w);
        await NextQuestionAsync(sessionId, w);

        await using (var submitCtx = _db.NewContext())
            (await new PracticeSessionService(submitCtx).SubmitAnswerAsync(sessionId, w.StudentId,
                new PracticeAnswerSubmitDto { QuestionId = questionId, SelectedAnswerId = null, Skipped = true }))
                .ShouldNotBeNull();

        await using var ctx = _db.NewContext();
        var review = await new PracticeSessionService(ctx).GetReviewAsync(sessionId, w.StudentId);

        var item = review!.Questions.ShouldHaveSingleItem();
        item.Status.ShouldBe("Skipped");
        ShouldHaveNoSolutionKey(item.Question!.InteractionPlan);
        item.Question.InteractionSolution.ShouldBe(Solution);
    }
}
