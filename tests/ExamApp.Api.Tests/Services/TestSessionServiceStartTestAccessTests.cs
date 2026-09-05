using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #14: StartTestAsync yalnızca (a) öğrenciye/sınıfına aktif atama varsa
/// VEYA (b) sınav keşfedilebilirse (grade uyumlu + StudentVisibility=Normal) izin verir.
/// </summary>
public class TestSessionServiceStartTestAccessTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    private TestSessionService NewService(AppDbContext ctx) => new(ctx);

    private sealed record World(int GradeA, int GradeB, int StudentId, int WorksheetId);

    private async Task<int> AddWorksheetAsync(AppDbContext ctx, int gradeId, WorksheetStudentVisibility visibility)
    {
        var worksheet = new Worksheet
        {
            Name = "Test",
            Description = "",
            GradeId = gradeId,
            MaxDurationSeconds = 600,
            StudentVisibility = visibility
        };
        ctx.Worksheets.Add(worksheet);
        await ctx.SaveChangesAsync();
        return worksheet.Id;
    }

    private async Task<(int studentId, int gradeAId, int gradeBId)> SeedStudentAsync()
    {
        await using var ctx = _db.NewContext();
        var gradeA = new Grade { Name = "5" };
        var gradeB = new Grade { Name = "6" };
        ctx.AddRange(gradeA, gradeB);
        await ctx.SaveChangesAsync();

        var student = new Student { UserId = 400, StudentNumber = "S1", SchoolName = "Sch", GradeId = gradeA.Id };
        ctx.Students.Add(student);
        await ctx.SaveChangesAsync();

        return (student.Id, gradeA.Id, gradeB.Id);
    }

    [Fact]
    public async Task StartTest_worksheet_does_not_exist_returns_null()
    {
        var (studentId, _, _) = await SeedStudentAsync();

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).StartTestAsync(999, new StudentProfileDto { Id = studentId, GradeId = null });

        result.ShouldBeNull();
    }

    [Fact]
    public async Task StartTest_student_with_active_assignment_can_start_even_when_restricted_and_grade_mismatched()
    {
        var (studentId, gradeA, gradeB) = await SeedStudentAsync();
        int worksheetId;
        await using (var seed = _db.NewContext())
        {
            worksheetId = await AddWorksheetAsync(seed, gradeB, WorksheetStudentVisibility.Restricted);
            seed.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = worksheetId,
                StudentId = studentId,
                StartAt = DateTime.UtcNow.AddDays(-1)
            });
            await seed.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).StartTestAsync(worksheetId, new StudentProfileDto { Id = studentId, GradeId = gradeA });

        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task StartTest_student_with_active_grade_assignment_can_start()
    {
        var (studentId, gradeA, _) = await SeedStudentAsync();
        int worksheetId;
        await using (var seed = _db.NewContext())
        {
            worksheetId = await AddWorksheetAsync(seed, gradeA, WorksheetStudentVisibility.Restricted);
            seed.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = worksheetId,
                StudentId = null,
                GradeId = gradeA,
                StartAt = DateTime.UtcNow.AddDays(-1)
            });
            await seed.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).StartTestAsync(worksheetId, new StudentProfileDto { Id = studentId, GradeId = gradeA });

        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task StartTest_unassigned_but_normal_and_grade_matched_can_start()
    {
        var (studentId, gradeA, _) = await SeedStudentAsync();
        int worksheetId;
        await using (var seed = _db.NewContext())
            worksheetId = await AddWorksheetAsync(seed, gradeA, WorksheetStudentVisibility.Normal);

        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).StartTestAsync(worksheetId, new StudentProfileDto { Id = studentId, GradeId = gradeA });

        result.ShouldNotBeNull();
        result.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task StartTest_unassigned_and_restricted_throws_unauthorized()
    {
        var (studentId, gradeA, _) = await SeedStudentAsync();
        int worksheetId;
        await using (var seed = _db.NewContext())
            worksheetId = await AddWorksheetAsync(seed, gradeA, WorksheetStudentVisibility.Restricted);

        await using var ctx = _db.NewContext();
        var act = () => NewService(ctx).StartTestAsync(worksheetId, new StudentProfileDto { Id = studentId, GradeId = gradeA });

        await Should.ThrowAsync<UnauthorizedAccessException>(act);
    }

    [Fact]
    public async Task StartTest_unassigned_and_grade_mismatched_throws_unauthorized_even_when_normal()
    {
        var (studentId, gradeA, gradeB) = await SeedStudentAsync();
        int worksheetId;
        await using (var seed = _db.NewContext())
            worksheetId = await AddWorksheetAsync(seed, gradeB, WorksheetStudentVisibility.Normal);

        await using var ctx = _db.NewContext();
        var act = () => NewService(ctx).StartTestAsync(worksheetId, new StudentProfileDto { Id = studentId, GradeId = gradeA });

        await Should.ThrowAsync<UnauthorizedAccessException>(act);
    }

    [Fact]
    public async Task StartTest_assignment_not_yet_started_throws_unauthorized_when_restricted()
    {
        var (studentId, gradeA, gradeB) = await SeedStudentAsync();
        int worksheetId;
        await using (var seed = _db.NewContext())
        {
            worksheetId = await AddWorksheetAsync(seed, gradeB, WorksheetStudentVisibility.Restricted);
            seed.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = worksheetId,
                StudentId = studentId,
                StartAt = DateTime.UtcNow.AddDays(1)
            });
            await seed.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var act = () => NewService(ctx).StartTestAsync(worksheetId, new StudentProfileDto { Id = studentId, GradeId = gradeA });

        await Should.ThrowAsync<UnauthorizedAccessException>(act);
    }

    [Fact]
    public async Task StartTest_assignment_already_ended_throws_unauthorized_when_restricted()
    {
        var (studentId, gradeA, gradeB) = await SeedStudentAsync();
        int worksheetId;
        await using (var seed = _db.NewContext())
        {
            worksheetId = await AddWorksheetAsync(seed, gradeB, WorksheetStudentVisibility.Restricted);
            seed.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = worksheetId,
                StudentId = studentId,
                StartAt = DateTime.UtcNow.AddDays(-2),
                EndAt = DateTime.UtcNow.AddDays(-1)
            });
            await seed.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var act = () => NewService(ctx).StartTestAsync(worksheetId, new StudentProfileDto { Id = studentId, GradeId = gradeA });

        await Should.ThrowAsync<UnauthorizedAccessException>(act);
    }

    public void Dispose() => _db.Dispose();
}
