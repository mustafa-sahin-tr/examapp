using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

public class ExamServiceWorksheetListTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private ExamService NewService(AppDbContext ctx) => new(ctx, new ImageHelper(), Substitute.For<IMinIoService>());

    private sealed record World(int GradeA, int GradeB, int SubjectMath, int WsMathA, int WsOtherA, int WsB);

    private async Task<World> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var gradeA = new Grade { Name = "3" };
        var gradeB = new Grade { Name = "4" };
        var math = new Subject { Name = "Matematik" };
        ctx.AddRange(gradeA, gradeB, math);
        await ctx.SaveChangesAsync();

        var wsMathA = new Worksheet { Name = "Alfa", Description = "toplama", GradeId = gradeA.Id, SubjectId = math.Id };
        var wsOtherA = new Worksheet { Name = "Beta", Description = "d", GradeId = gradeA.Id };
        var wsB = new Worksheet { Name = "Gama", Description = "d", GradeId = gradeB.Id };
        ctx.AddRange(wsMathA, wsOtherA, wsB);
        await ctx.SaveChangesAsync();
        return new World(gradeA.Id, gradeB.Id, math.Id, wsMathA.Id, wsOtherA.Id, wsB.Id);
    }

    private static ExamFilterDto Filter(int? id = null, List<int>? grades = null, List<int>? subjects = null,
        string? search = null, int page = 1, int size = 10, string? sortBy = null, string? sortDir = null,
        List<int>? statuses = null, int? minQuestionCount = null, int? maxQuestionCount = null,
        int? minDurationSeconds = null, int? maxDurationSeconds = null, bool? isPracticeTest = null,
        List<int>? bookIds = null) => new()
    {
        id = id ?? 0, gradeIds = grades, subjectIds = subjects, search = search, pageNumber = page, pageSize = size,
        sortBy = sortBy, sortDir = sortDir, statuses = statuses,
        minQuestionCount = minQuestionCount, maxQuestionCount = maxQuestionCount,
        minDurationSeconds = minDurationSeconds, maxDurationSeconds = maxDurationSeconds,
        isPracticeTest = isPracticeTest, bookIds = bookIds,
    };

    private static UserProfileDto Teacher => new() { Id = 1, Role = "Teacher" };

    // ---- Teacher ----

    [Fact]
    public async Task Teacher_list_returns_everything_ordered_by_name_with_paging()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(
            Filter(size: 2, sortBy: "alphabetical"), Teacher);

        page.TotalCount.ShouldBe(3);
        page.Items.Select(i => i.Name).ShouldBe(new[] { "Alfa", "Beta" });
    }

    [Fact]
    public async Task Teacher_list_filters_by_id()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();
        (await NewService(ctx).GetWorksheetsForTeacherAsync(Filter(id: w.WsB), Teacher))
            .Items.ShouldHaveSingleItem().Name.ShouldBe("Gama");
    }

    [Fact]
    public async Task Teacher_list_filters_by_grade_and_subject()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();
        var svc = NewService(ctx);

        (await svc.GetWorksheetsForTeacherAsync(Filter(grades: new() { w.GradeA }), Teacher))
            .TotalCount.ShouldBe(2);
        (await svc.GetWorksheetsForTeacherAsync(Filter(subjects: new() { w.SubjectMath }), Teacher))
            .Items.ShouldHaveSingleItem().Name.ShouldBe("Alfa");
    }

    [Fact]
    public async Task Teacher_list_search_matches_name_or_description()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(Filter(search: "toplama"), Teacher);
        page.Items.ShouldHaveSingleItem().Name.ShouldBe("Alfa");
    }

    // ---- Student ----

    [Fact]
    public async Task Student_list_defaults_to_the_students_own_grade()
    {
        var w = await SeedAsync();
        var student = new StudentProfileDto { Id = 10, GradeId = w.GradeB };

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForStudentsAsync(Filter(), student);
        page.Items.ShouldHaveSingleItem().Name.ShouldBe("Gama");
    }

    [Fact]
    public async Task Student_list_honours_an_explicit_grade_filter()
    {
        var w = await SeedAsync();
        var student = new StudentProfileDto { Id = 10, GradeId = w.GradeB };

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForStudentsAsync(Filter(grades: new() { w.GradeA }), student);
        page.TotalCount.ShouldBe(2);
    }

    [Fact]
    public async Task Teacher_list_sorts_alphabetical_desc()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(
            Filter(sortBy: "alphabetical", sortDir: "desc"), Teacher);

        page.Items.Select(i => i.Name).ShouldBe(new[] { "Gama", "Beta", "Alfa" });
    }

    [Fact]
    public async Task Teacher_list_filters_by_duration_range()
    {
        var w = await SeedAsync();
        await using (var seed = _db.NewContext())
        {
            (await seed.Worksheets.FindAsync(w.WsMathA))!.MaxDurationSeconds = 60;
            (await seed.Worksheets.FindAsync(w.WsOtherA))!.MaxDurationSeconds = 600;
            (await seed.Worksheets.FindAsync(w.WsB))!.MaxDurationSeconds = 1800;
            await seed.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(
            Filter(minDurationSeconds: 120, maxDurationSeconds: 1000, sortBy: "duration"), Teacher);

        page.Items.ShouldHaveSingleItem().Name.ShouldBe("Beta");
    }

    [Fact]
    public async Task Teacher_list_filters_by_is_practice_test()
    {
        var w = await SeedAsync();
        await using (var seed = _db.NewContext())
        {
            (await seed.Worksheets.FindAsync(w.WsMathA))!.IsPracticeTest = true;
            await seed.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(
            Filter(isPracticeTest: true), Teacher);

        page.Items.ShouldHaveSingleItem().Name.ShouldBe("Alfa");
    }

    public void Dispose() => _db.Dispose();
}
