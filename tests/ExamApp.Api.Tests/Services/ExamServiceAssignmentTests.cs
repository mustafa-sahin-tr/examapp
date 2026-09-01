using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

public class ExamServiceAssignmentTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private ExamService NewService(AppDbContext ctx) => new(ctx, new ImageHelper(), Substitute.For<IMinIoService>());

    private static readonly DateTime Start = new(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);

    private async Task<(int worksheetId, int studentId, int gradeId)> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "8" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();
        var ws = new Worksheet { Name = "Atanacak", Description = "", GradeId = grade.Id };
        var student = new Student { UserId = 1, StudentNumber = "n", SchoolName = "s", GradeId = grade.Id };
        ctx.AddRange(ws, student);
        await ctx.SaveChangesAsync();
        return (ws.Id, student.Id, grade.Id);
    }

    private static WorksheetAssignmentRequestDto Req(int worksheetId, int? studentId = null, int? gradeId = null,
        DateTime? start = null, DateTime? end = null) => new()
    {
        WorksheetId = worksheetId, StudentId = studentId, GradeId = gradeId,
        StartAt = start ?? Start, EndAt = end,
    };

    [Fact]
    public async Task Requires_exactly_one_target()
    {
        var (ws, student, _) = await SeedAsync();
        await using var ctx = _db.NewContext();
        var svc = NewService(ctx);

        (await svc.AssignWorksheetAsync(Req(ws), 1)).Message.ShouldContain("En az bir hedef");
        (await svc.AssignWorksheetAsync(Req(ws, studentId: student, gradeId: 1), 1)).Message.ShouldContain("birlikte seçilemez");
    }

    [Fact]
    public async Task Rejects_an_end_before_the_start()
    {
        var (ws, student, _) = await SeedAsync();
        await using var ctx = _db.NewContext();
        var r = await NewService(ctx).AssignWorksheetAsync(
            Req(ws, studentId: student, end: Start.AddHours(-1)), 1);
        r.Success.ShouldBeFalse();
        r.Message.ShouldContain("Bitiş zamanı");
    }

    [Fact]
    public async Task Validates_the_worksheet_student_and_grade_exist()
    {
        var (ws, student, grade) = await SeedAsync();
        await using var ctx = _db.NewContext();
        var svc = NewService(ctx);

        (await svc.AssignWorksheetAsync(Req(99999, studentId: student), 1)).Message.ShouldContain("Worksheet bulunamadı");
        (await svc.AssignWorksheetAsync(Req(ws, studentId: 99999), 1)).Message.ShouldContain("Öğrenci bulunamadı");
        (await svc.AssignWorksheetAsync(Req(ws, gradeId: 99999), 1)).Message.ShouldContain("Sınıf bulunamadı");
    }

    [Fact]
    public async Task Assigns_to_a_student_and_records_the_assigning_user()
    {
        var (ws, student, _) = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            var r = await NewService(ctx).AssignWorksheetAsync(Req(ws, studentId: student), userId: 55);
            r.Success.ShouldBeTrue();
        }

        await using var check = _db.NewContext();
        var a = await check.WorksheetAssignments.SingleAsync();
        a.StudentId.ShouldBe(student);
        a.GradeId.ShouldBeNull();
        a.CreateUserId.ShouldBe(55);
    }

    [Fact]
    public async Task Assigns_to_a_grade()
    {
        var (ws, _, grade) = await SeedAsync();
        await using (var ctx = _db.NewContext())
            (await NewService(ctx).AssignWorksheetAsync(Req(ws, gradeId: grade), 1)).Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        (await check.WorksheetAssignments.SingleAsync()).GradeId.ShouldBe(grade);
    }

    [Fact]
    public async Task Refuses_an_overlapping_assignment_for_the_same_target()
    {
        var (ws, student, _) = await SeedAsync();
        await using (var ctx = _db.NewContext())
            await NewService(ctx).AssignWorksheetAsync(Req(ws, studentId: student, end: Start.AddDays(7)), 1);

        await using (var ctx = _db.NewContext())
        {
            var r = await NewService(ctx).AssignWorksheetAsync(
                Req(ws, studentId: student, start: Start.AddDays(3), end: Start.AddDays(10)), 1);
            r.Success.ShouldBeFalse();
            r.Message.ShouldContain("mevcut bir atama");
        }
    }

    [Fact]
    public async Task Allows_a_non_overlapping_assignment_for_the_same_target()
    {
        var (ws, student, _) = await SeedAsync();
        await using (var ctx = _db.NewContext())
            await NewService(ctx).AssignWorksheetAsync(Req(ws, studentId: student, end: Start.AddDays(2)), 1);

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).AssignWorksheetAsync(
                Req(ws, studentId: student, start: Start.AddDays(5), end: Start.AddDays(9)), 1)).Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        (await check.WorksheetAssignments.CountAsync()).ShouldBe(2);
    }

    public void Dispose() => _db.Dispose();
}
