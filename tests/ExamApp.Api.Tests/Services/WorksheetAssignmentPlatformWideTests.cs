using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #277 (madde 7): <see cref="WorksheetAssignment.IsPlatformWide"/> — öğrenci görünürlük predikatı
/// (<see cref="WorksheetStudentAccess.AssignmentVisibleTo"/>) fail-closed: <c>IsPlatformWide || SchoolId == öğrencinin okulu</c>.
/// Platform geneli satırı yalnızca admin yazar; öğretmen sınıf ataması her zaman kendi okuluna, öğrenci ataması false.
/// </summary>
public class WorksheetAssignmentPlatformWideTests : IDisposable
{
    private const int SchoolTeacherUserId = 1;
    private const int AdminUserId = 99;
    private static readonly DateTime Start = DateTime.UtcNow.AddDays(-1);

    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private WorksheetAssignmentService NewService(AppDbContext ctx) => new(ctx, new SchoolAccessPolicy(ctx));

    private sealed record Seed(int GradeId, int SchoolA, int SchoolB, int Ws, int StudentA, int StudentB, int StudentNoSchool);

    private async Task<Seed> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "8" };
        var a = new School { Name = "A" };
        var b = new School { Name = "B" };
        ctx.AddRange(grade, a, b);
        await ctx.SaveChangesAsync();

        var studentA = new Student { UserId = 10, StudentNumber = "a", SchoolId = a.Id, GradeId = grade.Id };
        var studentB = new Student { UserId = 11, StudentNumber = "b", SchoolId = b.Id, GradeId = grade.Id };
        var studentNone = new Student { UserId = 12, StudentNumber = "n", SchoolId = null, GradeId = grade.Id };
        ctx.AddRange(studentA, studentB, studentNone, new Teacher { UserId = SchoolTeacherUserId, SchoolId = a.Id });
        await ctx.SaveChangesAsync();

        ctx.SetCurrentUser(SchoolTeacherUserId);
        var ws = new Worksheet { Name = "WS", Description = "", GradeId = grade.Id };
        ctx.Worksheets.Add(ws);
        await ctx.SaveChangesAsync();

        return new Seed(grade.Id, a.Id, b.Id, ws.Id, studentA.Id, studentB.Id, studentNone.Id);
    }

    private static WorksheetAssignmentRequestDto GradeReq(int ws, int grade) => new() { WorksheetId = ws, GradeId = grade, StartAt = Start };

    [Fact]
    public async Task Admin_grade_assignment_is_written_platform_wide()
    {
        var s = await SeedAsync();
        await using var ctx = _db.NewContext();

        (await NewService(ctx).AssignWorksheetAsync(GradeReq(s.Ws, s.GradeId), SchoolScope.Unrestricted(AdminUserId))).Success.ShouldBeTrue();

        var a = await ctx.WorksheetAssignments.AsNoTracking().SingleAsync();
        a.SchoolId.ShouldBeNull();
        a.IsPlatformWide.ShouldBeTrue();
    }

    [Fact]
    public async Task School_teacher_grade_assignment_is_not_platform_wide()
    {
        var s = await SeedAsync();
        await using var ctx = _db.NewContext();

        (await NewService(ctx).AssignWorksheetAsync(GradeReq(s.Ws, s.GradeId), SchoolScope.For(SchoolTeacherUserId, s.SchoolA)))
            .Success.ShouldBeTrue();

        var a = await ctx.WorksheetAssignments.AsNoTracking().SingleAsync();
        a.SchoolId.ShouldBe(s.SchoolA);
        a.IsPlatformWide.ShouldBeFalse();
    }

    [Fact]
    public async Task Admin_student_assignment_is_not_platform_wide()
    {
        var s = await SeedAsync();
        await using var ctx = _db.NewContext();

        (await NewService(ctx).AssignWorksheetAsync(
                new WorksheetAssignmentRequestDto { WorksheetId = s.Ws, StudentId = s.StudentB, StartAt = Start },
                SchoolScope.Unrestricted(AdminUserId)))
            .Success.ShouldBeTrue();

        (await ctx.WorksheetAssignments.AsNoTracking().SingleAsync()).IsPlatformWide.ShouldBeFalse();
    }

    [Theory]
    // schoolScoped: atama A okuluna; platformWide: admin satırı; legacyNull: SchoolId=null + IsPlatformWide=false (fail-closed)
    [InlineData("schoolScoped", true, false, false)]
    [InlineData("platformWide", true, true, true)]
    [InlineData("legacyNull", false, false, false)]
    public async Task Grade_assignment_visibility_matrix(string kind, bool seenByA, bool seenByB, bool seenByNoSchool)
    {
        var s = await SeedAsync();
        await using (var setup = _db.NewContext())
        {
            setup.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = s.Ws, GradeId = s.GradeId, StartAt = Start,
                SchoolId = kind == "schoolScoped" ? s.SchoolA : null,
                IsPlatformWide = kind == "platformWide",
            });
            await setup.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        async Task<bool> Sees(int studentId, int? schoolId) =>
            await ctx.ActiveAssignmentsFor(studentId, s.GradeId, schoolId, DateTime.UtcNow).AnyAsync();

        (await Sees(s.StudentA, s.SchoolA)).ShouldBe(seenByA);
        (await Sees(s.StudentB, s.SchoolB)).ShouldBe(seenByB);
        (await Sees(s.StudentNoSchool, null)).ShouldBe(seenByNoSchool);
    }

    [Fact]
    public async Task Direct_student_assignment_stays_visible_regardless_of_school_or_flag()
    {
        var s = await SeedAsync();
        await using (var setup = _db.NewContext())
        {
            setup.WorksheetAssignments.Add(new WorksheetAssignment { WorksheetId = s.Ws, StudentId = s.StudentNoSchool, StartAt = Start });
            await setup.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        (await ctx.ActiveAssignmentsFor(s.StudentNoSchool, s.GradeId, null, DateTime.UtcNow).AnyAsync()).ShouldBeTrue();
        (await ctx.ActiveAssignmentsFor(s.StudentB, s.GradeId, s.SchoolB, DateTime.UtcNow).AnyAsync()).ShouldBeFalse();
    }
}
