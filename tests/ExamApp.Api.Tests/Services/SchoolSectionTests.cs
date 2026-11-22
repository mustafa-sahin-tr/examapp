using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #1279 (#1071 S4): şubeler okul kapsamında; ad okul + sınıf içinde büyük/küçük harf duyarsız tekil; öğrenci yalnız kendi
/// okulunun doğrulanmış üyesi ve şubenin sınıfındaysa atanır; tek aktif üyelik, taşıma / çıkarma / silme geçmişi kapatır.
/// </summary>
public class SchoolSectionTests : IDisposable
{
    private const int Actor = 1279;

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    public SchoolSectionTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<IEnumerable<int>>().Select(id => new UserLookupResultDto { Id = id, FullName = $"U{id}" }).ToList());
    }

    public void Dispose() => _db.Dispose();

    private sealed record Seed(int SchoolA, int SchoolB, int Grade7, int Grade8, int S7a, int S7b, int S8, int PendingA, int StudentB);

    private async Task<Seed> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var a = new School { Name = "A" };
        var b = new School { Name = "B" };
        var g7 = new Grade { Name = "7. Sınıf" };
        var g8 = new Grade { Name = "8. Sınıf" };
        ctx.AddRange(a, b, g7, g8);
        await ctx.SaveChangesAsync();
        var now = DateTime.UtcNow;
        Student S(int userId, int school, int grade, bool verified = true) => new()
        {
            UserId = userId, StudentNumber = $"2024{userId}", SchoolId = school, GradeId = grade, SchoolVerifiedAt = verified ? now : null
        };
        var s7a = S(101, a.Id, g7.Id);
        var s7b = S(102, a.Id, g7.Id);
        var s8 = S(103, a.Id, g8.Id);
        var pending = S(104, a.Id, g7.Id, verified: false);
        var other = S(105, b.Id, g7.Id);
        ctx.Students.AddRange(s7a, s7b, s8, pending, other);
        await ctx.SaveChangesAsync();
        return new Seed(a.Id, b.Id, g7.Id, g8.Id, s7a.Id, s7b.Id, s8.Id, pending.Id, other.Id);
    }

    private SchoolSectionService Service(AppDbContext ctx) => new(ctx, _authApi);

    private async Task<int> CreateAsync(int school, int grade, string name)
    {
        await using var ctx = _db.NewContext();
        var r = await Service(ctx).CreateAsync(school, Actor, grade, name);
        r.Status.ShouldBe(SchoolSectionStatus.Success);
        return r.Section!.Id;
    }

    [Fact]
    public async Task Names_are_unique_per_school_and_grade_case_insensitive_in_turkish()
    {
        var s = await SeedAsync();
        await CreateAsync(s.SchoolA, s.Grade7, "ı-1");
        await using var ctx = _db.NewContext();
        var service = Service(ctx);

        (await service.CreateAsync(s.SchoolA, Actor, s.Grade7, "  I-1 ")).Status.ShouldBe(SchoolSectionStatus.Duplicate);
        (await service.CreateAsync(s.SchoolA, Actor, s.Grade8, "I-1")).Status.ShouldBe(SchoolSectionStatus.Success);
        (await service.CreateAsync(s.SchoolB, Actor, s.Grade7, "I-1")).Status.ShouldBe(SchoolSectionStatus.Success);
        (await service.CreateAsync(s.SchoolA, Actor, s.Grade7, " ")).Status.ShouldBe(SchoolSectionStatus.Invalid);
        (await service.CreateAsync(s.SchoolA, Actor, s.Grade7, new string('x', 21))).Status.ShouldBe(SchoolSectionStatus.Invalid);
        (await service.CreateAsync(s.SchoolA, Actor, 9999, "B")).Status.ShouldBe(SchoolSectionStatus.Invalid);
    }

    [Fact]
    public async Task Other_school_section_is_not_found_for_every_operation()
    {
        var s = await SeedAsync();
        var sectionB = await CreateAsync(s.SchoolB, s.Grade7, "A");
        await using var ctx = _db.NewContext();
        var service = Service(ctx);

        (await service.RenameAsync(s.SchoolA, Actor, sectionB, "Z")).Status.ShouldBe(SchoolSectionStatus.NotFound);
        (await service.DeleteAsync(s.SchoolA, Actor, sectionB)).Status.ShouldBe(SchoolSectionStatus.NotFound);
        (await service.AssignAsync(s.SchoolA, Actor, sectionB, [s.S7a])).Status.ShouldBe(SchoolSectionStatus.NotFound);
        (await service.ListStudentsAsync(s.SchoolA, sectionB, 1, 20)).ShouldBeNull();
        (await service.ListAsync(s.SchoolA, null)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Assign_rejects_other_school_pending_and_wrong_grade_students_without_partial_writes()
    {
        var s = await SeedAsync();
        var section = await CreateAsync(s.SchoolA, s.Grade7, "A");
        await using var ctx = _db.NewContext();
        var service = Service(ctx);

        (await service.AssignAsync(s.SchoolA, Actor, section, [s.S7a, s.StudentB])).Status.ShouldBe(SchoolSectionStatus.NotFound);
        (await service.AssignAsync(s.SchoolA, Actor, section, [s.S7a, s.PendingA])).Status.ShouldBe(SchoolSectionStatus.NotFound);
        var mismatch = await service.AssignAsync(s.SchoolA, Actor, section, [s.S7a, s.S8]);
        mismatch.Status.ShouldBe(SchoolSectionStatus.GradeMismatch);
        mismatch.StudentIds.ShouldBe([s.S8]);
        (await service.AssignAsync(s.SchoolA, Actor, section, [])).Status.ShouldBe(SchoolSectionStatus.Invalid);

        (await ctx.SchoolSectionMemberships.AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Moving_closes_the_old_membership_and_counts_follow()
    {
        var s = await SeedAsync();
        var a = await CreateAsync(s.SchoolA, s.Grade7, "A");
        var b = await CreateAsync(s.SchoolA, s.Grade7, "B");
        await using (var ctx = _db.NewContext())
        {
            var first = await Service(ctx).AssignAsync(s.SchoolA, Actor, a, [s.S7a, s.S7b]);
            first.Assignment!.Assigned.ShouldBe(2);
        }
        await using (var ctx = _db.NewContext())
        {
            var again = await Service(ctx).AssignAsync(s.SchoolA, Actor, a, [s.S7a]);
            again.Assignment!.Assigned.ShouldBe(0);
            again.Assignment.Unchanged.ShouldBe(1);
            (await Service(ctx).AssignAsync(s.SchoolA, Actor, b, [s.S7a])).Status.ShouldBe(SchoolSectionStatus.Success);
        }

        await using var check = _db.NewContext();
        var rows = await check.SchoolSectionMemberships.AsNoTracking().Where(m => m.StudentId == s.S7a).OrderBy(m => m.Id).ToListAsync();
        rows.Count.ShouldBe(2);
        rows[0].SectionId.ShouldBe(a);
        rows[0].EndReason.ShouldBe(SchoolSectionEndReason.Moved);
        rows[0].EndedByUserId.ShouldBe(Actor);
        rows[1].SectionId.ShouldBe(b);
        rows[1].EndedAt.ShouldBeNull();

        var list = await Service(check).ListAsync(s.SchoolA, s.Grade7);
        list.Select(x => (x.Name, x.StudentCount)).ShouldBe([("A", 1), ("B", 1)]);
        var students = await Service(check).ListStudentsAsync(s.SchoolA, b, 1, 20);
        students!.Items.ShouldHaveSingleItem().FullName.ShouldBe("U101");
        students.Items[0].StudentNumber.ShouldStartWith("****");
    }

    [Fact]
    public async Task Remove_and_delete_end_memberships_and_keep_history()
    {
        var s = await SeedAsync();
        var a = await CreateAsync(s.SchoolA, s.Grade7, "A");
        await using (var ctx = _db.NewContext())
            await Service(ctx).AssignAsync(s.SchoolA, Actor, a, [s.S7a, s.S7b]);

        await using (var ctx = _db.NewContext())
        {
            var service = Service(ctx);
            (await service.RemoveStudentAsync(s.SchoolA, Actor, a, s.S7a)).Status.ShouldBe(SchoolSectionStatus.Success);
            (await service.RemoveStudentAsync(s.SchoolA, Actor, a, s.S7a)).Status.ShouldBe(SchoolSectionStatus.NotFound);
            (await service.RemoveStudentAsync(s.SchoolB, Actor, a, s.S7b)).Status.ShouldBe(SchoolSectionStatus.NotFound);
            (await service.DeleteAsync(s.SchoolA, Actor, a)).Status.ShouldBe(SchoolSectionStatus.Success);
        }

        await using var check = _db.NewContext();
        var rows = await check.SchoolSectionMemberships.IgnoreQueryFilters().AsNoTracking().OrderBy(m => m.StudentId).ToListAsync();
        rows.Select(r => r.EndReason).ShouldBe([SchoolSectionEndReason.Removed, SchoolSectionEndReason.SectionDeleted]);
        (await Service(check).ListAsync(s.SchoolA, null)).ShouldBeEmpty();
        // Silinen şubenin adı yeniden kullanılabilir.
        (await Service(check).CreateAsync(s.SchoolA, Actor, s.Grade7, "A")).Status.ShouldBe(SchoolSectionStatus.Success);
    }

    [Fact]
    public async Task Only_approved_school_teachers_resolve_a_school()
    {
        var s = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.AddRange(
                new Teacher { UserId = 1, SchoolId = s.SchoolA, AccountApprovedAt = DateTime.UtcNow },
                new Teacher { UserId = 2, SchoolId = s.SchoolA },
                new Teacher { UserId = 3, SchoolId = s.SchoolA, AccountApprovedAt = DateTime.UtcNow, AccountSuspendedAt = DateTime.UtcNow },
                new Teacher { UserId = 4, IsIndependentTutor = true, AccountApprovedAt = DateTime.UtcNow });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var service = Service(check);
        (await service.ResolveTeacherSchoolIdAsync(1)).ShouldBe(s.SchoolA);
        (await service.ResolveTeacherSchoolIdAsync(2)).ShouldBeNull();
        (await service.ResolveTeacherSchoolIdAsync(3)).ShouldBeNull();
        (await service.ResolveTeacherSchoolIdAsync(4)).ShouldBeNull();
        (await service.ResolveTeacherSchoolIdAsync(999)).ShouldBeNull();
    }
}
