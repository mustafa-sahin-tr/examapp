using ExamApp.Api.Data;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Services.Interfaces;
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
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    // issue #222: okullu öğretmen = önceki davranış. Servis scope okulunu öğretmen kaydıyla doğruladığı için
    // TeacherId/OtherTeacherId aynı okulda öğretmen kaydıyla seed edilir (lazy, ilk çağrıda).
    private int? _teacherSchoolId;

    private SchoolScope SchoolTeacher(int userId)
    {
        if (_teacherSchoolId is null)
        {
            using var ctx = _db.NewContext();
            var school = new School { Name = "Öğretmen Okulu" };
            ctx.Schools.Add(school);
            ctx.SaveChanges();
            ctx.Teachers.AddRange(
                new Teacher { UserId = TeacherId, SchoolId = school.Id },
                new Teacher { UserId = OtherTeacherId, SchoolId = school.Id });
            ctx.SaveChanges();
            _teacherSchoolId = school.Id;
        }

        return SchoolScope.For(userId, _teacherSchoolId);
    }

    private TeacherService NewService(AppDbContext ctx) => new(ctx, _authApi);

    [Fact]
    public async Task GetDashboardSummaryAsync_TeacherHasNoWorksheets_ReturnsZeros()
    {
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).GetDashboardSummaryAsync(SchoolTeacher(TeacherId));

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
        var result = await NewService(check).GetDashboardSummaryAsync(SchoolTeacher(TeacherId));

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
        var result = await NewService(check).GetDashboardSummaryAsync(SchoolTeacher(TeacherId));

        result.TotalWorksheets.ShouldBe(0);
        result.TotalUniqueStudents.ShouldBe(0);
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_CountsDistinctDirectlyAssignedStudentsOnce()
    {
        int studentId;
        await using (var ctx = _db.NewContext())
        {
            SchoolTeacher(TeacherId); // Ensure teacher school is initialized
            var grade = new Grade { Name = "8" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();

            ctx.SetCurrentUser(TeacherId);
            var ws1 = new Worksheet { Name = "WS1", Description = "", GradeId = grade.Id };
            var ws2 = new Worksheet { Name = "WS2", Description = "", GradeId = grade.Id };
            var student = new Student { UserId = 100, StudentNumber = "n1", SchoolId = _teacherSchoolId };
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
        var result = await NewService(check).GetDashboardSummaryAsync(SchoolTeacher(TeacherId));

        result.TotalWorksheets.ShouldBe(2);
        result.TotalUniqueStudents.ShouldBe(1);
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_GradeScopedAssignment_IncludesAllStudentsInTheGrade()
    {
        await using (var ctx = _db.NewContext())
        {
            SchoolTeacher(TeacherId); // Ensure teacher school is initialized
            ctx.SetCurrentUser(TeacherId);
            var grade = new Grade { Name = "8" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();

            var ws = new Worksheet { Name = "WS", Description = "", GradeId = grade.Id };
            var s1 = new Student { UserId = 200, StudentNumber = "a", SchoolId = _teacherSchoolId, GradeId = grade.Id };
            var s2 = new Student { UserId = 201, StudentNumber = "b", SchoolId = _teacherSchoolId, GradeId = grade.Id };
            ctx.AddRange(ws, s1, s2);
            await ctx.SaveChangesAsync();

            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id, GradeId = grade.Id, StartAt = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetDashboardSummaryAsync(SchoolTeacher(TeacherId));

        result.TotalUniqueStudents.ShouldBe(2);
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_GradeScopedAssignmentFromOtherSchool_TeacherCannotSeeStudents()
    {
        // Issue #235: even if an assignment targets a specific school (A), a teacher from a different school
        // cannot see those students. Teacher school scope is applied first.
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
        var result = await NewService(check).GetDashboardSummaryAsync(SchoolTeacher(TeacherId));

        result.TotalUniqueStudents.ShouldBe(0); // Teacher is in "Öğretmen Okulu", students are in A and B
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_MixedDirectAndGradeAssignments_CountsOverlappingStudentOnlyOnce()
    {
        await using (var ctx = _db.NewContext())
        {
            SchoolTeacher(TeacherId); // Ensure teacher school is initialized
            ctx.SetCurrentUser(TeacherId);
            var grade = new Grade { Name = "8" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();

            var ws1 = new Worksheet { Name = "WS1", Description = "", GradeId = grade.Id };
            var ws2 = new Worksheet { Name = "WS2", Description = "", GradeId = grade.Id };
            // student appears both as a direct assignment target and as part of the grade.
            var overlapping = new Student { UserId = 400, StudentNumber = "a", SchoolId = _teacherSchoolId, GradeId = grade.Id };
            var gradeOnly = new Student { UserId = 401, StudentNumber = "b", SchoolId = _teacherSchoolId, GradeId = grade.Id };
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
        var result = await NewService(check).GetDashboardSummaryAsync(SchoolTeacher(TeacherId));

        result.TotalWorksheets.ShouldBe(2);
        result.TotalUniqueStudents.ShouldBe(2); // overlapping + gradeOnly, not double-counted
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_SoftDeletedDirectlyAssignedStudent_IsNotCounted()
    {
        await using (var ctx = _db.NewContext())
        {
            SchoolTeacher(TeacherId); // Ensure teacher school is initialized
            var grade = new Grade { Name = "8" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();

            ctx.SetCurrentUser(TeacherId);
            var ws = new Worksheet { Name = "WS", Description = "", GradeId = grade.Id };
            var activeStudent = new Student { UserId = 600, StudentNumber = "a", SchoolId = _teacherSchoolId };
            var deletedStudent = new Student { UserId = 601, StudentNumber = "b", SchoolId = _teacherSchoolId, IsDeleted = true };
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
        var result = await NewService(check).GetDashboardSummaryAsync(SchoolTeacher(TeacherId));

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
        var result = await NewService(check).GetDashboardSummaryAsync(SchoolTeacher(TeacherId));

        result.TotalWorksheets.ShouldBe(1);
        result.TotalUniqueStudents.ShouldBe(0);
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_IsIsolatedPerTeacher_DifferentTeacherIdsYieldDifferentResults()
    {
        await using (var ctx = _db.NewContext())
        {
            SchoolTeacher(TeacherId); // Ensure teacher school is initialized
            var grade = new Grade { Name = "8" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();

            ctx.SetCurrentUser(TeacherId);
            var ws = new Worksheet { Name = "Teacher1 WS", Description = "", GradeId = grade.Id };
            var student = new Student { UserId = 500, StudentNumber = "a", SchoolId = _teacherSchoolId };
            ctx.AddRange(ws, student);
            await ctx.SaveChangesAsync();
            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id, StudentId = student.Id, StartAt = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }

        await using var ctx1 = _db.NewContext();
        var resultForOwner = await NewService(ctx1).GetDashboardSummaryAsync(SchoolTeacher(TeacherId));
        resultForOwner.TotalWorksheets.ShouldBe(1);
        resultForOwner.TotalUniqueStudents.ShouldBe(1);

        await using var ctx2 = _db.NewContext();
        var resultForOtherTeacher = await NewService(ctx2).GetDashboardSummaryAsync(SchoolTeacher(OtherTeacherId));
        resultForOtherTeacher.TotalWorksheets.ShouldBe(0);
        resultForOtherTeacher.TotalUniqueStudents.ShouldBe(0);
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_SchoolTeacherWithCrossSchoolAssignment_OnlySeesTheirSchool()
    {
        // Issue #235: a school-scoped teacher assigned to a cross-school grade worksheet
        // only sees students from their own school, not from other schools in the assignment.
        await using (var ctx = _db.NewContext())
        {
            SchoolTeacher(TeacherId); // Ensure teacher school exists
            var grade = new Grade { Name = "8" };
            var otherSchool = new School { Name = "Diğer Okul" };
            ctx.AddRange(grade, otherSchool);
            await ctx.SaveChangesAsync();

            ctx.SetCurrentUser(TeacherId);
            var ws = new Worksheet { Name = "WS", Description = "", GradeId = grade.Id };
            var studentInTeacherSchool = new Student { UserId = 1003, StudentNumber = "a", SchoolId = _teacherSchoolId, GradeId = grade.Id };
            var studentInOtherSchool = new Student { UserId = 1004, StudentNumber = "b", SchoolId = otherSchool.Id, GradeId = grade.Id };
            ctx.AddRange(ws, studentInTeacherSchool, studentInOtherSchool);
            await ctx.SaveChangesAsync();

            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id, GradeId = grade.Id, StartAt = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetDashboardSummaryAsync(SchoolTeacher(TeacherId));

        result.TotalUniqueStudents.ShouldBe(1); // Only teacher's school student — counter verified
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_AdminWithCrossSchoolAssignment_SeesAllSchools()
    {
        // Acceptance criteria: admin behavior unchanged. Admin sees all students from all schools
        // even in cross-school grade assignments.
        const int adminUserId = 9999;
        await using (var ctx = _db.NewContext())
        {
            SchoolTeacher(TeacherId); // Ensure teacher school exists
            var grade = new Grade { Name = "8" };
            var otherSchool = new School { Name = "Diğer Okul" };
            ctx.AddRange(grade, otherSchool);
            await ctx.SaveChangesAsync();

            ctx.SetCurrentUser(adminUserId); // Admin creates worksheet
            var ws = new Worksheet { Name = "WS", Description = "", GradeId = grade.Id };
            var studentInTeacherSchool = new Student { UserId = 2001, StudentNumber = "a", SchoolId = _teacherSchoolId, GradeId = grade.Id };
            var studentInOtherSchool = new Student { UserId = 2002, StudentNumber = "b", SchoolId = otherSchool.Id, GradeId = grade.Id };
            ctx.AddRange(ws, studentInTeacherSchool, studentInOtherSchool);
            await ctx.SaveChangesAsync();

            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id, GradeId = grade.Id, StartAt = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var adminScope = SchoolScope.Unrestricted(adminUserId);
        var result = await NewService(check).GetDashboardSummaryAsync(adminScope);

        result.TotalUniqueStudents.ShouldBe(2); // Admin sees students from both schools
    }

    [Fact]
    public async Task GetDashboardSummaryAsync_PendingUnschooledTeacherWithGradeAssignment_SeesZero()
    {
        // Acceptance criteria: pending/unscoped teacher (no school, not validated) sees 0 students
        // in grade assignments because grade expansion is disabled (StudentTargetScope.Narrow).
        const int pendingTeacherId = 8888;
        await using (var ctx = _db.NewContext())
        {
            SchoolTeacher(TeacherId); // Ensure other school exists
            var grade = new Grade { Name = "8" };
            ctx.Grades.Add(grade);
            await ctx.SaveChangesAsync();

            ctx.SetCurrentUser(TeacherId);
            var ws = new Worksheet { Name = "WS", Description = "", GradeId = grade.Id };
            var student1 = new Student { UserId = 3001, StudentNumber = "a", SchoolId = _teacherSchoolId, GradeId = grade.Id };
            var student2 = new Student { UserId = 3002, StudentNumber = "b", SchoolId = _teacherSchoolId, GradeId = grade.Id };
            ctx.AddRange(ws, student1, student2);
            await ctx.SaveChangesAsync();

            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id, GradeId = grade.Id, StartAt = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        // Pending teacher: no school (null), not validated. SchoolScope with SchoolId=null is independent/pending.
        var pendingScope = SchoolScope.For(pendingTeacherId, null);
        var result = await NewService(check).GetDashboardSummaryAsync(pendingScope);

        result.TotalUniqueStudents.ShouldBe(0); // No direct assignments, grade expansion disabled for unscoped
    }

    public void Dispose() => _db.Dispose();
}
