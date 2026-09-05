using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.Schools;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

public class SchoolServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    private SchoolService NewService(AppDbContext ctx) => new(ctx);

    private async Task<int> SeedSchoolAsync(string name = "Ankara Lisesi", string? city = "Ankara")
    {
        await using var ctx = _db.NewContext();
        var school = new School { Name = name, City = city };
        ctx.Schools.Add(school);
        await ctx.SaveChangesAsync();
        return school.Id;
    }

    // ---- Create ----

    [Fact]
    public async Task CreateAsync_persists_a_trimmed_school()
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateAsync(
            new UpsertSchoolDto { Name = "  Ankara Lisesi  ", City = " Ankara " }, userId: 1);

        result.Success.ShouldBeTrue();
        var saved = await _db.NewContext().Schools.FindAsync(result.ObjectId);
        saved!.Name.ShouldBe("Ankara Lisesi");
        saved.City.ShouldBe("Ankara");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateAsync_rejects_a_blank_name(string name)
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).CreateAsync(new UpsertSchoolDto { Name = name }, userId: 1);

        result.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task CreateAsync_rejects_a_duplicate_name_case_insensitively()
    {
        await SeedSchoolAsync("Ankara Lisesi");
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).CreateAsync(new UpsertSchoolDto { Name = "ankara lisesi" }, userId: 1);

        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("zaten var");
        (await _db.NewContext().Schools.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task CreateAsync_allows_distinct_names()
    {
        await SeedSchoolAsync("Ankara Lisesi");
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).CreateAsync(new UpsertSchoolDto { Name = "İzmir Lisesi" }, userId: 1);

        result.Success.ShouldBeTrue();
        (await _db.NewContext().Schools.CountAsync()).ShouldBe(2);
    }

    // ---- Update ----

    [Fact]
    public async Task UpdateAsync_renames_an_existing_school()
    {
        var id = await SeedSchoolAsync("Ankara Lisesi");
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).UpdateAsync(id, new UpsertSchoolDto { Name = "Ankara Fen Lisesi" }, userId: 1);

        result.Success.ShouldBeTrue();
        (await _db.NewContext().Schools.FindAsync(id))!.Name.ShouldBe("Ankara Fen Lisesi");
    }

    [Fact]
    public async Task UpdateAsync_rejects_a_missing_school()
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).UpdateAsync(9999, new UpsertSchoolDto { Name = "X" }, userId: 1);

        result.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task UpdateAsync_rejects_renaming_to_another_schools_name_case_insensitively()
    {
        var idA = await SeedSchoolAsync("Ankara Lisesi");
        await using (var ctx = _db.NewContext())
        {
            ctx.Schools.Add(new School { Name = "Konya Lisesi" });
            await ctx.SaveChangesAsync();
        }

        await using var upd = _db.NewContext();
        var result = await NewService(upd).UpdateAsync(idA, new UpsertSchoolDto { Name = "konya lisesi" }, userId: 1);

        result.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task UpdateAsync_allows_keeping_its_own_name_unchanged()
    {
        var id = await SeedSchoolAsync("Ankara Lisesi", city: "Ankara");
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).UpdateAsync(id, new UpsertSchoolDto { Name = "Ankara Lisesi", City = "Yeni Şehir" }, userId: 1);

        result.Success.ShouldBeTrue();
        (await _db.NewContext().Schools.FindAsync(id))!.City.ShouldBe("Yeni Şehir");
    }

    // ---- Delete ----

    [Fact]
    public async Task DeleteAsync_rejects_a_missing_school()
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).DeleteAsync(9999, userId: 1);

        result.Success.ShouldBeFalse();
    }

    [Fact]
    public async Task DeleteAsync_succeeds_when_no_teacher_or_student_references_it()
    {
        var id = await SeedSchoolAsync("Ankara Lisesi");
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).DeleteAsync(id, userId: 1);

        result.Success.ShouldBeTrue();
        (await _db.NewContext().Schools.FindAsync(id)).ShouldBeNull(); // filtered by soft-delete query filter
    }

    [Fact]
    public async Task DeleteAsync_is_blocked_when_a_teacher_schoolname_matches_case_insensitively()
    {
        var id = await SeedSchoolAsync("Ankara Lisesi");
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Add(new Teacher { UserId = 1, SchoolName = "ankara lisesi" });
            await ctx.SaveChangesAsync();
        }

        await using var del = _db.NewContext();
        var result = await NewService(del).DeleteAsync(id, userId: 1);

        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("öğretmen");
        (await _db.NewContext().Schools.FindAsync(id)).ShouldNotBeNull(); // still there
    }

    [Fact]
    public async Task DeleteAsync_is_blocked_when_a_student_schoolname_matches_case_insensitively()
    {
        var id = await SeedSchoolAsync("Ankara Lisesi");
        await using (var ctx = _db.NewContext())
        {
            ctx.Students.Add(new Student { UserId = 1, StudentNumber = "123", SchoolName = "ANKARA LISESI" });
            await ctx.SaveChangesAsync();
        }

        await using var del = _db.NewContext();
        var result = await NewService(del).DeleteAsync(id, userId: 1);

        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("öğrenci");
    }

    [Fact]
    public async Task DeleteAsync_succeeds_when_teacher_schoolname_does_not_match()
    {
        var id = await SeedSchoolAsync("Ankara Lisesi");
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Add(new Teacher { UserId = 1, SchoolName = "İzmir Lisesi" });
            await ctx.SaveChangesAsync();
        }

        await using var del = _db.NewContext();
        var result = await NewService(del).DeleteAsync(id, userId: 1);

        result.Success.ShouldBeTrue();
    }

    // ---- Read ----

    [Fact]
    public async Task GetAllAsync_returns_schools_ordered_by_name()
    {
        await SeedSchoolAsync("Zeytinburnu Lisesi");
        await using (var ctx = _db.NewContext())
        {
            ctx.Schools.Add(new School { Name = "Ankara Lisesi" });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var result = await NewService(read).GetAllAsync();

        result.Select(s => s.Name).ShouldBe(new[] { "Ankara Lisesi", "Zeytinburnu Lisesi" });
    }

    public void Dispose() => _db.Dispose();
}
