using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #570 review: eski (yazma katılığından önce kaydedilmiş) bozuk planlı bir drag-drop sorusu öğrencinin test
/// sonucunu düşürmez: yinelenen anahtarlı plan istisna atmadan çözümsüz döner; ayrıştırılamayan ve "solution" içeren plan
/// kapalı başarısızlıkla plan=null döner (çözüm sızmaz).
/// </summary>
public class TestSessionServiceLegacyPlanReadTests : IDisposable
{
    private const int StudentUserId = 310;
    private const string Solution = """{"placements":[{"zone":"z1","label":"l1"}]}""";

    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private async Task<int> SeedInstanceAsync(string plan, string? solutionColumn, WorksheetInstanceStatus status)
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();

        var student = new Student { UserId = StudentUserId, StudentNumber = "S", SchoolName = "Sch", GradeId = grade.Id };
        var worksheet = new Worksheet { Name = "Test", Description = "", GradeId = grade.Id, MaxDurationSeconds = 600 };
        var question = new Question
        {
            Text = "Etiketleri yerleştir", Point = 10,
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
        var result = await new TestSessionService(ctx).GetCanvasTestResultAsync(instanceId, StudentUserId, includeCorrectAnswer);
        result.ShouldNotBeNull();
        return result!.TestInstanceQuestions.ShouldHaveSingleItem().Question;
    }

    [Theory]
    [InlineData(WorksheetInstanceStatus.Started, false)]
    [InlineData(WorksheetInstanceStatus.Completed, true)]
    public async Task GetCanvasTestResult_LegacyDuplicateKeyPlan_ReturnsDtoWithoutSolutionAndDoesNotThrow(
        WorksheetInstanceStatus status, bool includeCorrectAnswer)
    {
        var id = await SeedInstanceAsync("""{"labels":["l1"],"labels":["l2"],"solution":{"placements":[]},"solution":""" + Solution + "}",
            solutionColumn: null, status);

        var question = await ResultQuestionAsync(id, includeCorrectAnswer);

        question.InteractionPlan.ShouldNotBeNull();
        using var doc = System.Text.Json.JsonDocument.Parse(question.InteractionPlan!);
        doc.RootElement.TryGetProperty("solution", out _).ShouldBeFalse();
        question.InteractionSolution.ShouldBe(includeCorrectAnswer ? Solution : null);
    }

    [Fact]
    public async Task GetCanvasTestResult_UnparseablePlanMentioningSolution_ReturnsNullPlanAndDoesNotThrow()
    {
        var id = await SeedInstanceAsync("""{"labels":["l1"],"solution":{"placements":[]""", solutionColumn: null,
            WorksheetInstanceStatus.Started);

        var question = await ResultQuestionAsync(id, includeCorrectAnswer: false);

        question.InteractionPlan.ShouldBeNull();
        question.InteractionSolution.ShouldBeNull();
    }

    [Fact]
    public async Task GetCanvasTestResult_UnparseablePlanMentioningSolutionInFinishedTest_NeverLeaksPlanText()
    {
        var id = await SeedInstanceAsync("""{"SOLUTION":{"placements":[]}""", solutionColumn: Solution,
            WorksheetInstanceStatus.Completed);

        var question = await ResultQuestionAsync(id, includeCorrectAnswer: true);

        question.InteractionPlan.ShouldBeNull();
    }
}
