using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using Microsoft.EntityFrameworkCore;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #573: sayfa türü okuma tarafı — öğretmen ve öğrenci listeleri <c>WorksheetDto.Kind</c> taşır, <c>kind</c> filtresi
/// (null = hepsi) çalışır; etkinliğin öğrenci kartındaki oturum özeti puansızdır (skor/doğru/yanlış 0, cevaplanan madde ve
/// kontrol sayısı dolu), testte ise eski hesap aynen kalır ve yeni alanlar null.
/// </summary>
public class ExamServiceActivityKindTests : IDisposable
{
    private const int TeacherUserId = 1;
    private const int StudentUserId = 4601;

    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private ExamService NewService(AppDbContext ctx) =>
        new(ctx, new ImageHelper(), Substitute.For<IMinIoService>(), Substitute.For<IAuthApiClient>());

    private static ExamFilterDto Filter(WorksheetKind? kind = null) => new()
    {
        id = 0, pageNumber = 1, pageSize = 50, kind = kind,
    };

    private sealed record World(int StudentId, int GradeId, int TestWorksheetId, int ActivityWorksheetId, int CorrectAnswerId, int TestInstanceId, int ActivityInstanceId);

    /// <summary>
    /// Öğrenci + iki sayfa (Test, Activity), ikisinde de 3 satırlı oturum: MCQ (test: doğru şık; etkinlik: cevaplı), drag
    /// (cevaplı), boş. Etkinlik oturumunun 1 kontrolü var.
    /// </summary>
    private async Task<World> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        ctx.SetCurrentUser(TeacherUserId);
        var grade = new Grade { Name = "5" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();

        var student = new Student { UserId = StudentUserId, StudentNumber = "S4601", SchoolName = "Sch", GradeId = grade.Id };
        var test = new Worksheet { Name = "Test", Description = "", GradeId = grade.Id, MaxDurationSeconds = 600, Kind = WorksheetKind.Test };
        var activity = new Worksheet { Name = "Etkinlik", Description = "", GradeId = grade.Id, MaxDurationSeconds = 0, Kind = WorksheetKind.Activity };
        ctx.AddRange(student, test, activity);
        await ctx.SaveChangesAsync();

        var mcq = new Question { Text = "2+2", Point = 10 };
        var drag = new Question { Text = "Eşleştir", Point = 10, InteractionType = "dragDropLabeling" };
        var blank = new Question { Text = "Boş", Point = 10 };
        ctx.Questions.AddRange(mcq, drag, blank);
        await ctx.SaveChangesAsync();

        var right = new Answer { QuestionId = mcq.Id, Text = "4", Tag = "A" };
        ctx.Answers.Add(right);
        await ctx.SaveChangesAsync();
        mcq.CorrectAnswerId = right.Id;
        await ctx.SaveChangesAsync();

        var testWqs = new[] { mcq, drag, blank }.Select((q, i) => new WorksheetQuestion { TestId = test.Id, QuestionId = q.Id, Order = i + 1 }).ToList();
        var activityWqs = new[] { mcq, drag, blank }.Select((q, i) => new WorksheetQuestion { TestId = activity.Id, QuestionId = q.Id, Order = i + 1 }).ToList();
        ctx.TestQuestions.AddRange(testWqs);
        ctx.TestQuestions.AddRange(activityWqs);
        await ctx.SaveChangesAsync();

        var testInstance = new WorksheetInstance
        {
            StudentId = student.Id, WorksheetId = test.Id, Status = WorksheetInstanceStatus.Completed,
            StartTime = new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc), EndTime = new DateTime(2020, 1, 2, 0, 5, 0, DateTimeKind.Utc),
            MaxDurationSeconds = 600,
        };
        var activityInstance = new WorksheetInstance
        {
            StudentId = student.Id, WorksheetId = activity.Id, Status = WorksheetInstanceStatus.Started,
            StartTime = new DateTime(2020, 1, 2, 0, 0, 0, DateTimeKind.Utc), MaxDurationSeconds = 0,
            CheckCount = 1, FirstCheckedAt = new DateTime(2020, 1, 2, 0, 1, 0, DateTimeKind.Utc),
        };
        ctx.TestInstances.AddRange(testInstance, activityInstance);
        await ctx.SaveChangesAsync();

