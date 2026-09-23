using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #236 (AC3): Grade-targeted assignment with SchoolId=A → student from SchoolB cannot start test.
/// Boundary enforcement: same school student can start, different school student gets rejected.
/// </summary>
public class TestSessionServiceSchoolBoundaryTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private TestSessionService NewService(AppDbContext ctx) => new(ctx);

    public void Dispose() => _db.Dispose();

    private static readonly DateTime Start = new(2026, 3, 1, 8, 0, 0, DateTimeKind.Utc);

    private sealed record Seed(
        int GradeId, int SchoolA, int SchoolB,
        int WorksheetId,
        int StudentA, int StudentB);

    /// <summary>
    /// SchoolA: teacher, studentA (same school as assignment).
    /// SchoolB: studentB (different school, same grade).
    /// Grade assignment with SchoolId=SchoolA.
    /// </summary>
    private async Task<Seed> SeedAsync()
    {
        await using var ctx = _db.NewContext();

        var grade = new Grade { Name = "8" };
        var schoolA = new School { Name = "Okul A" };
        var schoolB = new School { Name = "Okul B" };
        ctx.AddRange(grade, schoolA, schoolB);
        await ctx.SaveChangesAsync();

        var teacher = new Teacher { UserId = 1, SchoolId = schoolA.Id };
        var studentA = new Student { UserId = 10, StudentNumber = "a", SchoolId = schoolA.Id, GradeId = grade.Id };
        var studentB = new Student { UserId = 11, StudentNumber = "b", SchoolId = schoolB.Id, GradeId = grade.Id };
        ctx.AddRange(teacher, studentA, studentB);
        await ctx.SaveChangesAsync();

        ctx.SetCurrentUser(1);
        var worksheet = new Worksheet { Name = "Test WS", Description = "", GradeId = grade.Id };
        ctx.Worksheets.Add(worksheet);
        await ctx.SaveChangesAsync();

        // Grade assignment scoped to SchoolA (same school as teacher)
        ctx.WorksheetAssignments.Add(new WorksheetAssignment
        {
            WorksheetId = worksheet.Id,
            GradeId = grade.Id,
            SchoolId = schoolA.Id, // KEY: assignment scoped to SchoolA
            StartAt = Start.AddDays(-1),
        });
        await ctx.SaveChangesAsync();

        return new Seed(grade.Id, schoolA.Id, schoolB.Id, worksheet.Id, studentA.Id, studentB.Id);
    }

    [Fact]
    public async Task StudentInSameSchoolAsAssignment_CanStartTest()
    {
        var seed = await SeedAsync();
        await using var ctx = _db.NewContext();

        var service = NewService(ctx);
        var studentProfile = new StudentProfileDto { Id = seed.StudentA, GradeId = seed.GradeId, SchoolId = seed.SchoolA };

        var result = await service.StartTestAsync(seed.WorksheetId, studentProfile);

        result.Success.ShouldBeTrue($"Expected success but got: {result.Message}");
    }

    [Fact]
    public async Task StudentInDifferentSchoolAsAssignment_WithRestrictedVisibility_CannotStartTest()
    {
        // AC3: School boundary enforcement. When worksheet is Restricted (no discovery),
        // student without an assignment from their school cannot start.
        var seed = await SeedAsync();
        await using var ctx = _db.NewContext();

        // Update worksheet to Restricted so students can only start if they have an active assignment
        var ws = await ctx.Worksheets.FindAsync(seed.WorksheetId);
        ws!.StudentVisibility = WorksheetStudentVisibility.Restricted;
        await ctx.SaveChangesAsync();

        var service = NewService(ctx);
        var studentProfile = new StudentProfileDto { Id = seed.StudentB, GradeId = seed.GradeId, SchoolId = seed.SchoolB };

        // StudentB has no assignment (the assignment is scoped to SchoolA).
        // With Restricted visibility, studentB cannot start.
        var ex = await Assert.ThrowsAsync<UnauthorizedAccessException>(
            () => service.StartTestAsync(seed.WorksheetId, studentProfile));

        ex.ShouldNotBeNull("Should throw UnauthorizedAccessException");
    }

    [Fact]
    public async Task StudentInDifferentSchoolAsAssignment_WithNormalVisibility_CanStartViaDiscovery()
    {
        // With Normal visibility (default), students can start via discovery even without assignment.
        // School boundary only applies to grade assignments, not discovery.
        // This verifies that discovery works across schools (by design).
        var seed = await SeedAsync();
        await using var ctx = _db.NewContext();

        // Worksheet has StudentVisibility=Normal (default), so studentB can start via discovery
        var service = NewService(ctx);
        var studentProfile = new StudentProfileDto { Id = seed.StudentB, GradeId = seed.GradeId, SchoolId = seed.SchoolB };

        // StudentB has no assignment, but can start via discovery (same grade + Normal visibility)
        var result = await service.StartTestAsync(seed.WorksheetId, studentProfile);

        result.Success.ShouldBeTrue("StudentB should start via discovery (same grade + Normal visibility)");
    }

    [Fact]
    public async Task AdminPlatformWideAssignment_BothSchoolsCanStart()
    {
        await using var setupCtx = _db.NewContext();
        var grade = new Grade { Name = "8" };
        var schoolA = new School { Name = "Okul A" };
        var schoolB = new School { Name = "Okul B" };
        setupCtx.AddRange(grade, schoolA, schoolB);
        await setupCtx.SaveChangesAsync();

        var studentA = new Student { UserId = 10, StudentNumber = "a", SchoolId = schoolA.Id, GradeId = grade.Id };
        var studentB = new Student { UserId = 11, StudentNumber = "b", SchoolId = schoolB.Id, GradeId = grade.Id };
        setupCtx.AddRange(studentA, studentB);
        await setupCtx.SaveChangesAsync();

        setupCtx.SetCurrentUser(99); // Admin (no teacher record)
        var worksheet = new Worksheet { Name = "Admin WS", Description = "", GradeId = grade.Id };
        setupCtx.Worksheets.Add(worksheet);
        await setupCtx.SaveChangesAsync();

        // Platform-wide assignment (SchoolId = null)
        setupCtx.WorksheetAssignments.Add(new WorksheetAssignment
        {
            WorksheetId = worksheet.Id,
            GradeId = grade.Id,
            SchoolId = null, // Platform-wide
            StartAt = Start.AddDays(-1),
        });
        await setupCtx.SaveChangesAsync();

        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        var studentAProfile = new StudentProfileDto { Id = studentA.Id, GradeId = grade.Id, SchoolId = schoolA.Id };
        var resultA = await service.StartTestAsync(worksheet.Id, studentAProfile);
        resultA.Success.ShouldBeTrue("SchoolA student should access admin platform-wide assignment");

        var studentBProfile = new StudentProfileDto { Id = studentB.Id, GradeId = grade.Id, SchoolId = schoolB.Id };
        var resultB = await service.StartTestAsync(worksheet.Id, studentBProfile);
        resultB.Success.ShouldBeTrue("SchoolB student should access admin platform-wide assignment");
    }
}
