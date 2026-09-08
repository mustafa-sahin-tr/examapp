using System;
using System.Linq;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #87: admin dashboard trend serileri (GET /api/admin/dashboard/trends).
/// </summary>
public class DashboardServiceTrendsTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    private DashboardService NewService(AppDbContext ctx) => new(ctx);

    /// <summary>Bugünün UTC tarihi, testler boyunca sabit referans olarak kullanılır.</summary>
    private static DateTime Today => DateTime.UtcNow.Date;

    // ---- Helpers ----

    private async Task<int> AddGradeAsync(AppDbContext ctx, string name = "G")
    {
        var g = new Grade { Name = name };
        ctx.Grades.Add(g);
        await ctx.SaveChangesAsync();
        return g.Id;
    }

    private async Task<int> AddQuestionAsync(AppDbContext ctx)
    {
        var q = new Question { Text = "q", Point = 1 };
        ctx.Questions.Add(q);
        await ctx.SaveChangesAsync();
        return q.Id;
    }

    /// <summary>
    /// Question.CreateTime, BaseEntity denetim (audit) interceptor'ı yüzünden Add sırasında her
    /// zaman DateTime.UtcNow ile ezilir; istenen güne taşımak için ekten sonra ayrı bir
    /// ExecuteUpdate ile (interceptor'a uğramadan) günceller.
    /// </summary>
    private static async Task SetQuestionCreateTimeAsync(AppDbContext ctx, int questionId, DateTime day)
    {
        await ctx.Questions.Where(q => q.Id == questionId)
            .ExecuteUpdateAsync(s => s.SetProperty(q => q.CreateTime, day));
    }

    private async Task<(int studentId, int gradeId)> AddStudentAsync(AppDbContext ctx, int userId)
    {
        var gradeId = await AddGradeAsync(ctx, $"G{userId}");
        var student = new Student { UserId = userId, StudentNumber = $"S{userId}", SchoolName = "School" };
        ctx.Students.Add(student);
        await ctx.SaveChangesAsync();
        return (student.Id, gradeId);
    }

    /// <summary>
    /// Cevaplanmış (veya isteğe bağlı cevaplanmamış) bir PracticeSessionQuestion satırı ekler.
    /// AnsweredAt, BaseEntity'nin denetim alanı DEĞİL; doğrudan set edilen değer aynen kalır.
    /// </summary>
    private async Task AddPracticeSolvedAsync(AppDbContext ctx, DateTime? answeredAt)
    {
        var (studentId, gradeId) = await AddStudentAsync(ctx, Random.Shared.Next(1, 1_000_000));
        var session = new PracticeSession { StudentId = studentId, GradeId = gradeId, StartTime = Today };
        ctx.PracticeSessions.Add(session);
        var questionId = await AddQuestionAsync(ctx);
        await ctx.SaveChangesAsync();

        ctx.PracticeSessionQuestions.Add(new PracticeSessionQuestion
        {
            PracticeSessionId = session.Id,
            QuestionId = questionId,
            ShownAt = Today,
            AnsweredAt = answeredAt,
        });
        await ctx.SaveChangesAsync();
    }

    /// <summary>
    /// Cevaplanmış (veya isteğe bağlı cevaplanmamış) bir WorksheetInstanceQuestion satırı ekler.
    /// UpdateTime, BaseEntity'nin denetim alanı olsa da yalnızca EntityState.Modified geçişinde
    /// ezilir; Added durumunda (ilk ekleme) dokunulmaz, bu yüzden burada verilen değer kalıcıdır.
    /// </summary>
    private async Task AddWorksheetSolvedAsync(AppDbContext ctx, DateTime? updateTime, int? selectedAnswerId = -1, string? answerPayload = null)
    {
        var (studentId, gradeId) = await AddStudentAsync(ctx, Random.Shared.Next(1, 1_000_000));
        var worksheet = new Worksheet { Name = "WS", Description = "", GradeId = gradeId };
        ctx.Worksheets.Add(worksheet);
        var questionId = await AddQuestionAsync(ctx);
        await ctx.SaveChangesAsync();

        var answerId = selectedAnswerId;
        if (answerId == -1)
        {
            var answer = new Answer { QuestionId = questionId, Text = "A", Tag = "A" };
            ctx.Answers.Add(answer);
            await ctx.SaveChangesAsync();
            answerId = answer.Id;
        }

        var wq = new WorksheetQuestion { TestId = worksheet.Id, QuestionId = questionId, Order = 1 };
        ctx.TestQuestions.Add(wq);
        var instance = new WorksheetInstance
        {
            StudentId = studentId,
            WorksheetId = worksheet.Id,
            StartTime = Today,
            Status = WorksheetInstanceStatus.Started,
        };
        ctx.Add(instance);
        await ctx.SaveChangesAsync();

        ctx.TestInstanceQuestions.Add(new WorksheetInstanceQuestion
        {
            WorksheetInstanceId = instance.Id,
            WorksheetQuestionId = wq.Id,
            SelectedAnswerId = answerId,
            AnswerPayload = answerPayload,
            UpdateTime = updateTime,
        });
        await ctx.SaveChangesAsync();
    }

    // ---- days param → fixed-length series with zero-fill ----

    [Fact]
    public async Task GetTrendsAsync_EmptyDatabase_ReturnsExactlyDaysEntriesAllZero()
    {
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).GetTrendsAsync(30);

        result.QuestionCreated.Count.ShouldBe(30);
        result.QuestionSolved.Count.ShouldBe(30);
        result.QuestionCreated.ShouldAllBe(p => p.Count == 0);
        result.QuestionSolved.ShouldAllBe(p => p.Count == 0);
    }

    [Fact]
    public async Task GetTrendsAsync_SparseData_MissingDaysAreZeroFilled()
    {
        await using (var ctx = _db.NewContext())
        {
            var q1 = await AddQuestionAsync(ctx);
            await SetQuestionCreateTimeAsync(ctx, q1, Today);

            var q2 = await AddQuestionAsync(ctx);
            await SetQuestionCreateTimeAsync(ctx, q2, Today.AddDays(-5));
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetTrendsAsync(7);

        result.QuestionCreated.Count.ShouldBe(7);
        result.QuestionCreated.Last().Date.ShouldBe(DateOnly.FromDateTime(Today));
        result.QuestionCreated.Last().Count.ShouldBe(1);
        result.QuestionCreated.First().Date.ShouldBe(DateOnly.FromDateTime(Today.AddDays(-6)));

        var dayMinus5 = result.QuestionCreated.Single(p => p.Date == DateOnly.FromDateTime(Today.AddDays(-5)));
        dayMinus5.Count.ShouldBe(1);

        // Between the two seeded days, everything else must be zero.
        result.QuestionCreated.Where(p => p.Date != DateOnly.FromDateTime(Today) && p.Date != DateOnly.FromDateTime(Today.AddDays(-5)))
            .ShouldAllBe(p => p.Count == 0);
    }

    [Fact]
    public async Task GetTrendsAsync_SeriesIsOrderedAscendingByDateStartingFromCutoff()
    {
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).GetTrendsAsync(5);

        var expectedDates = Enumerable.Range(0, 5)
            .Select(i => DateOnly.FromDateTime(Today.AddDays(-4 + i)))
            .ToList();

        result.QuestionCreated.Select(p => p.Date).ShouldBe(expectedDates);
    }

    // ---- merging two sources into a single QuestionSolved series ----

    [Fact]
    public async Task GetTrendsAsync_PracticeAndWorksheetSolvedOnSameDay_SumsIntoOneSeries()
    {
        await using (var ctx = _db.NewContext())
        {
            await AddPracticeSolvedAsync(ctx, Today);
            await AddWorksheetSolvedAsync(ctx, Today);
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetTrendsAsync(30);

        result.QuestionSolved.Last().Count.ShouldBe(2);
    }

    // ---- exclusion rules ----

    [Fact]
    public async Task GetTrendsAsync_UnansweredPracticeQuestion_IsNotCounted()
    {
        await using (var ctx = _db.NewContext())
        {
            await AddPracticeSolvedAsync(ctx, answeredAt: null);
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetTrendsAsync(30);

        result.QuestionSolved.ShouldAllBe(p => p.Count == 0);
    }

    [Fact]
    public async Task GetTrendsAsync_UnansweredWorksheetQuestion_WithNoSelectedAnswerAndNoPayload_IsNotCounted()
    {
        await using (var ctx = _db.NewContext())
        {
            // Row opened when the test started (CreateTime = today) but never answered:
            // both SelectedAnswerId and AnswerPayload are null, UpdateTime is null too.
            await AddWorksheetSolvedAsync(ctx, updateTime: null, selectedAnswerId: null, answerPayload: null);
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetTrendsAsync(30);

        result.QuestionSolved.ShouldAllBe(p => p.Count == 0);
    }

    [Fact]
    public async Task GetTrendsAsync_WorksheetQuestionAnsweredViaPayloadOnly_IsCounted()
    {
        // Non-MCQ (e.g. drag-drop) answers are stored in AnswerPayload with no SelectedAnswerId.
        await using (var ctx = _db.NewContext())
        {
            await AddWorksheetSolvedAsync(ctx, updateTime: Today, selectedAnswerId: null, answerPayload: "{\"order\":[1,2]}");
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetTrendsAsync(30);

        result.QuestionSolved.Last().Count.ShouldBe(1);
    }

    [Fact]
    public async Task GetTrendsAsync_WorksheetQuestionAnsweredButUpdateTimeNull_IsNotCounted()
    {
        // Edge case guarded explicitly by the service's `w.UpdateTime != null` check.
        await using (var ctx = _db.NewContext())
        {
            var (studentId, gradeId) = await AddStudentAsync(ctx, 999001);
            var worksheet = new Worksheet { Name = "WS", Description = "", GradeId = gradeId };
            ctx.Worksheets.Add(worksheet);
            var questionId = await AddQuestionAsync(ctx);
            await ctx.SaveChangesAsync();

            var answer = new Answer { QuestionId = questionId, Text = "A", Tag = "A" };
            ctx.Answers.Add(answer);
            var wq = new WorksheetQuestion { TestId = worksheet.Id, QuestionId = questionId, Order = 1 };
            ctx.TestQuestions.Add(wq);
            var instance = new WorksheetInstance { StudentId = studentId, WorksheetId = worksheet.Id, StartTime = Today, Status = WorksheetInstanceStatus.Started };
            ctx.Add(instance);
            await ctx.SaveChangesAsync();

            ctx.TestInstanceQuestions.Add(new WorksheetInstanceQuestion
            {
                WorksheetInstanceId = instance.Id,
                WorksheetQuestionId = wq.Id,
                SelectedAnswerId = answer.Id,
                UpdateTime = null, // answered flag present, but UpdateTime somehow missing
            });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetTrendsAsync(30);

        result.QuestionSolved.ShouldAllBe(p => p.Count == 0);
    }

    [Fact]
    public async Task GetTrendsAsync_QuestionCreatedOutsideDateWindow_IsNotCounted()
    {
        await using (var ctx = _db.NewContext())
        {
            var qOld = await AddQuestionAsync(ctx);
            await SetQuestionCreateTimeAsync(ctx, qOld, Today.AddDays(-40)); // outside a 30-day window
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetTrendsAsync(30);

        result.QuestionCreated.ShouldAllBe(p => p.Count == 0);
    }

    [Fact]
    public async Task GetTrendsAsync_PracticeAndWorksheetSolvedOutsideDateWindow_AreNotCounted()
    {
        // Note: the helpers below also insert a supporting Question (CreateTime = today), which
        // is irrelevant to QuestionCreated and is intentionally not asserted on here.
        await using (var ctx = _db.NewContext())
        {
            await AddPracticeSolvedAsync(ctx, Today.AddDays(-40));
            await AddWorksheetSolvedAsync(ctx, Today.AddDays(-40));
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetTrendsAsync(30);

        result.QuestionSolved.ShouldAllBe(p => p.Count == 0);
    }

    [Fact]
    public async Task GetTrendsAsync_QuestionCreatedOnCutoffBoundary_IsIncluded()
    {
        // cutoff = today - (days - 1); the boundary day itself must be included (>=).
        await using (var ctx = _db.NewContext())
        {
            var q = await AddQuestionAsync(ctx);
            await SetQuestionCreateTimeAsync(ctx, q, Today.AddDays(-6)); // exact cutoff for days=7
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetTrendsAsync(7);

        result.QuestionCreated.First().Count.ShouldBe(1);
    }

    public void Dispose() => _db.Dispose();
}
