using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

public class ExamServiceAssignedViewTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private WorksheetAssignmentService NewService(AppDbContext ctx) => new(ctx);
    private ExamService NewExamService(AppDbContext ctx) => new(ctx, new ImageHelper(), Substitute.For<IMinIoService>());

    private sealed record World(int StudentId, int GradeId, int WsForStudent, int WsForGrade);

    private async Task<World> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var grade = new Grade { Name = "6" };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();

        var student = new Student { UserId = 1, StudentNumber = "n", SchoolName = "s", GradeId = grade.Id };
        var wsStudent = new Worksheet { Name = "Öğrenciye", Description = "", GradeId = grade.Id };
        var wsGrade = new Worksheet { Name = "Sınıfa", Description = "", GradeId = grade.Id };
        ctx.AddRange(student, wsStudent, wsGrade);
        await ctx.SaveChangesAsync();
        return new World(student.Id, grade.Id, wsStudent.Id, wsGrade.Id);
    }

    private StudentProfileDto Student(World w) => new() { Id = w.StudentId, GradeId = w.GradeId };

    private async Task AddAssignmentAsync(int worksheetId, int? studentId, int? gradeId, DateTime start, DateTime? end)
    {
        await using var ctx = _db.NewContext();
        ctx.WorksheetAssignments.Add(new WorksheetAssignment
        {
            WorksheetId = worksheetId, StudentId = studentId, GradeId = gradeId, StartAt = start, EndAt = end,
        });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task No_assignments_returns_an_empty_list()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();
        (await NewService(ctx).GetActiveAssignmentsForStudentAsync(Student(w))).ShouldBeEmpty();
    }

    [Fact]
    public async Task Returns_active_student_and_grade_assignments_but_not_expired_or_future_ones()
    {
        var w = await SeedAsync();
        var now = DateTime.UtcNow;

        await AddAssignmentAsync(w.WsForStudent, studentId: w.StudentId, gradeId: null, now.AddDays(-1), now.AddDays(5));
        await AddAssignmentAsync(w.WsForGrade, studentId: null, gradeId: w.GradeId, now.AddDays(-2), null);
        await AddAssignmentAsync(w.WsForGrade, studentId: null, gradeId: w.GradeId, now.AddDays(-10), now.AddDays(-3)); // expired
        await AddAssignmentAsync(w.WsForStudent, studentId: w.StudentId, gradeId: null, now.AddDays(3), now.AddDays(9)); // future

        await using var ctx = _db.NewContext();
        var active = await NewService(ctx).GetActiveAssignmentsForStudentAsync(Student(w));

        active.Count.ShouldBe(2);
        active.ShouldContain(a => a.WorksheetId == w.WsForStudent && !a.IsGradeAssignment);
        active.ShouldContain(a => a.WorksheetId == w.WsForGrade && a.IsGradeAssignment);
    }

    [Fact]
    public async Task Attaches_the_students_latest_instance_to_an_assignment()
    {
        var w = await SeedAsync();
        var now = DateTime.UtcNow;
        await AddAssignmentAsync(w.WsForStudent, w.StudentId, null, now.AddDays(-1), null);

        int instanceId;
        await using (var ctx = _db.NewContext())
        {
            var inst = new WorksheetInstance
            {
                StudentId = w.StudentId, WorksheetId = w.WsForStudent,
                Status = WorksheetInstanceStatus.Started, StartTime = now.AddHours(-1),
            };
            ctx.TestInstances.Add(inst);
            await ctx.SaveChangesAsync();
            instanceId = inst.Id;
        }

        await using var read = _db.NewContext();
        var a = (await NewService(read).GetActiveAssignmentsForStudentAsync(Student(w))).ShouldHaveSingleItem();
        a.InstanceId.ShouldBe(instanceId);
        a.InstanceStatus.ShouldBe(WorksheetInstanceStatus.Started);
    }

    [Fact]
    public async Task GetWorksheetAndInstances_lists_grade_worksheets_with_the_students_instance()
    {
        var w = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            ctx.TestInstances.Add(new WorksheetInstance
            {
                StudentId = w.StudentId, WorksheetId = w.WsForGrade,
                Status = WorksheetInstanceStatus.Completed, StartTime = DateTime.UtcNow.AddDays(-1),
            });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var list = await NewExamService(read).GetWorksheetAndInstancesAsync(Student(w), w.GradeId);

        list.Count.ShouldBe(2);
        list.Single(x => x.Worksheet.Id == w.WsForGrade).Instance!.Status.ShouldBe(WorksheetInstanceStatus.Completed);
        list.Single(x => x.Worksheet.Id == w.WsForStudent).Instance.ShouldBeNull();
    }

    public void Dispose() => _db.Dispose();
}
