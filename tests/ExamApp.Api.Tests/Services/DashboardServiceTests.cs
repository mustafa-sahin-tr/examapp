using ExamApp.Api.Data;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #85: admin dashboard özet sayaçları (GET /api/admin/dashboard/summary).
/// </summary>
public class DashboardServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    private DashboardService NewService(AppDbContext ctx) => new(ctx);

    [Fact]
    public async Task GetSummaryAsync_EmptyDatabase_AllCountsAreZeroAndRatioIsZero()
    {
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).GetSummaryAsync();

        result.TeacherCount.ShouldBe(0);
        result.StudentCount.ShouldBe(0);
        result.WorksheetCount.ShouldBe(0);
        result.QuestionCount.ShouldBe(0);
        result.AiClassifiedQuestionCount.ShouldBe(0);
        result.AiClassifiedRatio.ShouldBe(0);
    }

    [Fact]
    public async Task GetSummaryAsync_MixedClassificationSources_CountsOnlyAiAndComputesRatio()
    {
        await using (var ctx = _db.NewContext())
        {
            // 2 AI, 1 Human, 1 null (unclassified) => 4 total, AI ratio = 2/4 = 0.5
            ctx.Questions.Add(new Question { Text = "q1", Point = 1, ClassificationSource = ClassificationSource.AI });
            ctx.Questions.Add(new Question { Text = "q2", Point = 1, ClassificationSource = ClassificationSource.AI });
            ctx.Questions.Add(new Question { Text = "q3", Point = 1, ClassificationSource = ClassificationSource.Human });
            ctx.Questions.Add(new Question { Text = "q4", Point = 1, ClassificationSource = null });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetSummaryAsync();

        result.QuestionCount.ShouldBe(4);
        result.AiClassifiedQuestionCount.ShouldBe(2);
        result.AiClassifiedRatio.ShouldBe(0.5);
    }

    [Fact]
    public async Task GetSummaryAsync_NoAiClassifiedQuestions_RatioIsZeroNotNaN()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.Questions.Add(new Question { Text = "q1", Point = 1, ClassificationSource = ClassificationSource.Human });
            ctx.Questions.Add(new Question { Text = "q2", Point = 1, ClassificationSource = null });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetSummaryAsync();

        result.QuestionCount.ShouldBe(2);
        result.AiClassifiedQuestionCount.ShouldBe(0);
        result.AiClassifiedRatio.ShouldBe(0);
        double.IsNaN(result.AiClassifiedRatio).ShouldBeFalse();
    }

    [Fact]
    public async Task GetSummaryAsync_SeedsTeachersStudentsWorksheetsAndQuestions_ReturnsExactCounts()
    {
        await using (var ctx = _db.NewContext())
        {
            var grade = new Grade { Name = "8" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();

            ctx.Teachers.Add(new Teacher { UserId = 1 });
            ctx.Teachers.Add(new Teacher { UserId = 2 });
            ctx.Teachers.Add(new Teacher { UserId = 3 });

            ctx.Students.Add(new Student { UserId = 10, StudentNumber = "a", SchoolName = "s" });
            ctx.Students.Add(new Student { UserId = 11, StudentNumber = "b", SchoolName = "s" });

            ctx.Worksheets.Add(new Worksheet { Name = "WS1", Description = "", GradeId = grade.Id });

            ctx.Questions.Add(new Question { Text = "q1", Point = 1, ClassificationSource = ClassificationSource.AI });
            ctx.Questions.Add(new Question { Text = "q2", Point = 1, ClassificationSource = ClassificationSource.Human });
            ctx.Questions.Add(new Question { Text = "q3", Point = 1, ClassificationSource = ClassificationSource.AI });

            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetSummaryAsync();

        result.TeacherCount.ShouldBe(3);
        result.StudentCount.ShouldBe(2);
        result.WorksheetCount.ShouldBe(1);
        result.QuestionCount.ShouldBe(3);
        result.AiClassifiedQuestionCount.ShouldBe(2);
        result.AiClassifiedRatio.ShouldBe(2.0 / 3.0, 0.0001);
    }

    [Fact]
    public async Task GetSummaryAsync_IsNotScopedByCurrentUser_CountsAllTeachersAcrossOwners()
    {
        // Dashboard is an admin-wide aggregate, unlike per-teacher services — it must not
        // apply any SetCurrentUser-based row filtering.
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(1);
            ctx.Teachers.Add(new Teacher { UserId = 1 });
            await ctx.SaveChangesAsync();

            ctx.SetCurrentUser(2);
            ctx.Teachers.Add(new Teacher { UserId = 2 });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetSummaryAsync();

        result.TeacherCount.ShouldBe(2);
    }

    public void Dispose() => _db.Dispose();
}
