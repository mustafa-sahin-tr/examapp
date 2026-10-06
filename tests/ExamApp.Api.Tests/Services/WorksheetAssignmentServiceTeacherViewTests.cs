using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

public class WorksheetAssignmentServiceTeacherViewTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private WorksheetAssignmentService NewService(AppDbContext ctx) => new(ctx, new SchoolAccessPolicy(ctx));

    private const int TeacherUserId = 500;

    [Fact]
    public async Task Returns_an_empty_name_when_the_worksheet_does_not_exist()
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).GetWorksheetAssignmentsForTeacherAsync(404, SchoolScope.For(TeacherUserId, null));
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
        var result = await NewService(read).GetWorksheetAssignmentsForTeacherAsync(wsId, SchoolScope.For(TeacherUserId, null));
        result.WorksheetName.ShouldBe("Deneme");
        result.Assignments.ShouldBeEmpty();
    }

    [Fact]
    public async Task Aggregates_student_status_for_a_grade_assignment()
    {
        int wsId, gradeId, schoolId;
        await using (var ctx = _db.NewContext())
        {
            var g = new Grade { Name = "6" };
            // issue #192: okulsuz istek sahibi için öğrenci kapsamı Approved Booking'dir; bu test tenancy'yi değil
            // durum toplamayı doğruladığından öğretmen ve öğrenciler aynı okulda seed edilir (#190 kuralı).
            var school = new School { Name = "Okul" };
            ctx.AddRange(g, school);
            await ctx.SaveChangesAsync();
            gradeId = g.Id;
            schoolId = school.Id;

            var ws = new Worksheet { Name = "W", Description = "", GradeId = g.Id };
            ctx.Worksheets.Add(ws);
            await ctx.SaveChangesAsync();
            wsId = ws.Id;

            var s1 = new Student { UserId = 1, StudentNumber = "1", SchoolName = "s", SchoolId = school.Id, SchoolVerifiedAt = DateTime.UtcNow, GradeId = g.Id };
            var s2 = new Student { UserId = 2, StudentNumber = "2", SchoolName = "s", SchoolId = school.Id, SchoolVerifiedAt = DateTime.UtcNow, GradeId = g.Id };
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
        var result = await NewService(read).GetWorksheetAssignmentsForTeacherAsync(wsId, SchoolScope.For(TeacherUserId, schoolId));

        var assignment = result.Assignments.ShouldHaveSingleItem();
        assignment.TargetType.ShouldBe("Grade");
        assignment.Students.Count.ShouldBe(2);
        assignment.CompletedCount.ShouldBe(1);
        assignment.NotStartedCount.ShouldBe(1);
    }

    // issue #367: one live instance per (student, worksheet) - a test solved BEFORE it was assigned cannot be started
    // again, so a Completed instance satisfies a later assignment. An unfinished one still has to start in the window.
    [Fact]
    public async Task Completed_before_the_assignment_then_assigned_counts_as_Completed_but_an_unfinished_early_start_does_not()
    {
        int wsId, schoolId;
        await using (var ctx = _db.NewContext())
        {
            var g = new Grade { Name = "7" };
            var school = new School { Name = "Okul" };
            ctx.AddRange(g, school);
            await ctx.SaveChangesAsync();
            schoolId = school.Id;
            var ws = new Worksheet { Name = "W", Description = "", GradeId = g.Id };
            ctx.Worksheets.Add(ws);
            await ctx.SaveChangesAsync();
            wsId = ws.Id;

            var done = new Student { UserId = 11, StudentNumber = "1", SchoolName = "s", SchoolId = school.Id, SchoolVerifiedAt = DateTime.UtcNow, GradeId = g.Id };
            var open = new Student { UserId = 12, StudentNumber = "2", SchoolName = "s", SchoolId = school.Id, SchoolVerifiedAt = DateTime.UtcNow, GradeId = g.Id };
            ctx.AddRange(done, open);
            await ctx.SaveChangesAsync();

            var startAt = DateTime.UtcNow.AddDays(-1);
            ctx.SetCurrentUser(TeacherUserId);
            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id, GradeId = g.Id, StartAt = startAt, EndAt = DateTime.UtcNow.AddDays(5),
            });
            ctx.TestInstances.AddRange(
                new WorksheetInstance
                {
                    StudentId = done.Id, WorksheetId = ws.Id, Status = WorksheetInstanceStatus.Completed,
                    StartTime = startAt.AddDays(-10), EndTime = startAt.AddDays(-10).AddMinutes(20),
                },
                new WorksheetInstance
                {
                    StudentId = open.Id, WorksheetId = ws.Id, Status = WorksheetInstanceStatus.Started,
                    StartTime = startAt.AddDays(-10),
                });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var result = await NewService(read).GetWorksheetAssignmentsForTeacherAsync(wsId, SchoolScope.For(TeacherUserId, schoolId));

        var assignment = result.Assignments.ShouldHaveSingleItem();
        assignment.CompletedCount.ShouldBe(1);
        assignment.InProgressCount.ShouldBe(0);
        assignment.NotStartedCount.ShouldBe(1);
    }

    [Theory]
    // (instance start offset from StartAt in hours, status, has EndAt, expected)
    [InlineData(-240, WorksheetInstanceStatus.Completed, true, true)]     // completed before the window: counts
    [InlineData(-240, WorksheetInstanceStatus.Started, true, false)]      // unfinished early start: does not
    [InlineData(1, WorksheetInstanceStatus.Started, true, true)]          // started inside: counts (caller resolves status)
    [InlineData(24 * 30, WorksheetInstanceStatus.Completed, true, false)] // started after EndAt: never
    [InlineData(24 * 30, WorksheetInstanceStatus.Completed, false, true)] // open-ended window
    public void AssignmentInstanceWindow_rule(int offsetHours, WorksheetInstanceStatus status, bool hasEndAt, bool expected)
    {
        var startAt = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        DateTime? endAt = hasEndAt ? startAt.AddDays(7) : null;
        AssignmentInstanceWindow.Counts(startAt.AddHours(offsetHours), status, startAt, endAt).ShouldBe(expected);
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
