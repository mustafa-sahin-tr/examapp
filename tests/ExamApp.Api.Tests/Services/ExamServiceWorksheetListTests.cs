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

    // ---- Sorting: query behaviour ----

    [Fact]
    public async Task Teacher_list_sorts_alphabetical_asc_by_default_direction()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(
            Filter(sortBy: "Alphabetical"), Teacher);

        page.Items.Select(i => i.Name).ShouldBe(new[] { "Alfa", "Beta", "Gama" });
    }

    [Fact]
    public async Task Teacher_list_sorts_by_question_count_asc()
    {
        var w = await SeedAsync();
        await using (var seed = _db.NewContext())
        {
            var q = new Question { Text = "q", Point = 1 };
            seed.Questions.Add(q);
            await seed.SaveChangesAsync();
            seed.TestQuestions.Add(new WorksheetQuestion { TestId = w.WsB, QuestionId = q.Id, Order = 1 });
            seed.TestQuestions.Add(new WorksheetQuestion { TestId = w.WsB, QuestionId = q.Id, Order = 2 });
            seed.TestQuestions.Add(new WorksheetQuestion { TestId = w.WsOtherA, QuestionId = q.Id, Order = 1 });
            await seed.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(
            Filter(sortBy: "questionCount"), Teacher);

        // Alfa=0, Beta=1, Gama=2  -> ascending
        page.Items.Select(i => i.Name).ShouldBe(new[] { "Alfa", "Beta", "Gama" });
    }

    [Fact]
    public async Task Teacher_list_tie_on_sort_key_breaks_by_id_ascending()
    {
        var w = await SeedAsync();
        await using (var seed = _db.NewContext())
        {
            foreach (var id in new[] { w.WsMathA, w.WsOtherA, w.WsB })
                (await seed.Worksheets.FindAsync(id))!.MaxDurationSeconds = 300;
            await seed.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(
            Filter(sortBy: "duration", sortDir: "desc"), Teacher);

        page.Items.Select(i => i.Id).ShouldBe(new[] { w.WsMathA, w.WsOtherA, w.WsB });
    }

    // ---- Common filters ----

    [Fact]
    public async Task Teacher_list_filters_by_question_count_range()
    {
        var w = await SeedAsync();
        await using (var seed = _db.NewContext())
        {
            var q = new Question { Text = "q", Point = 1 };
            seed.Questions.Add(q);
            await seed.SaveChangesAsync();
            for (var i = 0; i < 3; i++)
                seed.TestQuestions.Add(new WorksheetQuestion { TestId = w.WsOtherA, QuestionId = q.Id, Order = i });
            seed.TestQuestions.Add(new WorksheetQuestion { TestId = w.WsB, QuestionId = q.Id, Order = 0 });
            await seed.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(
            Filter(minQuestionCount: 2, maxQuestionCount: 5), Teacher);

        page.Items.ShouldHaveSingleItem().Name.ShouldBe("Beta");
    }

    [Fact]
    public async Task Teacher_list_filters_by_book_ids()
    {
        var w = await SeedAsync();
        int bookMatch, bookOther;
        await using (var seed = _db.NewContext())
        {
            var b1 = new Book { Name = "Kitap 1" };
            var b2 = new Book { Name = "Kitap 2" };
            seed.AddRange(b1, b2);
            await seed.SaveChangesAsync();
            var bt1 = new BookTest { Name = "BT1", BookId = b1.Id };
            var bt2 = new BookTest { Name = "BT2", BookId = b2.Id };
            seed.AddRange(bt1, bt2);
            await seed.SaveChangesAsync();
            (await seed.Worksheets.FindAsync(w.WsMathA))!.BookTestId = bt1.Id;
            (await seed.Worksheets.FindAsync(w.WsB))!.BookTestId = bt2.Id;
            await seed.SaveChangesAsync();
            bookMatch = b1.Id; bookOther = b2.Id;
        }

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(
            Filter(bookIds: new() { bookMatch }), Teacher);

        page.Items.ShouldHaveSingleItem().Name.ShouldBe("Alfa");
    }

    [Fact]
    public async Task Teacher_list_filters_by_is_practice_test_false()
    {
        var w = await SeedAsync();
        await using (var seed = _db.NewContext())
        {
            (await seed.Worksheets.FindAsync(w.WsMathA))!.IsPracticeTest = true;
            await seed.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(
            Filter(isPracticeTest: false), Teacher);

        page.Items.Select(i => i.Name).ShouldBe(new[] { "Beta", "Gama" });
    }

    // ---- Student status filter ----

    private async Task<int> SeedStudentWithInstancesAsync(World w)
    {
        await using var ctx = _db.NewContext();
        var student = new Student { UserId = 555, StudentNumber = "S1", SchoolName = "Okul", GradeId = w.GradeA };
        ctx.Students.Add(student);
        await ctx.SaveChangesAsync();

        // WsMathA -> Started (in progress), WsOtherA -> Completed, WsB -> no instance
        ctx.TestInstances.AddRange(
            new WorksheetInstance { StudentId = student.Id, WorksheetId = w.WsMathA, StartTime = DateTime.UtcNow, Status = WorksheetInstanceStatus.Started },
            new WorksheetInstance { StudentId = student.Id, WorksheetId = w.WsOtherA, StartTime = DateTime.UtcNow, Status = WorksheetInstanceStatus.Completed });
        await ctx.SaveChangesAsync();
        return student.Id;
    }

    [Fact]
    public async Task Student_list_statuses_not_started_returns_only_worksheets_without_an_instance()
    {
        var w = await SeedAsync();
        var studentId = await SeedStudentWithInstancesAsync(w);
        var student = new StudentProfileDto { Id = studentId, GradeId = w.GradeA };

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForStudentsAsync(
            Filter(grades: new() { w.GradeA, w.GradeB }, statuses: new() { -1 }), student);

        page.Items.Select(i => i.Name).ShouldBe(new[] { "Gama" });
    }

    [Fact]
    public async Task Student_list_statuses_completed_returns_only_completed_worksheets()
    {
        var w = await SeedAsync();
        var studentId = await SeedStudentWithInstancesAsync(w);
        var student = new StudentProfileDto { Id = studentId, GradeId = w.GradeA };

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForStudentsAsync(
            Filter(grades: new() { w.GradeA, w.GradeB }, statuses: new() { 1 }), student);

        page.Items.ShouldHaveSingleItem().Name.ShouldBe("Beta");
    }

    [Fact]
    public async Task Student_list_statuses_in_progress_and_completed_returns_union()
    {
        var w = await SeedAsync();
        var studentId = await SeedStudentWithInstancesAsync(w);
        var student = new StudentProfileDto { Id = studentId, GradeId = w.GradeA };

        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForStudentsAsync(
            Filter(grades: new() { w.GradeA, w.GradeB }, statuses: new() { 0, 1 }, sortBy: "alphabetical"), student);

        page.Items.Select(i => i.Name).ShouldBe(new[] { "Alfa", "Beta" });
    }

    [Fact]
    public async Task Student_list_status_filter_never_widens_beyond_the_students_permitted_grade()
    {
        var w = await SeedAsync();
        var studentId = await SeedStudentWithInstancesAsync(w);
        var student = new StudentProfileDto { Id = studentId, GradeId = w.GradeA };

        await using var ctx = _db.NewContext();
        // no explicit grade filter -> scoped to student's own grade (A); WsB is grade B
        var page = await NewService(ctx).GetWorksheetsForStudentsAsync(
            Filter(statuses: new() { -1, 0, 1 }), student);

        page.Items.Select(i => i.Name).ShouldNotContain("Gama");
    }

    [Fact]
    public async Task Teacher_list_ignores_statuses_filter()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForTeacherAsync(
            Filter(statuses: new() { 1 }), Teacher);

        page.TotalCount.ShouldBe(3);
    }

    public void Dispose() => _db.Dispose();
}
