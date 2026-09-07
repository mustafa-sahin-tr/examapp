using ExamApp.Api.Data;
using ExamApp.Api.Services;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #53: öğretmen dashboard özet kartları (TotalWorksheets / TotalUniqueStudents).
/// </summary>
public class TeacherServiceDashboardSummaryTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private const int TeacherId = 1;
    private const int OtherTeacherId = 2;

    private TeacherService NewService(AppDbContext ctx) => new(ctx);

    [Fact]
    public async Task GetDashboardSummaryAsync_TeacherHasNoWorksheets_ReturnsZeros()
    {
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).GetDashboardSummaryAsync(TeacherId);

        result.TotalWorksheets.ShouldBe(0);
        result.TotalUniqueStudents.ShouldBe(0);
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_OnlyCountsWorksheetsOwnedByTheTeacher()
    {
        await using (var ctx = _db.NewContext())
        {
            var grade = new Grade { Name = "8" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();

            ctx.SetCurrentUser(TeacherId);
            ctx.Worksheets.Add(new Worksheet { Name = "Owned 1", Description = "", GradeId = grade.Id });
            ctx.Worksheets.Add(new Worksheet { Name = "Owned 2", Description = "", GradeId = grade.Id });
            await ctx.SaveChangesAsync();

            ctx.SetCurrentUser(OtherTeacherId);
            ctx.Worksheets.Add(new Worksheet { Name = "Other teacher's", Description = "", GradeId = grade.Id });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetDashboardSummaryAsync(TeacherId);

        result.TotalWorksheets.ShouldBe(2);
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_SharedWorksheetsFromOtherTeachersAreNotCounted()
    {
        await using (var ctx = _db.NewContext())
        {
            var grade = new Grade { Name = "8" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();

            ctx.SetCurrentUser(OtherTeacherId);
            ctx.Worksheets.Add(new Worksheet
            {
                Name = "Shared by other",
                Description = "",
                GradeId = grade.Id,
                TeacherSharing = WorksheetTeacherSharing.PublicView
            });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetDashboardSummaryAsync(TeacherId);

        result.TotalWorksheets.ShouldBe(0);
        result.TotalUniqueStudents.ShouldBe(0);
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_CountsDistinctDirectlyAssignedStudentsOnce()
    {
        int studentId;
        await using (var ctx = _db.NewContext())
        {
            var grade = new Grade { Name = "8" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();

            ctx.SetCurrentUser(TeacherId);
            var ws1 = new Worksheet { Name = "WS1", Description = "", GradeId = grade.Id };
            var ws2 = new Worksheet { Name = "WS2", Description = "", GradeId = grade.Id };
            var student = new Student { UserId = 100, StudentNumber = "n1", SchoolName = "s" };
            ctx.AddRange(ws1, ws2, student);
            await ctx.SaveChangesAsync();
            studentId = student.Id;

            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws1.Id, StudentId = studentId, StartAt = DateTime.UtcNow
            });
            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws2.Id, StudentId = studentId, StartAt = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetDashboardSummaryAsync(TeacherId);

        result.TotalWorksheets.ShouldBe(2);
        result.TotalUniqueStudents.ShouldBe(1);
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_GradeScopedAssignment_IncludesAllStudentsInTheGrade()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(TeacherId);
            var grade = new Grade { Name = "8" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();

            var ws = new Worksheet { Name = "WS", Description = "", GradeId = grade.Id };
            var s1 = new Student { UserId = 200, StudentNumber = "a", SchoolName = "s", GradeId = grade.Id };
            var s2 = new Student { UserId = 201, StudentNumber = "b", SchoolName = "s", GradeId = grade.Id };
            ctx.AddRange(ws, s1, s2);
            await ctx.SaveChangesAsync();

            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id, GradeId = grade.Id, StartAt = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetDashboardSummaryAsync(TeacherId);

        result.TotalUniqueStudents.ShouldBe(2);
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_GradeScopedAssignmentWithSchoolId_OnlyIncludesStudentsInThatSchool()
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
        var result = await NewService(check).GetDashboardSummaryAsync(TeacherId);

        result.TotalUniqueStudents.ShouldBe(1);
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_MixedDirectAndGradeAssignments_CountsOverlappingStudentOnlyOnce()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(TeacherId);
            var grade = new Grade { Name = "8" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();

            var ws1 = new Worksheet { Name = "WS1", Description = "", GradeId = grade.Id };
            var ws2 = new Worksheet { Name = "WS2", Description = "", GradeId = grade.Id };
            // student appears both as a direct assignment target and as part of the grade.
            var overlapping = new Student { UserId = 400, StudentNumber = "a", SchoolName = "s", GradeId = grade.Id };
            var gradeOnly = new Student { UserId = 401, StudentNumber = "b", SchoolName = "s", GradeId = grade.Id };
            ctx.AddRange(ws1, ws2, overlapping, gradeOnly);
            await ctx.SaveChangesAsync();

            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws1.Id, GradeId = grade.Id, StartAt = DateTime.UtcNow
            });
            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws2.Id, StudentId = overlapping.Id, StartAt = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetDashboardSummaryAsync(TeacherId);

        result.TotalWorksheets.ShouldBe(2);
        result.TotalUniqueStudents.ShouldBe(2); // overlapping + gradeOnly, not double-counted
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_SoftDeletedDirectlyAssignedStudent_IsNotCounted()
    {
        await using (var ctx = _db.NewContext())
        {
            var grade = new Grade { Name = "8" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();

            ctx.SetCurrentUser(TeacherId);
            var ws = new Worksheet { Name = "WS", Description = "", GradeId = grade.Id };
            var activeStudent = new Student { UserId = 600, StudentNumber = "a", SchoolName = "s" };
            var deletedStudent = new Student { UserId = 601, StudentNumber = "b", SchoolName = "s", IsDeleted = true };
            ctx.AddRange(ws, activeStudent, deletedStudent);
            await ctx.SaveChangesAsync();

            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id, StudentId = activeStudent.Id, StartAt = DateTime.UtcNow
            });
            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id, StudentId = deletedStudent.Id, StartAt = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetDashboardSummaryAsync(TeacherId);

        result.TotalWorksheets.ShouldBe(1);
        result.TotalUniqueStudents.ShouldBe(1); // sadece silinmemiş direkt atanan öğrenci sayılır
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_WorksheetWithoutAssignments_ReturnsZeroUniqueStudents()
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
        var result = await NewService(check).GetDashboardSummaryAsync(TeacherId);

        result.TotalWorksheets.ShouldBe(1);
        result.TotalUniqueStudents.ShouldBe(0);
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_IsIsolatedPerTeacher_DifferentTeacherIdsYieldDifferentResults()
    {
        await using (var ctx = _db.NewContext())
        {
            var grade = new Grade { Name = "8" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();

            ctx.SetCurrentUser(TeacherId);
            var ws = new Worksheet { Name = "Teacher1 WS", Description = "", GradeId = grade.Id };
            var student = new Student { UserId = 500, StudentNumber = "a", SchoolName = "s" };
            ctx.AddRange(ws, student);
            await ctx.SaveChangesAsync();
            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id, StudentId = student.Id, StartAt = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }

        await using var ctx1 = _db.NewContext();
        var resultForOwner = await NewService(ctx1).GetDashboardSummaryAsync(TeacherId);
        resultForOwner.TotalWorksheets.ShouldBe(1);
        resultForOwner.TotalUniqueStudents.ShouldBe(1);

        await using var ctx2 = _db.NewContext();
        var resultForOtherTeacher = await NewService(ctx2).GetDashboardSummaryAsync(OtherTeacherId);
        resultForOtherTeacher.TotalWorksheets.ShouldBe(0);
        resultForOtherTeacher.TotalUniqueStudents.ShouldBe(0);
    }

    public void Dispose() => _db.Dispose();
}
