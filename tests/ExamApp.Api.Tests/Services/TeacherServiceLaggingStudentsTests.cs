using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #55: öğretmen dashboard "Geride Kalan Öğrenciler" listesi (GetLaggingStudentsAsync).
/// </summary>
public class TeacherServiceLaggingStudentsTests : IDisposable
{
    private const int TeacherId = 1;
    private const int OtherTeacherId = 2;

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    public TeacherServiceLaggingStudentsTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new List<UserLookupResultDto>());
    }

    private TeacherService NewService(AppDbContext ctx) => new(ctx, _authApi);

    private static async Task<int> SeedGradeAsync(AppDbContext ctx, string name = "8")
    {
        var grade = new Grade { Name = name };
        ctx.Grades.Add(grade);
        await ctx.SaveChangesAsync();
        return grade.Id;
    }

    [Fact]
    public async Task GetLaggingStudentsAsync_TeacherHasNoWorksheets_ReturnsEmptyArray()
    {
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).GetLaggingStudentsAsync(TeacherId);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetLaggingStudentsAsync_WorksheetHasNoAssignments_ReturnsEmptyArray()
    {
        await using (var ctx = _db.NewContext())
        {
            var gradeId = await SeedGradeAsync(ctx);
            ctx.SetCurrentUser(TeacherId);
            ctx.Worksheets.Add(new Worksheet { Name = "WS", Description = "", GradeId = gradeId });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetLaggingStudentsAsync(TeacherId);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetLaggingStudentsAsync_OtherTeachersWorksheet_IsNotReturned()
    {
        int worksheetId;
        int studentId;
        var startAt = DateTime.UtcNow.AddDays(-2);

        await using (var ctx = _db.NewContext())
        {
            var gradeId = await SeedGradeAsync(ctx);
            ctx.SetCurrentUser(OtherTeacherId);
            var ws = new Worksheet { Name = "Other's WS", Description = "", GradeId = gradeId };
            var student = new Student { UserId = 100, StudentNumber = "a", SchoolName = "s" };
            ctx.AddRange(ws, student);
            await ctx.SaveChangesAsync();
            worksheetId = ws.Id;
            studentId = student.Id;

            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = worksheetId, StudentId = studentId, StartAt = startAt
            });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetLaggingStudentsAsync(TeacherId);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetLaggingStudentsAsync_AssignmentNotYetStarted_IsExcluded()
    {
        await using (var ctx = _db.NewContext())
        {
            var gradeId = await SeedGradeAsync(ctx);
            ctx.SetCurrentUser(TeacherId);
            var ws = new Worksheet { Name = "WS", Description = "", GradeId = gradeId };
            var student = new Student { UserId = 200, StudentNumber = "a", SchoolName = "s" };
            ctx.AddRange(ws, student);
            await ctx.SaveChangesAsync();

            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id, StudentId = student.Id, StartAt = DateTime.UtcNow.AddDays(1)
            });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetLaggingStudentsAsync(TeacherId);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetLaggingStudentsAsync_CompletedAndExpired_IsExcluded()
    {
        // Completed inside window, window already ended -> not low completion, not expired -> excluded entirely.
        await using (var ctx = _db.NewContext())
        {
            var gradeId = await SeedGradeAsync(ctx);
            ctx.SetCurrentUser(TeacherId);
            var ws = new Worksheet { Name = "WS", Description = "", GradeId = gradeId };
            var student = new Student { UserId = 300, StudentNumber = "a", SchoolName = "s" };
            ctx.AddRange(ws, student);
            await ctx.SaveChangesAsync();

            var startAt = DateTime.UtcNow.AddDays(-5);
            var endAt = DateTime.UtcNow.AddDays(-1);
            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id, StudentId = student.Id, StartAt = startAt, EndAt = endAt
            });
            ctx.TestInstances.Add(new WorksheetInstance
            {
                WorksheetId = ws.Id,
                StudentId = student.Id,
                StartTime = startAt.AddHours(1),
                EndTime = startAt.AddHours(2),
                Status = WorksheetInstanceStatus.Completed
            });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetLaggingStudentsAsync(TeacherId);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetLaggingStudentsAsync_CompletedAndNotExpired_IsExcluded()
    {
        await using (var ctx = _db.NewContext())
        {
            var gradeId = await SeedGradeAsync(ctx);
            ctx.SetCurrentUser(TeacherId);
            var ws = new Worksheet { Name = "WS", Description = "", GradeId = gradeId };
            var student = new Student { UserId = 310, StudentNumber = "a", SchoolName = "s" };
            ctx.AddRange(ws, student);
            await ctx.SaveChangesAsync();

            var startAt = DateTime.UtcNow.AddDays(-2);
            var endAt = DateTime.UtcNow.AddDays(5);
            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id, StudentId = student.Id, StartAt = startAt, EndAt = endAt
            });
            ctx.TestInstances.Add(new WorksheetInstance
            {
                WorksheetId = ws.Id,
                StudentId = student.Id,
                StartTime = startAt.AddHours(1),
                EndTime = startAt.AddHours(2),
                Status = WorksheetInstanceStatus.Completed
            });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetLaggingStudentsAsync(TeacherId);

        result.ShouldBeEmpty();
    }

    [Fact]
    public async Task GetLaggingStudentsAsync_NotCompletedAndNotExpired_IsLowCompletionOnly()
    {
        await using (var ctx = _db.NewContext())
        {
            var gradeId = await SeedGradeAsync(ctx);
            ctx.SetCurrentUser(TeacherId);
            var ws = new Worksheet { Name = "WS", Description = "", GradeId = gradeId };
            var student = new Student { UserId = 400, StudentNumber = "a", SchoolName = "s" };
            ctx.AddRange(ws, student);
            await ctx.SaveChangesAsync();

            var startAt = DateTime.UtcNow.AddDays(-1);
            var endAt = DateTime.UtcNow.AddDays(5); // not expired yet
            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id, StudentId = student.Id, StartAt = startAt, EndAt = endAt
            });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetLaggingStudentsAsync(TeacherId);

        result.Count.ShouldBe(1);
        result[0].IsLowCompletion.ShouldBeTrue();
        result[0].IsExpired.ShouldBeFalse();
        result[0].CompletionPercentage.ShouldBe(0);
    }

    [Fact]
    public async Task GetLaggingStudentsAsync_NotCompletedAndExpired_BothFlagsTrue()
    {
        await using (var ctx = _db.NewContext())
        {
            var gradeId = await SeedGradeAsync(ctx);
            ctx.SetCurrentUser(TeacherId);
            var ws = new Worksheet { Name = "WS", Description = "", GradeId = gradeId };
            var student = new Student { UserId = 500, StudentNumber = "a", SchoolName = "s" };
            ctx.AddRange(ws, student);
            await ctx.SaveChangesAsync();

            var startAt = DateTime.UtcNow.AddDays(-10);
            var endAt = DateTime.UtcNow.AddDays(-1); // already expired
            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id, StudentId = student.Id, StartAt = startAt, EndAt = endAt
            });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetLaggingStudentsAsync(TeacherId);

        result.Count.ShouldBe(1);
        result[0].IsLowCompletion.ShouldBeTrue();
        result[0].IsExpired.ShouldBeTrue();
    }

    [Fact]
    public async Task GetLaggingStudentsAsync_TestInstanceStatusExpired_BothFlagsTrue()
    {
        // TestInstance.Status = Expired doesn't count as completed, and the assignment window
        // has already passed -> both IsLowCompletion and IsExpired should be true together.
        await using (var ctx = _db.NewContext())
        {
            var gradeId = await SeedGradeAsync(ctx);
            ctx.SetCurrentUser(TeacherId);
            var ws = new Worksheet { Name = "WS", Description = "", GradeId = gradeId };
            var student = new Student { UserId = 600, StudentNumber = "a", SchoolName = "s" };
            ctx.AddRange(ws, student);
            await ctx.SaveChangesAsync();

            var startAt = DateTime.UtcNow.AddDays(-10);
            var endAt = DateTime.UtcNow.AddDays(-1);
            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id, StudentId = student.Id, StartAt = startAt, EndAt = endAt
            });
            ctx.TestInstances.Add(new WorksheetInstance
            {
                WorksheetId = ws.Id,
                StudentId = student.Id,
                StartTime = startAt.AddHours(1),
                EndTime = null,
                Status = WorksheetInstanceStatus.Expired
            });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetLaggingStudentsAsync(TeacherId);

        result.Count.ShouldBe(1);
        result[0].IsLowCompletion.ShouldBeTrue();
        result[0].IsExpired.ShouldBeTrue();
    }

    [Fact]
    public async Task GetLaggingStudentsAsync_GradeAndDirectAssignmentOverlap_UsesMostRecentlyStartedWindow()
    {
        // Older grade assignment already expired; a newer direct assignment for the same
        // worksheet/student is still open -> the newer window should win (not-expired, low completion).
        await using (var ctx = _db.NewContext())
        {
            ctx.SetCurrentUser(TeacherId);
            var gradeId = await SeedGradeAsync(ctx);

            var ws = new Worksheet { Name = "WS", Description = "", GradeId = gradeId };
            var student = new Student { UserId = 700, StudentNumber = "a", SchoolName = "s", GradeId = gradeId };
            ctx.AddRange(ws, student);
            await ctx.SaveChangesAsync();

            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id,
                GradeId = gradeId,
                StartAt = DateTime.UtcNow.AddDays(-30),
                EndAt = DateTime.UtcNow.AddDays(-20) // long expired
            });
            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id,
                StudentId = student.Id,
                StartAt = DateTime.UtcNow.AddDays(-1),
                EndAt = DateTime.UtcNow.AddDays(5) // still open
            });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetLaggingStudentsAsync(TeacherId);

        result.Count.ShouldBe(1);
        result[0].IsLowCompletion.ShouldBeTrue();
        result[0].IsExpired.ShouldBeFalse();
    }

    [Fact]
    public async Task GetLaggingStudentsAsync_AuthApiThrows_FallsBackToStudentNumber()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns<IReadOnlyList<UserLookupResultDto>>(_ => throw new HttpRequestException("boom"));

        await using (var ctx = _db.NewContext())
        {
            var gradeId = await SeedGradeAsync(ctx);
            ctx.SetCurrentUser(TeacherId);
            var ws = new Worksheet { Name = "WS", Description = "", GradeId = gradeId };
            var student = new Student { UserId = 800, StudentNumber = "S-42", SchoolName = "s" };
            ctx.AddRange(ws, student);
            await ctx.SaveChangesAsync();

            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id, StudentId = student.Id, StartAt = DateTime.UtcNow.AddDays(-1)
            });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetLaggingStudentsAsync(TeacherId);

        result.Count.ShouldBe(1);
        result[0].StudentName.ShouldBe("Öğrenci #S-42");
    }

    [Fact]
    public async Task GetLaggingStudentsAsync_AuthApiSucceeds_UsesResolvedFullName()
    {
        await using (var ctx = _db.NewContext())
        {
            var gradeId = await SeedGradeAsync(ctx);
            ctx.SetCurrentUser(TeacherId);
            var ws = new Worksheet { Name = "WS", Description = "", GradeId = gradeId };
            var student = new Student { UserId = 900, StudentNumber = "S-99", SchoolName = "s" };
            ctx.AddRange(ws, student);
            await ctx.SaveChangesAsync();

            ctx.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id, StudentId = student.Id, StartAt = DateTime.UtcNow.AddDays(-1)
            });
            await ctx.SaveChangesAsync();
        }

        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(new List<UserLookupResultDto>
            {
                new() { Id = 900, FullName = "Ayşe Yılmaz" }
            });

        await using var check = _db.NewContext();
        var result = await NewService(check).GetLaggingStudentsAsync(TeacherId);

        result.Count.ShouldBe(1);
        result[0].StudentName.ShouldBe("Ayşe Yılmaz");
    }

    public void Dispose() => _db.Dispose();
}
