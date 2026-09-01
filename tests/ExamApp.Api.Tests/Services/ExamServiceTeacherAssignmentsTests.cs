using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

public class ExamServiceTeacherAssignmentsTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private ExamService NewService(AppDbContext ctx) => new(ctx, new ImageHelper(), Substitute.For<IMinIoService>());

    private const int TeacherUserId = 500;

    [Fact]
    public async Task Returns_an_empty_name_when_the_worksheet_does_not_exist()
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).GetWorksheetAssignmentsForTeacherAsync(404, TeacherUserId);
        result.WorksheetId.ShouldBe(404);
        result.WorksheetName.ShouldBe("");
    }

    [Fact]
    public async Task Returns_the_worksheet_name_but_no_assignments_when_none_were_made_by_this_teacher()
    {
        int wsId;
        await using (var ctx = _db.NewContext())
        {
            var g = new Grade { Name = "5" };
            ctx.Grades.Add(g);
            await ctx.SaveChangesAsync();
            var ws = new Worksheet { Name = "Deneme", Description = "", GradeId = g.Id };
            ctx.Worksheets.Add(ws);
            await ctx.SaveChangesAsync();
            wsId = ws.Id;
            // an assignment made by a DIFFERENT teacher
            ctx.SetCurrentUser(999);
            ctx.WorksheetAssignments.Add(new WorksheetAssignment { WorksheetId = ws.Id, GradeId = g.Id, StartAt = DateTime.UtcNow });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var result = await NewService(read).GetWorksheetAssignmentsForTeacherAsync(wsId, TeacherUserId);
        result.WorksheetName.ShouldBe("Deneme");
        result.Assignments.ShouldBeEmpty();
    }

    [Fact]
    public async Task Aggregates_student_status_for_a_grade_assignment()
    {
        int wsId, gradeId;
        await using (var ctx = _db.NewContext())
        {
            var g = new Grade { Name = "6" };
            ctx.Grades.Add(g);
            await ctx.SaveChangesAsync();
            gradeId = g.Id;

            var ws = new Worksheet { Name = "W", Description = "", GradeId = g.Id };
            ctx.Worksheets.Add(ws);
            await ctx.SaveChangesAsync();
            wsId = ws.Id;

            var s1 = new Student { UserId = 1, StudentNumber = "1", SchoolName = "s", GradeId = g.Id };
            var s2 = new Student { UserId = 2, StudentNumber = "2", SchoolName = "s", GradeId = g.Id };
            ctx.AddRange(s1, s2);
            await ctx.SaveChangesAsync();

            ctx.SetCurrentUser(TeacherUserId);
            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id, GradeId = g.Id, StartAt = DateTime.UtcNow.AddDays(-1),
            });
            // s1 completed an instance, s2 has none
            ctx.TestInstances.Add(new WorksheetInstance
            {
                StudentId = s1.Id, WorksheetId = ws.Id, Status = WorksheetInstanceStatus.Completed,
                StartTime = DateTime.UtcNow.AddHours(-2), EndTime = DateTime.UtcNow.AddHours(-1),
            });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var result = await NewService(read).GetWorksheetAssignmentsForTeacherAsync(wsId, TeacherUserId);

        var assignment = result.Assignments.ShouldHaveSingleItem();
        assignment.TargetType.ShouldBe("Grade");
        assignment.Students.Count.ShouldBe(2);
        assignment.CompletedCount.ShouldBe(1);
        assignment.NotStartedCount.ShouldBe(1);
    }

    // ---- WorksheetAssignment entity ----

    [Theory]
    [InlineData(5, null, true, false)]   // grade-scoped
    [InlineData(null, 9, false, true)]   // student-scoped
    [InlineData(5, 9, false, true)]      // both -> student wins
    public void WorksheetAssignment_scope_flags(int? gradeId, int? studentId, bool grade, bool student)
    {
        var a = new WorksheetAssignment { GradeId = gradeId, StudentId = studentId };
        a.IsGradeScoped.ShouldBe(grade);
        a.IsStudentScoped.ShouldBe(student);
    }

    public void Dispose() => _db.Dispose();
}
