using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #14: GetWorksheetsForStudentsAsync yalnızca atanmış VEYA keşfedilebilir
/// (grade uyumlu + StudentVisibility=Normal) sınavları döndürür; her satır için IsAssigned
/// doğru işaretlenir.
/// </summary>
public class ExamServiceStudentDiscoveryTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private ExamService NewService(AppDbContext ctx) => new(ctx, new ImageHelper(), Substitute.For<IMinIoService>(), Substitute.For<IAuthApiClient>());

    private static ExamFilterDto Filter(int page = 1, int size = 10) => new()
    {
        id = 0, pageNumber = page, pageSize = size
    };

    private async Task<int> AddWorksheetAsync(AppDbContext ctx, string name, int gradeId, WorksheetStudentVisibility visibility)
    {
        var worksheet = new Worksheet
        {
            Name = name,
            Description = "d",
            GradeId = gradeId,
            StudentVisibility = visibility
        };
        ctx.Worksheets.Add(worksheet);
        await ctx.SaveChangesAsync();
        return worksheet.Id;
    }

    private async Task<int> SeedGradeAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "5" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();
        return grade.Id;
    }

    [Fact]
    public async Task Restricted_and_unassigned_worksheet_is_not_visible()
    {
        var gradeId = await SeedGradeAsync();
        await using (var seed = _db.NewContext())
            await AddWorksheetAsync(seed, "Gizli", gradeId, WorksheetStudentVisibility.Restricted);

        var student = new StudentProfileDto { Id = 1, GradeId = gradeId };
        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForStudentsAsync(Filter(), student);

        page.TotalCount.ShouldBe(0);
        page.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Normal_and_grade_matched_worksheet_is_visible_and_not_marked_assigned()
    {
        var gradeId = await SeedGradeAsync();
        await using (var seed = _db.NewContext())
            await AddWorksheetAsync(seed, "Kesfet", gradeId, WorksheetStudentVisibility.Normal);

        var student = new StudentProfileDto { Id = 1, GradeId = gradeId };
        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForStudentsAsync(Filter(), student);

        var item = page.Items.ShouldHaveSingleItem();
        item.Name.ShouldBe("Kesfet");
        item.IsAssigned.ShouldBeFalse();
    }

    [Fact]
    public async Task Restricted_but_assigned_worksheet_is_visible_and_marked_assigned()
    {
        var gradeId = await SeedGradeAsync();
        int worksheetId;
        int studentId;
        await using (var seed = _db.NewContext())
        {
            worksheetId = await AddWorksheetAsync(seed, "Atanan", gradeId, WorksheetStudentVisibility.Restricted);
            var student = new Student { UserId = 700, StudentNumber = "S1", SchoolName = "Sch", GradeId = gradeId };
            seed.Students.Add(student);
            await seed.SaveChangesAsync();
            studentId = student.Id;

            seed.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = worksheetId,
                StudentId = studentId,
                StartAt = DateTime.UtcNow.AddDays(-1)
            });
            await seed.SaveChangesAsync();
        }

        var studentProfile = new StudentProfileDto { Id = studentId, GradeId = gradeId };
        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForStudentsAsync(Filter(), studentProfile);

        var item = page.Items.ShouldHaveSingleItem();
        item.Name.ShouldBe("Atanan");
        item.IsAssigned.ShouldBeTrue();
    }

    [Fact]
    public async Task Normal_grade_matched_and_unassigned_worksheet_is_visible_but_not_marked_assigned()
    {
        var gradeId = await SeedGradeAsync();
        int worksheetId;
        int studentId;
        await using (var seed = _db.NewContext())
        {
            worksheetId = await AddWorksheetAsync(seed, "Serbest", gradeId, WorksheetStudentVisibility.Normal);
            var student = new Student { UserId = 701, StudentNumber = "S2", SchoolName = "Sch", GradeId = gradeId };
            seed.Students.Add(student);
            await seed.SaveChangesAsync();
            studentId = student.Id;
        }

        var studentProfile = new StudentProfileDto { Id = studentId, GradeId = gradeId };
        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForStudentsAsync(Filter(), studentProfile);

        var item = page.Items.ShouldHaveSingleItem();
        item.Name.ShouldBe("Serbest");
        item.IsAssigned.ShouldBeFalse();
    }

    [Fact]
    public async Task Restricted_worksheet_with_grade_scoped_assignment_is_visible_to_that_grades_students()
    {
        var gradeId = await SeedGradeAsync();
        int worksheetId;
        await using (var seed = _db.NewContext())
        {
            worksheetId = await AddWorksheetAsync(seed, "SinifaOzel", gradeId, WorksheetStudentVisibility.Restricted);
            seed.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = worksheetId,
                GradeId = gradeId,
                StudentId = null,
                StartAt = DateTime.UtcNow.AddDays(-1)
            });
            await seed.SaveChangesAsync();
        }

        // Sınıfa atanmış (StudentId=null, GradeId dolu) bir atama, o sınıftaki her öğrenciye görünür olmalı.
        var student = new StudentProfileDto { Id = 1, GradeId = gradeId };
        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForStudentsAsync(Filter(), student);

        var item = page.Items.ShouldHaveSingleItem();
        item.Name.ShouldBe("SinifaOzel");
        item.IsAssigned.ShouldBeTrue();
    }

    [Fact]
    public async Task Restricted_worksheet_with_grade_scoped_assignment_for_another_grade_is_not_visible()
    {
        await using var seedGrades = _db.NewContext();
        var gradeA = new Grade { Name = "5" };
        var gradeB = new Grade { Name = "6" };
        seedGrades.AddRange(gradeA, gradeB);
        await seedGrades.SaveChangesAsync();

        int worksheetId;
        await using (var seed = _db.NewContext())
        {
            worksheetId = await AddWorksheetAsync(seed, "BaskaSinif", gradeB.Id, WorksheetStudentVisibility.Restricted);
            seed.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = worksheetId,
                GradeId = gradeB.Id,
                StudentId = null,
                StartAt = DateTime.UtcNow.AddDays(-1)
            });
            await seed.SaveChangesAsync();
        }

        // Öğrenci gradeA'da; atama gradeB'ye özel — görünmemeli.
        var student = new StudentProfileDto { Id = 1, GradeId = gradeA.Id };
        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForStudentsAsync(
            new ExamFilterDto { id = 0, pageNumber = 1, pageSize = 10, gradeIds = new() { gradeA.Id, gradeB.Id } }, student);

        page.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Restricted_worksheet_with_not_yet_started_assignment_is_not_visible()
    {
        var gradeId = await SeedGradeAsync();
        int worksheetId;
        int studentId;
        await using (var seed = _db.NewContext())
        {
            worksheetId = await AddWorksheetAsync(seed, "HenuzBaslamadi", gradeId, WorksheetStudentVisibility.Restricted);
            var student = new Student { UserId = 702, StudentNumber = "S3", SchoolName = "Sch", GradeId = gradeId };
            seed.Students.Add(student);
            await seed.SaveChangesAsync();
            studentId = student.Id;

            seed.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = worksheetId,
                StudentId = studentId,
                StartAt = DateTime.UtcNow.AddDays(1)
            });
            await seed.SaveChangesAsync();
        }

        var studentProfile = new StudentProfileDto { Id = studentId, GradeId = gradeId };
        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForStudentsAsync(Filter(), studentProfile);

        page.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Restricted_worksheet_with_expired_assignment_is_not_visible()
    {
        var gradeId = await SeedGradeAsync();
        int worksheetId;
        int studentId;
        await using (var seed = _db.NewContext())
        {
            worksheetId = await AddWorksheetAsync(seed, "SuresiDoldu", gradeId, WorksheetStudentVisibility.Restricted);
            var student = new Student { UserId = 703, StudentNumber = "S4", SchoolName = "Sch", GradeId = gradeId };
            seed.Students.Add(student);
            await seed.SaveChangesAsync();
            studentId = student.Id;

            seed.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = worksheetId,
                StudentId = studentId,
                StartAt = DateTime.UtcNow.AddDays(-2),
                EndAt = DateTime.UtcNow.AddDays(-1)
            });
            await seed.SaveChangesAsync();
        }

        var studentProfile = new StudentProfileDto { Id = studentId, GradeId = gradeId };
        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForStudentsAsync(Filter(), studentProfile);

        page.Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Normal_and_grade_matched_worksheet_with_expired_assignment_is_visible_but_not_marked_assigned()
    {
        var gradeId = await SeedGradeAsync();
        int worksheetId;
        int studentId;
        await using (var seed = _db.NewContext())
        {
            worksheetId = await AddWorksheetAsync(seed, "SerbestSuresiDoldu", gradeId, WorksheetStudentVisibility.Normal);
            var student = new Student { UserId = 704, StudentNumber = "S5", SchoolName = "Sch", GradeId = gradeId };
            seed.Students.Add(student);
            await seed.SaveChangesAsync();
            studentId = student.Id;

            seed.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = worksheetId,
                StudentId = studentId,
                StartAt = DateTime.UtcNow.AddDays(-2),
                EndAt = DateTime.UtcNow.AddDays(-1)
            });
            await seed.SaveChangesAsync();
        }

        var studentProfile = new StudentProfileDto { Id = studentId, GradeId = gradeId };
        await using var ctx = _db.NewContext();
        var page = await NewService(ctx).GetWorksheetsForStudentsAsync(Filter(), studentProfile);

        var item = page.Items.ShouldHaveSingleItem();
        item.Name.ShouldBe("SerbestSuresiDoldu");
        item.IsAssigned.ShouldBeFalse();
    }

    public void Dispose() => _db.Dispose();
}
