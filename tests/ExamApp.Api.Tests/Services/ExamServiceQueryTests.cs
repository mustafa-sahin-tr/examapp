using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

public class ExamServiceQueryTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IMinIoService _minio = Substitute.For<IMinIoService>();

    private ExamService NewService(AppDbContext ctx) => new(ctx, new ImageHelper(), _minio);
    private TestSessionService NewSession(AppDbContext ctx) => new(ctx);
    private WorksheetAuthoringService NewAuthoring(AppDbContext ctx) => new(ctx, new ImageHelper(), _minio);

    /// <summary>Worksheet.GradeId is a required FK — every worksheet needs a real grade.</summary>
    private static async Task<int> AddGradeAsync(AppDbContext ctx, string name = "G")
    {
        var g = new Grade { Name = name };
        ctx.Grades.Add(g);
        await ctx.SaveChangesAsync();
        return g.Id;
    }

    // ---- GetGradesAsync ----

    [Fact]
    public async Task GetGrades_returns_every_grade()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.Grades.AddRange(new Grade { Name = "1" }, new Grade { Name = "2" }, new Grade { Name = "3" });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        (await NewService(read).GetGradesAsync()).Select(g => g.Name).ShouldBe(new[] { "1", "2", "3" }, ignoreOrder: true);
    }

    // ---- GetLatestWorksheetsAsync ----

    [Fact]
    public async Task GetLatest_orders_by_newest_first_pages_and_hides_deleted()
    {
        await using (var ctx = _db.NewContext())
        {
            var gradeId = await AddGradeAsync(ctx);
            for (var i = 1; i <= 5; i++)
                ctx.Worksheets.Add(new Worksheet { Name = $"WS{i}", Description = "", GradeId = gradeId, CreateTime = new DateTime(2026, 1, i) });
            await ctx.SaveChangesAsync();
            var deleted = await ctx.Worksheets.FirstAsync(w => w.Name == "WS5");
            deleted.IsDeleted = true;
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var page = await NewService(read).GetLatestWorksheetsAsync(pageNumber: 1, pageSize: 2);

        page.Select(w => w.Name).ShouldBe(new[] { "WS4", "WS3" }); // WS5 deleted, newest first
    }

    [Fact]
    public async Task GetLatest_reports_the_question_count()
    {
        int wsId;
        await using (var ctx = _db.NewContext())
        {
            var worksheet = new Worksheet { Name = "WS", Description = "", GradeId = await AddGradeAsync(ctx) };
            var q1 = new Question { Text = "a" };
            var q2 = new Question { Text = "b" };
            ctx.AddRange(worksheet, q1, q2);
            await ctx.SaveChangesAsync();
            wsId = worksheet.Id;
            ctx.TestQuestions.AddRange(
                new WorksheetQuestion { TestId = worksheet.Id, QuestionId = q1.Id },
                new WorksheetQuestion { TestId = worksheet.Id, QuestionId = q2.Id });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var dto = (await NewService(read).GetLatestWorksheetsAsync(1, 10)).Single(w => w.Id == wsId);
        dto.QuestionCount.ShouldBe(2);
    }

    // ---- GetWorksheetByIdAsync ----

    [Fact]
    public async Task GetWorksheetById_returns_null_when_missing()
    {
        await using var ctx = _db.NewContext();
        (await NewService(ctx).GetWorksheetByIdAsync(404)).ShouldBeNull();
    }

    [Fact]
    public async Task GetWorksheetById_maps_the_worksheet()
    {
        int id;
        await using (var ctx = _db.NewContext())
        {
            var ws = new Worksheet { Name = "Deneme 1", Description = "d", Subtitle = "s", BadgeText = "b", GradeId = await AddGradeAsync(ctx) };
            ctx.Worksheets.Add(ws);
            await ctx.SaveChangesAsync();
            id = ws.Id;
        }

        await using var read = _db.NewContext();
        var dto = await NewService(read).GetWorksheetByIdAsync(id);
        dto.ShouldNotBeNull();
        dto!.Name.ShouldBe("Deneme 1");
        dto.Subtitle.ShouldBe("s");
    }

    // ---- GetExamQuestionsAsync ----

    [Fact]
    public async Task GetExamQuestions_maps_the_subject_name_as_category()
    {
        await using (var ctx = _db.NewContext())
        {
            var subject = new Subject { Name = "Türkçe" };
            ctx.Subjects.Add(subject);
            await ctx.SaveChangesAsync();
            ctx.Questions.Add(new Question { Text = "q", SubjectId = subject.Id, Point = 3 });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var q = (await NewService(read).GetExamQuestionsAsync()).ShouldHaveSingleItem();
        q.CategoryName.ShouldBe("Türkçe");
        q.Point.ShouldBe(3);
    }

    // ---- EndTest ----

    private async Task<(int studentUserId, int instanceId)> SeedStartedInstanceAsync(WorksheetInstanceStatus status = WorksheetInstanceStatus.Started)
    {
        await using var ctx = _db.NewContext();
        var ws = new Worksheet { Name = "WS", Description = "", GradeId = await AddGradeAsync(ctx) };
        var student = new Student { UserId = 900, StudentNumber = "S", SchoolName = "Sch" };
        ctx.AddRange(ws, student);
        await ctx.SaveChangesAsync();
        var inst = new WorksheetInstance
        {
            StudentId = student.Id, WorksheetId = ws.Id, StartTime = DateTime.UtcNow.AddMinutes(-10), Status = status,
        };
        ctx.TestInstances.Add(inst);
        await ctx.SaveChangesAsync();
        return (student.UserId, inst.Id);
    }

    [Fact]
    public async Task EndTest_fails_for_an_unknown_instance()
    {
        await using var ctx = _db.NewContext();
        (await NewSession(ctx).EndTest(999, userId: 1)).Success.ShouldBeFalse();
    }

    [Fact]
    public async Task EndTest_fails_for_another_users_instance()
    {
        var (_, instanceId) = await SeedStartedInstanceAsync();
        await using var ctx = _db.NewContext();
        (await NewSession(ctx).EndTest(instanceId, userId: 111)).Success.ShouldBeFalse();
    }

    [Fact]
    public async Task EndTest_rejects_an_instance_that_is_not_started()
    {
        var (userId, instanceId) = await SeedStartedInstanceAsync(WorksheetInstanceStatus.Completed);
        await using var ctx = _db.NewContext();
        var r = await NewSession(ctx).EndTest(instanceId, userId);
        r.Success.ShouldBeFalse();
        r.Message.ShouldContain("zaten");
    }

    [Fact]
    public async Task EndTest_completes_a_started_instance()
    {
        var (userId, instanceId) = await SeedStartedInstanceAsync();
        await using (var ctx = _db.NewContext())
            (await NewSession(ctx).EndTest(instanceId, userId)).Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        var inst = await check.TestInstances.FirstAsync(x => x.Id == instanceId);
        inst.Status.ShouldBe(WorksheetInstanceStatus.Completed);
        inst.EndTime.ShouldNotBeNull();
    }

    // ---- DeleteWorksheetAsync ----

    [Fact]
    public async Task DeleteWorksheet_fails_when_missing()
    {
        await using var ctx = _db.NewContext();
        (await NewAuthoring(ctx).DeleteWorksheetAsync(404, userId: 1)).Success.ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteWorksheet_soft_deletes_and_records_the_user()
    {
        int id;
        await using (var ctx = _db.NewContext())
        {
            var ws = new Worksheet { Name = "WS", Description = "", GradeId = await AddGradeAsync(ctx) };
            ctx.Worksheets.Add(ws);
            await ctx.SaveChangesAsync();
            id = ws.Id;
        }

        await using (var ctx = _db.NewContext())
            (await NewAuthoring(ctx).DeleteWorksheetAsync(id, userId: 77)).Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        (await check.Worksheets.AnyAsync(w => w.Id == id)).ShouldBeFalse(); // query filter
        var soft = await check.Worksheets.IgnoreQueryFilters().FirstAsync(w => w.Id == id);
        soft.IsDeleted.ShouldBeTrue();
        soft.DeleteUserId.ShouldBe(77);
    }

    public void Dispose() => _db.Dispose();
}