        ctx.TestInstanceQuestions.AddRange(
            new WorksheetInstanceQuestion { WorksheetInstanceId = testInstance.Id, WorksheetQuestionId = testWqs[0].Id, SelectedAnswerId = right.Id },
            new WorksheetInstanceQuestion { WorksheetInstanceId = testInstance.Id, WorksheetQuestionId = testWqs[1].Id, AnswerPayload = "{}" },
            new WorksheetInstanceQuestion { WorksheetInstanceId = testInstance.Id, WorksheetQuestionId = testWqs[2].Id },
            new WorksheetInstanceQuestion { WorksheetInstanceId = activityInstance.Id, WorksheetQuestionId = activityWqs[0].Id, SelectedAnswerId = right.Id },
            new WorksheetInstanceQuestion { WorksheetInstanceId = activityInstance.Id, WorksheetQuestionId = activityWqs[1].Id, AnswerPayload = "{}" },
            new WorksheetInstanceQuestion { WorksheetInstanceId = activityInstance.Id, WorksheetQuestionId = activityWqs[2].Id });
        await ctx.SaveChangesAsync();

        return new World(student.Id, grade.Id, test.Id, activity.Id, right.Id, testInstance.Id, activityInstance.Id);
    }

    private async Task<Paged<WorksheetDto>> StudentListAsync(World w, WorksheetKind? kind = null)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).GetWorksheetsForStudentsAsync(Filter(kind), new StudentProfileDto { Id = w.StudentId, GradeId = w.GradeId });
    }

    private async Task<Paged<WorksheetDto>> TeacherListAsync(WorksheetKind? kind = null)
    {
        await using var ctx = _db.NewContext();
        return await NewService(ctx).GetWorksheetsForTeacherAsync(Filter(kind), new UserProfileDto { Id = TeacherUserId, Role = "Teacher" }, isAdmin: false);
    }

    // ---------------------------------------------------------------- kind: liste ve filtre

    [Fact]
    public async Task GetWorksheetsForTeacherAsync_KindOmitted_ReturnsBothWithTheirKind()
    {
        await SeedAsync();

        var page = await TeacherListAsync();

        page.Items.ToDictionary(i => i.Name, i => i.Kind)
            // Liste CreateTime'a göre sıralı; iki kayıt aynı anda yazılabildiği için sıra kararsız — sırasız karşılaştır.
            .ShouldBe(new Dictionary<string, WorksheetKind> { ["Test"] = WorksheetKind.Test, ["Etkinlik"] = WorksheetKind.Activity },
                ignoreOrder: true);
    }

    [Theory]
    [InlineData(WorksheetKind.Test, "Test")]
    [InlineData(WorksheetKind.Activity, "Etkinlik")]
    public async Task GetWorksheetsForTeacherAsync_KindFilter_ReturnsOnlyThatKind(WorksheetKind kind, string expectedName)
    {
        await SeedAsync();

        var page = await TeacherListAsync(kind);

        var only = page.Items.ShouldHaveSingleItem();
        only.Name.ShouldBe(expectedName);
        only.Kind.ShouldBe(kind);
        page.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetWorksheetsForStudentsAsync_KindFilter_ReturnsOnlyActivities()
    {
        var w = await SeedAsync();

        var page = await StudentListAsync(w, WorksheetKind.Activity);

        page.Items.ShouldHaveSingleItem().Id.ShouldBe(w.ActivityWorksheetId);
    }

    [Fact]
    public async Task GetWorksheetsForStudentsAsync_KindOmitted_ReturnsBoth()
    {
        var w = await SeedAsync();

        var page = await StudentListAsync(w);

        page.TotalCount.ShouldBe(2);
    }

    // ---------------------------------------------------------------- oturum özeti: etkinlik puansız

    [Fact]
    public async Task GetWorksheetsForStudentsAsync_ActivityInstanceSummary_IsPointlessButReportsProgressAndChecks()
    {
        var w = await SeedAsync();

        var item = (await StudentListAsync(w, WorksheetKind.Activity)).Items.ShouldHaveSingleItem();

        var summary = item.Instance.ShouldNotBeNull();
        summary.Score.ShouldBe(0);
        summary.CorrectAnswers.ShouldBe(0);
        summary.WrongAnswers.ShouldBe(0);
        summary.TotalQuestions.ShouldBe(3);
        summary.AnsweredQuestions.ShouldBe(2); // MCQ + drag; boş madde sayılmaz
        summary.CheckCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetWorksheetsForStudentsAsync_TestInstanceSummary_KeepsScoringAndLeavesNewFieldsNull()
    {
        var w = await SeedAsync();

        var item = (await StudentListAsync(w, WorksheetKind.Test)).Items.ShouldHaveSingleItem();

        var summary = item.Instance.ShouldNotBeNull();
        summary.CorrectAnswers.ShouldBe(1); // MCQ doğru şık
        summary.Score.ShouldBeGreaterThan(0);
        summary.AnsweredQuestions.ShouldBeNull();
        summary.CheckCount.ShouldBeNull();
    }

    [Fact]
    public async Task GetWorksheetsForStudentsAsync_ActivityWithoutAnyCheck_ReportsZeroCheckCount()
    {
        var w = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            await ctx.TestInstances.Where(ti => ti.Id == w.ActivityInstanceId)
                .ExecuteUpdateAsync(u => u.SetProperty(ti => ti.CheckCount, 0).SetProperty(ti => ti.FirstCheckedAt, (DateTime?)null));
        }

        var item = (await StudentListAsync(w, WorksheetKind.Activity)).Items.ShouldHaveSingleItem();

        item.Instance.ShouldNotBeNull().CheckCount.ShouldBe(0);
    }

    // ---------------------------------------------------------------- issue #609: FirstCompletedAt (yalnız etkinlik)

    [Fact]
    public async Task GetWorksheetsForStudentsAsync_RetakingActivity_CarriesFirstCompletedAt_TestKindStaysNull()
    {
        var w = await SeedAsync();
        var firstCompletedAt = new DateTime(2020, 1, 2, 0, 3, 0, DateTimeKind.Utc);
        await using (var ctx = _db.NewContext())
        {
            // Etkinlik yeniden çözülüyor (Started + FirstCompletedAt); test oturumunun kolonu da dolu (migration backfill'i gibi).
            await ctx.TestInstances.Where(ti => ti.Id == w.ActivityInstanceId || ti.Id == w.TestInstanceId)
                .ExecuteUpdateAsync(u => u.SetProperty(ti => ti.FirstCompletedAt, (DateTime?)firstCompletedAt));
        }

        var page = await StudentListAsync(w);

        var activity = page.Items.Single(i => i.Id == w.ActivityWorksheetId).Instance.ShouldNotBeNull();
        activity.Status.ShouldBe((int)WorksheetInstanceStatus.Started);
        activity.FirstCompletedAt.ShouldBe(firstCompletedAt);
        page.Items.Single(i => i.Id == w.TestWorksheetId).Instance.ShouldNotBeNull().FirstCompletedAt.ShouldBeNull();
    }

    [Fact]
    public async Task GetWorksheetsForStudentsAsync_NeverCompletedActivity_HasNullFirstCompletedAt()
    {
        var w = await SeedAsync();

        var page = await StudentListAsync(w);

        page.Items.Single(i => i.Id == w.ActivityWorksheetId).Instance.ShouldNotBeNull().FirstCompletedAt.ShouldBeNull();
    }
}
