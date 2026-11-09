using ExamApp.Api.Data;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>Kayıtta okul seçmeyen öğrenci sonradan seçer; seçim onay bekler, okulu olan öğrenciyi kilit durdurur.</summary>
public class StudentRequestSchoolTests : IDisposable
{
    private const int UserId = 10;
    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private StudentService NewService(AppDbContext ctx) =>
        new(ctx, Substitute.For<IAuthApiClient>(), new SchoolAccessPolicy(ctx));

    private async Task<(int First, int Second)> SeedAsync(bool assignFirst = false)
    {
        await using var ctx = _db.NewContext();
        var first = new School { Name = "Okul A" };
        var second = new School { Name = "Okul B" };
        var grade = new Grade { Name = "8" };
        ctx.AddRange(first, second, grade);
        await ctx.SaveChangesAsync();

        var student = new Student { UserId = UserId, StudentNumber = "1", GradeId = grade.Id };
        if (assignFirst)
        {
            student.SchoolId = first.Id;
            student.SchoolVerifiedAt = DateTime.UtcNow;
        }
        ctx.Students.Add(student);
        await ctx.SaveChangesAsync();
        return (first.Id, second.Id);
    }

    private async Task<Student> ReadStudentAsync()
    {
        await using var ctx = _db.NewContext();
        return await ctx.Students.AsNoTracking().SingleAsync(s => s.UserId == UserId);
    }

    [Fact]
    public async Task Student_without_school_gets_a_pending_membership()
    {
        var (first, _) = await SeedAsync();
        await using var ctx = _db.NewContext();

        var response = await NewService(ctx).RequestSchool(UserId, first);

        response.Success.ShouldBeTrue();
        var student = await ReadStudentAsync();
        student.SchoolId.ShouldBe(first);
        student.SchoolVerifiedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Pending_student_cannot_pick_another_school()
    {
        var (first, second) = await SeedAsync();
        await using (var ctx = _db.NewContext())
            await NewService(ctx).RequestSchool(UserId, first);

        await using var again = _db.NewContext();
        var response = await NewService(again).RequestSchool(UserId, second);

        response.Success.ShouldBeFalse();
        response.Conflict.ShouldBeTrue();
        (await ReadStudentAsync()).SchoolId.ShouldBe(first);
    }

    [Fact]
    public async Task Verified_student_stays_locked_and_keeps_verification()
    {
        var (first, second) = await SeedAsync(assignFirst: true);
        await using var ctx = _db.NewContext();

        var response = await NewService(ctx).RequestSchool(UserId, second);

        response.Conflict.ShouldBeTrue();
        var student = await ReadStudentAsync();
        student.SchoolId.ShouldBe(first);
        student.SchoolVerifiedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Same_school_is_idempotent_and_keeps_verification()
    {
        var (first, _) = await SeedAsync(assignFirst: true);
        await using var ctx = _db.NewContext();

        (await NewService(ctx).RequestSchool(UserId, first)).Success.ShouldBeTrue();

        (await ReadStudentAsync()).SchoolVerifiedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Unknown_school_is_rejected()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();

        var response = await NewService(ctx).RequestSchool(UserId, 999_999);

        response.Success.ShouldBeFalse();
        response.Conflict.ShouldBeFalse();
        (await ReadStudentAsync()).SchoolId.ShouldBeNull();
    }

    [Fact]
    public async Task Recently_rejected_school_waits_but_another_school_is_free()
    {
        var ids = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            var student = await ctx.Students.SingleAsync(s => s.UserId == UserId);
            student.LastRejectedSchoolId = ids.First;
            student.SchoolRejectedAt = DateTime.UtcNow.AddDays(-1);
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).RequestSchool(UserId, ids.First)).Success.ShouldBeFalse();
        (await ReadStudentAsync()).SchoolId.ShouldBeNull();

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).RequestSchool(UserId, ids.Second)).Success.ShouldBeTrue();
        (await ReadStudentAsync()).SchoolId.ShouldBe(ids.Second);
    }
}
