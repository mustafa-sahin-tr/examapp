using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #190 (güvenlik incelemesi): worksheet SAHİBİ de başka okulun öğrencisine atama yapamaz;
/// red mesajı "öğrenci bulunamadı" ile aynıdır (var/yok oracle'ı kapalı). Aynı okul ve admin serbest.
/// assignments/overview: öğrenci listesi istek sahibinin okuluyla sınırlı.
/// </summary>
public class WorksheetAssignmentServiceSchoolScopeTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private WorksheetAssignmentService NewService(AppDbContext ctx) => new(ctx, new SchoolAccessPolicy());

    private const int OwnerUserId = 1;
    private static readonly DateTime Start = new(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);

    public void Dispose() => _db.Dispose();

    private async Task<(int wsId, int gradeId, int schoolA, int schoolB, int studentA, int studentB, int studentNone)> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        ctx.SetCurrentUser(OwnerUserId);

        var grade = new Grade { Name = "8" };
        var schoolA = new School { Name = "Okul A" };
        var schoolB = new School { Name = "Okul B" };
        ctx.AddRange(grade, schoolA, schoolB);
        await ctx.SaveChangesAsync();

        var ws = new Worksheet { Name = "W", Description = "", GradeId = grade.Id };
        var studentA = new Student { UserId = 10, StudentNumber = "a", SchoolId = schoolA.Id, GradeId = grade.Id };
        var studentB = new Student { UserId = 11, StudentNumber = "b", SchoolId = schoolB.Id, GradeId = grade.Id };
        var studentNone = new Student { UserId = 12, StudentNumber = "n", SchoolId = null, GradeId = grade.Id };
        var owner = new Teacher { UserId = OwnerUserId, SchoolId = schoolA.Id };
        ctx.AddRange(ws, studentA, studentB, studentNone, owner);
        await ctx.SaveChangesAsync();

        return (ws.Id, grade.Id, schoolA.Id, schoolB.Id, studentA.Id, studentB.Id, studentNone.Id);
    }

    private static WorksheetAssignmentRequestDto Req(int wsId, int studentId) => new()
    {
        WorksheetId = wsId, StudentId = studentId, StartAt = Start,
    };

    [Fact]
    public async Task Owner_CannotAssignToStudentOfDifferentSchool_LooksLikeNotFound()
    {
        var (ws, _, _, _, _, studentB, _) = await SeedAsync();
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).AssignWorksheetAsync(Req(ws, studentB), OwnerUserId, isAdmin: false);
        var missing = await NewService(ctx).AssignWorksheetAsync(Req(ws, 99999), OwnerUserId, isAdmin: false);

        r.Success.ShouldBeFalse();
        r.Message.ShouldBe(missing.Message); // oracle kapalı: farklı okul == olmayan öğrenci
        (await ctx.WorksheetAssignments.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Owner_CannotAssignToIndependentStudent_WhenOwnerIsSchoolBound()
    {
        var (ws, _, _, _, _, _, studentNone) = await SeedAsync();
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).AssignWorksheetAsync(Req(ws, studentNone), OwnerUserId, isAdmin: false);

        r.Success.ShouldBeFalse();
        (await ctx.WorksheetAssignments.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Owner_CanAssignToStudentOfSameSchool()
    {
        var (ws, _, _, _, studentA, _, _) = await SeedAsync();
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).AssignWorksheetAsync(Req(ws, studentA), OwnerUserId, isAdmin: false);

        r.Success.ShouldBeTrue();
        (await ctx.WorksheetAssignments.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Admin_CanAssignToStudentOfAnySchool()
    {
        var (ws, _, _, _, _, studentB, _) = await SeedAsync();
        await using var ctx = _db.NewContext();

        var r = await NewService(ctx).AssignWorksheetAsync(Req(ws, studentB), userId: 555, isAdmin: true);

        r.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task Overview_GradeAssignment_ListsOnlyRequestersSchoolStudents()
    {
        var (ws, gradeId, schoolA, _, studentA, _, _) = await SeedAsync();
        await using (var setup = _db.NewContext())
        {
            setup.SetCurrentUser(OwnerUserId);
            setup.WorksheetAssignments.Add(new WorksheetAssignment { WorksheetId = ws, GradeId = gradeId, StartAt = Start });
            await setup.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var overview = await NewService(ctx).GetWorksheetAssignmentsForTeacherAsync(ws, SchoolScope.For(OwnerUserId, schoolA));

        var assignment = overview.Assignments.ShouldHaveSingleItem();
        assignment.Students.Select(s => s.StudentId).ShouldBe(new[] { studentA });
    }

    [Fact]
    public async Task Overview_Unrestricted_ListsAllStudentsInGrade()
    {
        var (ws, gradeId, _, _, studentA, studentB, studentNone) = await SeedAsync();
        await using (var setup = _db.NewContext())
        {
            setup.SetCurrentUser(OwnerUserId);
            setup.WorksheetAssignments.Add(new WorksheetAssignment { WorksheetId = ws, GradeId = gradeId, StartAt = Start });
            await setup.SaveChangesAsync();
        }

        await using var ctx = _db.NewContext();
        var overview = await NewService(ctx).GetWorksheetAssignmentsForTeacherAsync(ws, SchoolScope.Unrestricted(OwnerUserId));

        var assignment = overview.Assignments.ShouldHaveSingleItem();
        assignment.Students.Select(s => s.StudentId).ShouldBe(new[] { studentA, studentB, studentNone }, ignoreOrder: true);
    }
}
