using ExamApp.Api.Data;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #190: merkezi okul izolasyonu kararı (SchoolAccessPolicy).
/// Matris: aynı okul (izin), farklı okul (red), okulsuz→okullu (red), okullu→okulsuz (red),
/// okulsuz→okulsuz (izin), admin/servis (izin, tüm okullar).
/// ApplyScope: filtre SQL düzeyinde — sayfalama (Take) farklı okul kayıtlarıyla dolmaz.
/// </summary>
public class SchoolAccessPolicyTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly SchoolAccessPolicy _policy = new();

    public void Dispose() => _db.Dispose();

    // ---- CanAccess ----

    [Fact]
    public void CanAccess_SameSchool_Allowed()
        => _policy.CanAccess(SchoolScope.For(userId: 1, schoolId: 10), targetSchoolId: 10).ShouldBeTrue();

    [Fact]
    public void CanAccess_DifferentSchool_Denied()
        => _policy.CanAccess(SchoolScope.For(userId: 1, schoolId: 10), targetSchoolId: 20).ShouldBeFalse();

    [Fact]
    public void CanAccess_IndependentRequester_SchoolBoundTarget_Denied()
        => _policy.CanAccess(SchoolScope.For(userId: 1, schoolId: null), targetSchoolId: 10).ShouldBeFalse();

    [Fact]
    public void CanAccess_SchoolBoundRequester_IndependentTarget_Denied()
        => _policy.CanAccess(SchoolScope.For(userId: 1, schoolId: 10), targetSchoolId: null).ShouldBeFalse();

    [Fact]
    public void CanAccess_IndependentRequester_IndependentTarget_Allowed()
        => _policy.CanAccess(SchoolScope.For(userId: 1, schoolId: null), targetSchoolId: null).ShouldBeTrue();

    [Theory]
    [InlineData(10)]
    [InlineData(20)]
    [InlineData(null)]
    public void CanAccess_Unrestricted_AlwaysAllowed(int? targetSchoolId)
        => _policy.CanAccess(SchoolScope.Unrestricted(userId: 1), targetSchoolId).ShouldBeTrue();

    // ---- ApplyScope ----

    private async Task<(int SchoolA, int SchoolB)> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var a = new School { Name = "Okul A" };
        var b = new School { Name = "Okul B" };
        ctx.Schools.AddRange(a, b);
        await ctx.SaveChangesAsync();

        ctx.Students.AddRange(
            new Student { UserId = 1, StudentNumber = "A1", SchoolId = a.Id },
            new Student { UserId = 2, StudentNumber = "A2", SchoolId = a.Id },
            new Student { UserId = 3, StudentNumber = "B1", SchoolId = b.Id },
            new Student { UserId = 4, StudentNumber = "N1", SchoolId = null });

        ctx.Teachers.AddRange(
            new Teacher { UserId = 11, SchoolId = a.Id },
            new Teacher { UserId = 12, SchoolId = b.Id },
            new Teacher { UserId = 13, SchoolId = null, IsIndependentTutor = true });

        await ctx.SaveChangesAsync();
        return (a.Id, b.Id);
    }

    [Fact]
    public async Task ApplyScope_SameSchool_ReturnsOnlyThatSchool()
    {
        var (a, _) = await SeedAsync();
        await using var ctx = _db.NewContext();

        var students = await _policy.ApplyScope(ctx.Students.AsNoTracking(), SchoolScope.For(99, a))
            .Select(s => s.StudentNumber).ToListAsync();

        students.ShouldBe(new[] { "A1", "A2" }, ignoreOrder: true);
    }

    [Fact]
    public async Task ApplyScope_DifferentSchoolRecords_NeverReturned()
    {
        var (_, b) = await SeedAsync();
        await using var ctx = _db.NewContext();

        var students = await _policy.ApplyScope(ctx.Students.AsNoTracking(), SchoolScope.For(99, b))
            .Select(s => s.StudentNumber).ToListAsync();

        students.ShouldBe(new[] { "B1" });
        students.ShouldNotContain("A1");
        students.ShouldNotContain("N1");
    }

    [Fact]
    public async Task ApplyScope_IndependentRequester_SeesOnlyIndependentRecords()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();

        // Okulsuz → okullu kayıtlar görünmez; null == null eşleşmesi SQL'de "IS NULL" olarak çalışır.
        var students = await _policy.ApplyScope(ctx.Students.AsNoTracking(), SchoolScope.For(99, null))
            .Select(s => s.StudentNumber).ToListAsync();

        students.ShouldBe(new[] { "N1" });
    }

    [Fact]
    public async Task ApplyScope_Unrestricted_ReturnsAllSchools()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();

        var students = await _policy.ApplyScope(ctx.Students.AsNoTracking(), SchoolScope.Unrestricted(1))
            .CountAsync();

        students.ShouldBe(4);
    }

    [Fact]
    public async Task ApplyScope_WorksForTeachersToo()
    {
        var (a, _) = await SeedAsync();
        await using var ctx = _db.NewContext();

        var teachers = await _policy.ApplyScope(ctx.Teachers.AsNoTracking(), SchoolScope.For(99, a))
            .Select(t => t.UserId).ToListAsync();

        teachers.ShouldBe(new[] { 11 });
    }

    [Fact]
    public async Task ApplyScope_SchoolBound_FilterIsInGeneratedSql()
    {
        var (a, _) = await SeedAsync();
        await using var ctx = _db.NewContext();

        var sql = _policy.ApplyScope(ctx.Students.AsNoTracking(), SchoolScope.For(99, a)).ToQueryString();

        sql.ShouldContain("SchoolId");
        sql.ShouldNotContain("IS NULL", customMessage: "okullu istek sahibi için IS NULL değil parametre eşitliği beklenir");
    }

    [Fact]
    public async Task ApplyScope_Independent_GeneratesIsNullPredicate()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();

        var sql = _policy.ApplyScope(ctx.Students.AsNoTracking(), SchoolScope.For(99, null)).ToQueryString();

        sql.ShouldContain("SchoolId");
        sql.ShouldContain("IS NULL");
    }

    [Fact]
    public async Task ApplyScope_Unrestricted_DoesNotAddSchoolPredicate()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();

        var sql = _policy.ApplyScope(ctx.Students.AsNoTracking(), SchoolScope.Unrestricted(1)).ToQueryString();

        // Global soft-delete filtresi dışında WHERE koşulu yok: "SchoolId" yalnızca SELECT listesinde geçer.
        var wherePart = sql[(sql.IndexOf("WHERE", StringComparison.OrdinalIgnoreCase) is var i && i >= 0 ? i : sql.Length)..];
        wherePart.ShouldNotContain("SchoolId");
    }

    [Fact]
    public async Task ApplyScope_IsQueryLevel_TakeIsNotFilledByOtherSchools()
    {
        // Sonradan süzme olsaydı Take(1) ilk sıradaki (başka okul) kaydı alıp süzer ve boş dönerdi.
        var (_, b) = await SeedAsync();
        await using var ctx = _db.NewContext();

        var page = await _policy.ApplyScope(ctx.Students.AsNoTracking(), SchoolScope.For(99, b))
            .OrderBy(s => s.StudentNumber)
            .Skip(0)
            .Take(1)
            .Select(s => s.StudentNumber)
            .ToListAsync();

        page.ShouldBe(new[] { "B1" });
    }
}
