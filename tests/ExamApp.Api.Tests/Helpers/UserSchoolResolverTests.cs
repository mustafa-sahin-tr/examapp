using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Helpers;

/// <summary>
/// issue #326 (D3): kullanıcı okulunun tek kaynağı — canlı öğretmen satırı önce (okulsuzsa öğrenci satırına düşülmez), yoksa
/// canlı öğrenci satırı; silinmiş satırlar sayılmaz; #259 unique index canlı satırı tekil kılar; index'siz ortamda çoklu
/// canlı satır → null (tahmin yok).
/// </summary>
public class UserSchoolResolverTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private async Task<(int A, int B, int GradeId)> SeedSchoolsAsync()
    {
        await using var ctx = _db.NewContext();
        var a = new School { Name = "A" };
        var b = new School { Name = "B" };
        var grade = new Grade { Name = "5" };
        ctx.AddRange(a, b, grade);
        await ctx.SaveChangesAsync();
        return (a.Id, b.Id, grade.Id);
    }

    [Fact]
    public async Task Teacher_row_wins_and_a_schoolless_teacher_does_not_fall_back_to_the_student_row()
    {
        var (a, b, grade) = await SeedSchoolsAsync();
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.AddRange(new Teacher { UserId = 1, SchoolId = a }, new Teacher { UserId = 2, SchoolId = null });
            ctx.Students.AddRange(
                new Student { UserId = 1, StudentNumber = "1", GradeId = grade, SchoolId = b, SchoolVerifiedAt = DateTime.UtcNow },
                new Student { UserId = 2, StudentNumber = "2", GradeId = grade, SchoolId = b, SchoolVerifiedAt = DateTime.UtcNow },
                new Student { UserId = 3, StudentNumber = "3", GradeId = grade, SchoolId = b, SchoolVerifiedAt = DateTime.UtcNow });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var map = await UserSchoolResolver.ResolveManyAsync(read, new[] { 1, 2, 3, 4, 0 });

        map[1].ShouldBe(a, "öğretmen satırı esas (#234)");
        map[2].ShouldBeNull("bağımsız öğretmen okulsuz; öğrenci satırındaki okul kullanılmaz");
        map[3].ShouldBe(b, "öğretmen satırı yok → öğrenci satırı");
        map[4].ShouldBeNull("kaydı yok");
        map.ContainsKey(0).ShouldBeFalse("legacy 0 id sorgulanmaz");
        (await UserSchoolResolver.ResolveAsync(read, 3)).ShouldBe(b);
    }

    [Fact]
    public async Task Soft_deleted_rows_are_ignored_and_the_single_live_row_decides()
    {
        var (a, b, grade) = await SeedSchoolsAsync();
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.AddRange(
                new Teacher { UserId = 10, SchoolId = a, IsDeleted = true },
                new Teacher { UserId = 10, SchoolId = b });
            // Yalnız silinmiş öğretmen satırı → öğrenci satırına düşülür.
            ctx.Teachers.Add(new Teacher { UserId = 11, SchoolId = a, IsDeleted = true });
            ctx.Students.Add(new Student { UserId = 11, StudentNumber = "11", GradeId = grade, SchoolId = b, SchoolVerifiedAt = DateTime.UtcNow });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        (await UserSchoolResolver.ResolveAsync(read, 10)).ShouldBe(b);
        (await UserSchoolResolver.ResolveAsync(read, 11)).ShouldBe(b);
    }

    [Fact]
    public async Task Unique_live_index_prevents_a_second_live_teacher_row()
    {
        var (a, b, _) = await SeedSchoolsAsync();
        await using var ctx = _db.NewContext();
        ctx.Teachers.AddRange(new Teacher { UserId = 20, SchoolId = a }, new Teacher { UserId = 20, SchoolId = b });

        await Should.ThrowAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [Fact]
    public async Task Without_the_unique_index_two_live_rows_are_ambiguous_and_resolve_to_no_school()
    {
        var (a, b, grade) = await SeedSchoolsAsync();
        await using (var ctx = _db.NewContext())
        {
            await ctx.Database.ExecuteSqlRawAsync("DROP INDEX \"IX_Teachers_UserId\"");
            await ctx.Database.ExecuteSqlRawAsync("DROP INDEX \"IX_Students_UserId\"");
            ctx.Teachers.AddRange(new Teacher { UserId = 30, SchoolId = a }, new Teacher { UserId = 30, SchoolId = b });
            ctx.Students.AddRange(
                new Student { UserId = 31, StudentNumber = "a", GradeId = grade, SchoolId = a, SchoolVerifiedAt = DateTime.UtcNow },
                new Student { UserId = 31, StudentNumber = "b", GradeId = grade, SchoolId = b, SchoolVerifiedAt = DateTime.UtcNow });
            await ctx.SaveChangesAsync();
        }

        await using var read = _db.NewContext();
        var map = await UserSchoolResolver.ResolveManyAsync(read, new[] { 30, 31 });
        map[30].ShouldBeNull("hangisinin doğru olduğu bilinemez → okulsuz (güvenli taraf)");
        map[31].ShouldBeNull();

        // issue #334: ayrıntılı varyant belirsizliği okulsuzluktan ayırır (aynı sorgu, ikinci okul okuma kodu yok).
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Add(new Teacher { UserId = 32, SchoolId = null });
            await ctx.SaveChangesAsync();
        }
        var detailed = await UserSchoolResolver.ResolveManyDetailedAsync(read, new int?[] { 30, 31, 32, 33 });
        detailed[30].ShouldBe(new UserSchool(null, true));
        detailed[31].ShouldBe(new UserSchool(null, true));
        detailed[32].ShouldBe(new UserSchool(null, false), "tek satır, okulsuz (bağımsız)");
        detailed[33].ShouldBe(new UserSchool(null, false), "kaydı yok");
    }

    [Fact]
    public void Same_school_requires_two_equal_non_null_schools()
    {
        UserSchoolResolver.SameSchool(1, 1).ShouldBeTrue();
        UserSchoolResolver.SameSchool(1, 2).ShouldBeFalse();
        UserSchoolResolver.SameSchool(null, 1).ShouldBeFalse();
        UserSchoolResolver.SameSchool(1, null).ShouldBeFalse();
        UserSchoolResolver.SameSchool(null, null).ShouldBeFalse("null == null aynı okul sayılmaz");
    }
}
