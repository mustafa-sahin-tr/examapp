using ExamApp.Api.Data;
using ExamApp.Api.Services;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #54: öğretmen dashboard "Sınavlarım" tablosu (GetWorksheetsOverviewAsync).
/// </summary>
public class TeacherServiceWorksheetsOverviewTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private const int TeacherId = 1;
    private const int OtherTeacherId = 2;

    private TeacherService NewService(AppDbContext ctx) => new(ctx);

    [Fact]
    public async Task GetWorksheetsOverviewAsync_TeacherHasNoWorksheets_ReturnsEmptyArray()
    {
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).GetWorksheetsOverviewAsync(TeacherId);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetWorksheetsOverviewAsync_WorksheetWithoutAssignments_ReturnsZeroCountAndZeroPercentage()
    {
        await using (var ctx = _db.NewContext())
        {
            var grade = new Grade { Name = "8" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();

            ctx.SetCurrentUser(TeacherId);
            ctx.Worksheets.Add(new Worksheet { Name = "No assignments", Description = "", GradeId = grade.Id });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetWorksheetsOverviewAsync(TeacherId);

        result.Count.ShouldBe(1);
        result[0].Name.ShouldBe("No assignments");
        result[0].AssignedStudentCount.ShouldBe(0);
        result[0].CompletionPercentage.ShouldBe(0);
    }

    [Fact]
    public async Task GetWorksheetsOverviewAsync_OtherTeachersWorksheet_IsNotReturned()
    {
        await using (var ctx = _db.NewContext())
        {
            var grade = new Grade { Name = "8" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();

            ctx.SetCurrentUser(OtherTeacherId);
            ctx.Worksheets.Add(new Worksheet { Name = "Other teacher's", Description = "", GradeId = grade.Id });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetWorksheetsOverviewAsync(TeacherId);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetWorksheetsOverviewAsync_GradeAssignmentOverlappingDirectAssignment_CountsStudentOnce()
    {
        int worksheetId;
        int overlappingStudentId;
        int gradeOnlyStudentId;
        DateTime startAt = DateTime.UtcNow.AddDays(-1);

        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(TeacherId);
            var grade = new Grade { Name = "8" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();

            var ws = new Worksheet { Name = "WS", Description = "", GradeId = grade.Id };
            var overlapping = new Student { UserId = 400, StudentNumber = "a", SchoolName = "s", GradeId = grade.Id };
            var gradeOnly = new Student { UserId = 401, StudentNumber = "b", SchoolName = "s", GradeId = grade.Id };
            ctx.AddRange(ws, overlapping, gradeOnly);
            await ctx.SaveChangesAsync();

            worksheetId = ws.Id;
            overlappingStudentId = overlapping.Id;
            gradeOnlyStudentId = gradeOnly.Id;

            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id, GradeId = grade.Id, StartAt = startAt
            });
            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id, StudentId = overlapping.Id, StartAt = startAt
            });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetWorksheetsOverviewAsync(TeacherId);

        result.Count.ShouldBe(1);
        result[0].WorksheetId.ShouldBe(worksheetId);
        result[0].AssignedStudentCount.ShouldBe(2); // overlapping + gradeOnly, distinct
    }

    [Fact]
    public async Task GetWorksheetsOverviewAsync_GradeAssignmentWithSchoolId_OnlyIncludesStudentsInThatSchool()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(TeacherId);
            var grade = new Grade { Name = "8" };
            var schoolA = new School { Name = "Okul A" };
            var schoolB = new School { Name = "Okul B" };
            ctx.AddRange(grade, schoolA, schoolB);
            await ctx.SaveChangesAsync();

            var ws = new Worksheet { Name = "WS", Description = "", GradeId = grade.Id };
            var studentInA = new Student { UserId = 300, StudentNumber = "a", SchoolId = schoolA.Id, GradeId = grade.Id };
            var studentInB = new Student { UserId = 301, StudentNumber = "b", SchoolId = schoolB.Id, GradeId = grade.Id };
            ctx.AddRange(ws, studentInA, studentInB);
            await ctx.SaveChangesAsync();

            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id, GradeId = grade.Id, SchoolId = schoolA.Id, StartAt = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetWorksheetsOverviewAsync(TeacherId);

        result.Count.ShouldBe(1);
        result[0].AssignedStudentCount.ShouldBe(1);
    }

    [Fact]
    public async Task GetWorksheetsOverviewAsync_CompletionAtOrAboveFifty_CalculatesPercentageCorrectly()
    {
        // 1 of 2 assigned students completed -> 50%.
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(TeacherId);
            var grade = new Grade { Name = "8" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();

            var ws = new Worksheet { Name = "WS", Description = "", GradeId = grade.Id };
            var completedStudent = new Student { UserId = 700, StudentNumber = "a", SchoolName = "s" };
            var pendingStudent = new Student { UserId = 701, StudentNumber = "b", SchoolName = "s" };
            ctx.AddRange(ws, completedStudent, pendingStudent);
            await ctx.SaveChangesAsync();

            var startAt = DateTime.UtcNow.AddDays(-2);
            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id, StudentId = completedStudent.Id, StartAt = startAt
            });
            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id, StudentId = pendingStudent.Id, StartAt = startAt
            });
            await ctx.SaveChangesAsync();

            ctx.TestInstances.Add(new WorksheetInstance
            {
                WorksheetId = ws.Id,
                StudentId = completedStudent.Id,
                StartTime = startAt.AddHours(1),
                EndTime = startAt.AddHours(2),
                Status = WorksheetInstanceStatus.Completed
            });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetWorksheetsOverviewAsync(TeacherId);

        result.Count.ShouldBe(1);
        result[0].AssignedStudentCount.ShouldBe(2);
        result[0].CompletionPercentage.ShouldBe(50);
    }

    [Fact]
    public async Task GetWorksheetsOverviewAsync_CompletionBelowFifty_CalculatesPercentageCorrectly()
    {
        // 1 of 4 assigned students completed -> 25%.
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(TeacherId);
            var grade = new Grade { Name = "8" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();

            var ws = new Worksheet { Name = "WS", Description = "", GradeId = grade.Id };
            var completedStudent = new Student { UserId = 800, StudentNumber = "a", SchoolName = "s" };
            var s2 = new Student { UserId = 801, StudentNumber = "b", SchoolName = "s" };
            var s3 = new Student { UserId = 802, StudentNumber = "c", SchoolName = "s" };
            var s4 = new Student { UserId = 803, StudentNumber = "d", SchoolName = "s" };
            ctx.AddRange(ws, completedStudent, s2, s3, s4);
            await ctx.SaveChangesAsync();

            var startAt = DateTime.UtcNow.AddDays(-2);
            foreach (var student in new[] { completedStudent, s2, s3, s4 })
            {
                ctx.WorksheetAssignments.Add(new WorksheetAssignment
                {
                    WorksheetId = ws.Id, StudentId = student.Id, StartAt = startAt
                });
            }
            await ctx.SaveChangesAsync();

            ctx.TestInstances.Add(new WorksheetInstance
            {
                WorksheetId = ws.Id,
                StudentId = completedStudent.Id,
                StartTime = startAt.AddHours(1),
                EndTime = startAt.AddHours(2),
                Status = WorksheetInstanceStatus.Completed
            });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetWorksheetsOverviewAsync(TeacherId);

        result.Count.ShouldBe(1);
        result[0].AssignedStudentCount.ShouldBe(4);
        result[0].CompletionPercentage.ShouldBe(25);
    }

    [Fact]
    public async Task GetWorksheetsOverviewAsync_TestInstanceOutsideAssignmentWindow_IsNotCountedAsCompleted()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(TeacherId);
            var grade = new Grade { Name = "8" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();

            var ws = new Worksheet { Name = "WS", Description = "", GradeId = grade.Id };
            var student = new Student { UserId = 900, StudentNumber = "a", SchoolName = "s" };
            ctx.AddRange(ws, student);
            await ctx.SaveChangesAsync();

            var startAt = DateTime.UtcNow.AddDays(-1);
            var endAt = DateTime.UtcNow.AddDays(1);
            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id, StudentId = student.Id, StartAt = startAt, EndAt = endAt
            });
            await ctx.SaveChangesAsync();

            // Completed, but before the assignment window started -> should not count.
            ctx.TestInstances.Add(new WorksheetInstance
            {
                WorksheetId = ws.Id,
                StudentId = student.Id,
                StartTime = startAt.AddDays(-5),
                EndTime = startAt.AddDays(-5).AddHours(1),
                Status = WorksheetInstanceStatus.Completed
            });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetWorksheetsOverviewAsync(TeacherId);

        result.Count.ShouldBe(1);
        result[0].AssignedStudentCount.ShouldBe(1);
        result[0].CompletionPercentage.ShouldBe(0);
    }

    public void Dispose() => _db.Dispose();
}
