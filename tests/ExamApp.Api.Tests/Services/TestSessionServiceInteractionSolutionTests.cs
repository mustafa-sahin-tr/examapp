using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #570: öğrenci test sonucu (<see cref="TestSessionService.GetCanvasTestResultAsync"/>) — drag-drop planı her
/// zaman çözümsüz; etkileşim çözümü yalnız örnek soruda ya da bitmiş (Completed/Expired) testte, doğru cevaplarla aynı kapıdan.
/// </summary>
public class TestSessionServiceInteractionSolutionTests : IDisposable
{
    private const int StudentUserId = 300;
    private const string Solution = """{"placements":[{"zone":"z1","label":"l1"}]}""";
    private static readonly string PlanWithoutSolution = """{"labels":["l1","l2"],"zones":[{"id":"z1"}]}""";
    private static readonly string PlanWithEmbeddedSolution =
        """{"labels":["l1","l2"],"zones":[{"id":"z1"}],"solution":{"placements":[{"zone":"z1","label":"l1"}]}}""";

    private readonly TestDb _db = TestDb.Create();

    private TestSessionService NewService(AppDbContext ctx) => new(ctx);

    public void Dispose() => _db.Dispose();

    private static void ShouldHaveNoSolutionKey(string? plan)
    {
        plan.ShouldNotBeNull();
        using var doc = JsonDocument.Parse(plan!);
        doc.RootElement.TryGetProperty("solution", out _).ShouldBeFalse();
    }

    /// <summary>Bir dragDrop sorusu içeren test oturumu; oturum durumu ve soru ayarları parametreyle.</summary>
    private async Task<int> SeedInstanceAsync(WorksheetInstanceStatus status, bool isExample, string plan, string? solutionColumn)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();

        var student = new Student { UserId = StudentUserId, StudentNumber = "S", SchoolName = "Sch", GradeId = grade.Id };
        var worksheet = new Worksheet { Name = "Test", Description = "", GradeId = grade.Id, MaxDurationSeconds = 600 };
        var question = new Question
        {
            Text = "Etiketleri yerleştir", Point = 10, IsExample = isExample,
            InteractionType = "dragDropLabeling", InteractionPlan = plan, InteractionSolution = solutionColumn,
        };
        ctx.AddRange(student, worksheet, question);
        await ctx.SaveChangesAsync();

        var wq = new WorksheetQuestion { TestId = worksheet.Id, QuestionId = question.Id, Order = 1 };
        ctx.TestQuestions.Add(wq);
        await ctx.SaveChangesAsync();

        var instance = new WorksheetInstance
        {
            StudentId = student.Id, WorksheetId = worksheet.Id, Status = status,
            StartTime = DateTime.UtcNow.AddMinutes(-5),
            EndTime = WorksheetInstanceStatusRules.IsFinished(status) ? DateTime.UtcNow.AddMinutes(-1) : null,
        };
        ctx.TestInstances.Add(instance);
        await ctx.SaveChangesAsync();

        ctx.TestInstanceQuestions.Add(new WorksheetInstanceQuestion { WorksheetInstanceId = instance.Id, WorksheetQuestionId = wq.Id });
        await ctx.SaveChangesAsync();
        return instance.Id;
    }

    private async Task<QuestionDto> ResultQuestionAsync(int instanceId, bool includeCorrectAnswer)
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).GetCanvasTestResultAsync(instanceId, StudentUserId, includeCorrectAnswer);
        result.ShouldNotBeNull();
        return result!.TestInstanceQuestions.ShouldHaveSingleItem().Question;
    }

    [Fact]
    public async Task GetCanvasTestResult_StartedTestNormalQuestion_PlanHasNoSolutionAndNoSolutionIsReturned()
    {
        var id = await SeedInstanceAsync(WorksheetInstanceStatus.Started, isExample: false, PlanWithoutSolution, Solution);

        var question = await ResultQuestionAsync(id, includeCorrectAnswer: false);

        ShouldHaveNoSolutionKey(question.InteractionPlan);
        question.InteractionSolution.ShouldBeNull();
    }

    [Fact]
    public async Task GetCanvasTestResult_StartedTestLegacyEmbeddedSolution_NeverLeaksEmbeddedSolution()
    {
        var id = await SeedInstanceAsync(WorksheetInstanceStatus.Started, isExample: false, PlanWithEmbeddedSolution, solutionColumn: null);

        var question = await ResultQuestionAsync(id, includeCorrectAnswer: false);

        ShouldHaveNoSolutionKey(question.InteractionPlan);
        question.InteractionSolution.ShouldBeNull();
    }

    [Fact]
    public async Task GetCanvasTestResult_ExampleQuestionInStartedTest_ReturnsSolution()
    {
        var id = await SeedInstanceAsync(WorksheetInstanceStatus.Started, isExample: true, PlanWithoutSolution, Solution);

        var question = await ResultQuestionAsync(id, includeCorrectAnswer: false);

        ShouldHaveNoSolutionKey(question.InteractionPlan);
        question.InteractionSolution.ShouldBe(Solution);
    }

    [Theory]
    [InlineData(WorksheetInstanceStatus.Completed)]
    [InlineData(WorksheetInstanceStatus.Expired)]
    public async Task GetCanvasTestResult_FinishedTestWithCorrectAnswers_ReturnsSolution(WorksheetInstanceStatus status)
    {
        var id = await SeedInstanceAsync(status, isExample: false, PlanWithoutSolution, Solution);

        var question = await ResultQuestionAsync(id, includeCorrectAnswer: true);

        ShouldHaveNoSolutionKey(question.InteractionPlan);
        question.InteractionSolution.ShouldBe(Solution);
    }

    [Fact]
    public async Task GetCanvasTestResult_FinishedTestWithoutCorrectAnswers_HidesSolution()
    {
        var id = await SeedInstanceAsync(WorksheetInstanceStatus.Completed, isExample: false, PlanWithoutSolution, Solution);

        var question = await ResultQuestionAsync(id, includeCorrectAnswer: false);

        ShouldHaveNoSolutionKey(question.InteractionPlan);
        question.InteractionSolution.ShouldBeNull();
    }

    [Fact]
    public async Task GetCanvasTestResult_FinishedTestLegacyEmbeddedSolution_SplitsSolutionOnlyWhenRevealed()
    {
        var id = await SeedInstanceAsync(WorksheetInstanceStatus.Completed, isExample: false, PlanWithEmbeddedSolution, solutionColumn: null);

        var revealed = await ResultQuestionAsync(id, includeCorrectAnswer: true);
        ShouldHaveNoSolutionKey(revealed.InteractionPlan);
        revealed.InteractionSolution.ShouldNotBeNull();
        revealed.InteractionSolution!.ShouldContain("placements");

        var hidden = await ResultQuestionAsync(id, includeCorrectAnswer: false);
        ShouldHaveNoSolutionKey(hidden.InteractionPlan);
        hidden.InteractionSolution.ShouldBeNull();
    }
}
