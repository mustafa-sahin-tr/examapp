using ExamApp.Api.Data;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #420: veli uçlarının tek yetki kapısı (<see cref="ParentChildAccess"/>) — yalnızca Active + koparılmamış bağlantı ve
/// silinmemiş iki taraf erişim verir; Pending, Revoked, başka velinin çocuğu, olmayan öğrenci, silinmiş veli/öğrenci, veli
/// kaydı olmayan kullanıcı → null (uçta 404). Özet servisi kapıdan geçmeyen istekte audit yazmaz, veri döndürmez.
/// </summary>
public class ParentChildAccessTests : IDisposable
{
    private const int ParentUser = 42001;
    private const int OtherParentUser = 42002;
    private const int StudentUser = 42101;
    private const int OtherStudentUser = 42102;

    private readonly TestDb _db = TestDb.Create();
    private readonly FixedTimeProvider _time = new(new DateTimeOffset(2026, 10, 7, 9, 0, 0, TimeSpan.Zero));

    public void Dispose() => _db.Dispose();

    private sealed record Seed(int ParentId, int OtherParentId, int StudentId, int OtherStudentId, int SchoolId, int GradeId);

    private async Task<Seed> SeedAsync(bool verifiedSchool = true)
    {
        await using var ctx = _db.NewContext();
        var school = new School { Name = "Atatürk Ortaokulu" };
        var grade = new Grade { Name = "7. Sınıf" };
        ctx.AddRange(school, grade);
        await ctx.SaveChangesAsync();

        var student = new Student
        {
            UserId = StudentUser, StudentNumber = "s1", SchoolId = school.Id, GradeId = grade.Id,
            SchoolVerifiedAt = verifiedSchool ? DateTime.UtcNow : null
        };
        var other = new Student { UserId = OtherStudentUser, StudentNumber = "s2", GradeId = grade.Id };
        var parent = new Parent { UserId = ParentUser };
        var otherParent = new Parent { UserId = OtherParentUser };
        ctx.AddRange(student, other, parent, otherParent);
        await ctx.SaveChangesAsync();
        return new Seed(parent.Id, otherParent.Id, student.Id, other.Id, school.Id, grade.Id);
    }

    private async Task<int> LinkAsync(int parentId, int studentId, ParentStudentLinkStatus status, bool revokedAtSet = false)
    {
        await using var ctx = _db.NewContext();
        var link = new ParentStudentLink
        {
            Origin = ParentStudentLinkOrigin.ParentCreated,
            ParentId = parentId,
            StudentId = studentId,
            Status = status,
            CreatedAt = _time.GetUtcNow().UtcDateTime.AddDays(-1),
            ActivatedAt = status == ParentStudentLinkStatus.Active ? _time.GetUtcNow().UtcDateTime.AddHours(-20) : null,
            RevokedAt = status == ParentStudentLinkStatus.Revoked || revokedAtSet ? _time.GetUtcNow().UtcDateTime.AddHours(-1) : null
        };
        ctx.ParentStudentLinks.Add(link);
        await ctx.SaveChangesAsync();
        return link.Id;
    }

    private async Task<ParentChildAccessGrant?> EnsureAsync(int parentUserId, int studentId)
    {
        await using var ctx = _db.NewContext();
        return await new ParentChildAccess(ctx).EnsureActiveChildAsync(parentUserId, studentId);
    }

    private async Task SoftDeleteAsync<T>(int id) where T : BaseEntity
    {
        await using var ctx = _db.NewContext();
        var entity = await ctx.Set<T>().IgnoreQueryFilters().SingleAsync(e => EF.Property<int>(e, "Id") == id);
        entity.IsDeleted = true;
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Active_link_grants_access_with_student_scope()
    {
        var seed = await SeedAsync();
        var linkId = await LinkAsync(seed.ParentId, seed.StudentId, ParentStudentLinkStatus.Active);

        var grant = (await EnsureAsync(ParentUser, seed.StudentId)).ShouldNotBeNull();

        grant.LinkId.ShouldBe(linkId);
        grant.ParentId.ShouldBe(seed.ParentId);
        grant.StudentId.ShouldBe(seed.StudentId);
        grant.StudentUserId.ShouldBe(StudentUser);
        grant.GradeId.ShouldBe(seed.GradeId);
        grant.VerifiedSchoolId.ShouldBe(seed.SchoolId);
    }

    [Fact]
    public async Task Unverified_school_is_not_exposed_as_scope()
    {
        var seed = await SeedAsync(verifiedSchool: false);
        await LinkAsync(seed.ParentId, seed.StudentId, ParentStudentLinkStatus.Active);

        (await EnsureAsync(ParentUser, seed.StudentId)).ShouldNotBeNull().VerifiedSchoolId.ShouldBeNull();
    }

    [Theory]
    [InlineData(ParentStudentLinkStatus.Pending)]
    [InlineData(ParentStudentLinkStatus.Revoked)]
    public async Task Non_active_link_is_denied(ParentStudentLinkStatus status)
    {
        var seed = await SeedAsync();
        await LinkAsync(seed.ParentId, seed.StudentId, status);

        (await EnsureAsync(ParentUser, seed.StudentId)).ShouldBeNull();
    }

    [Fact]
    public async Task Active_status_with_revoked_timestamp_is_denied()
    {
        var seed = await SeedAsync();
        await LinkAsync(seed.ParentId, seed.StudentId, ParentStudentLinkStatus.Active, revokedAtSet: true);

        (await EnsureAsync(ParentUser, seed.StudentId)).ShouldBeNull();
    }

    [Fact]
    public async Task Revoked_then_relinked_pair_uses_the_new_active_link()
    {
        var seed = await SeedAsync();
        await LinkAsync(seed.ParentId, seed.StudentId, ParentStudentLinkStatus.Revoked);
        var active = await LinkAsync(seed.ParentId, seed.StudentId, ParentStudentLinkStatus.Active);

        (await EnsureAsync(ParentUser, seed.StudentId)).ShouldNotBeNull().LinkId.ShouldBe(active);
    }

    [Fact]
    public async Task Another_parents_child_is_denied()
    {
        var seed = await SeedAsync();
        await LinkAsync(seed.OtherParentId, seed.StudentId, ParentStudentLinkStatus.Active);
        await LinkAsync(seed.ParentId, seed.OtherStudentId, ParentStudentLinkStatus.Active);

        (await EnsureAsync(ParentUser, seed.StudentId)).ShouldBeNull();
        (await EnsureAsync(OtherParentUser, seed.OtherStudentId)).ShouldBeNull();
    }

    [Fact]
    public async Task Unknown_student_and_user_without_parent_record_are_denied()
    {
        var seed = await SeedAsync();
        await LinkAsync(seed.ParentId, seed.StudentId, ParentStudentLinkStatus.Active);

        (await EnsureAsync(ParentUser, 999_999)).ShouldBeNull();
        (await EnsureAsync(StudentUser, seed.StudentId)).ShouldBeNull(); // öğrencinin kendisi veli değil
        (await EnsureAsync(0, seed.StudentId)).ShouldBeNull();
        (await EnsureAsync(ParentUser, 0)).ShouldBeNull();
    }

    [Fact]
    public async Task Soft_deleted_student_is_denied()
    {
        var seed = await SeedAsync();
        await LinkAsync(seed.ParentId, seed.StudentId, ParentStudentLinkStatus.Active);
        await SoftDeleteAsync<Student>(seed.StudentId);

        (await EnsureAsync(ParentUser, seed.StudentId)).ShouldBeNull();
    }

    [Fact]
    public async Task Soft_deleted_parent_is_denied()
    {
        var seed = await SeedAsync();
        await LinkAsync(seed.ParentId, seed.StudentId, ParentStudentLinkStatus.Active);
        await SoftDeleteAsync<Parent>(seed.ParentId);

        (await EnsureAsync(ParentUser, seed.StudentId)).ShouldBeNull();
    }

    [Fact]
    public async Task Summary_writes_audit_only_when_access_is_granted()
    {
        var seed = await SeedAsync();
        await LinkAsync(seed.ParentId, seed.StudentId, ParentStudentLinkStatus.Active);
        await LinkAsync(seed.ParentId, seed.OtherStudentId, ParentStudentLinkStatus.Pending);

        await using (var ctx = _db.NewContext())
        {
            var service = NewSummaryService(ctx);
            (await service.GetChildSummaryAsync(ParentUser, seed.OtherStudentId)).ShouldBeNull();
            (await service.GetChildSummaryAsync(OtherParentUser, seed.StudentId)).ShouldBeNull();
            (await service.GetChildSummaryAsync(ParentUser, seed.StudentId)).ShouldNotBeNull().StudentId.ShouldBe(seed.StudentId);
        }

        await using var read = _db.NewContext();
        var audit = (await read.ParentAccessAudits.AsNoTracking().ToListAsync()).ShouldHaveSingleItem();
        audit.ParentId.ShouldBe(seed.ParentId);
        audit.StudentId.ShouldBe(seed.StudentId);
        audit.Endpoint.ShouldBe(ParentAccessEndpoints.ChildSummary);
        audit.At.ShouldBe(_time.GetUtcNow().UtcDateTime);
    }

    [Fact]
    public async Task Audit_is_deduplicated_per_ten_minute_bucket()
    {
        var seed = await SeedAsync();
        await LinkAsync(seed.ParentId, seed.StudentId, ParentStudentLinkStatus.Active);
        await LinkAsync(seed.ParentId, seed.OtherStudentId, ParentStudentLinkStatus.Active);
        _time.Now = new DateTimeOffset(2026, 10, 7, 9, 1, 0, TimeSpan.Zero); // kova 09:00-09:10

        async Task<bool> RecordAsync(int studentId, string endpoint = ParentAccessEndpoints.ChildSummary)
        {
            await using var ctx = _db.NewContext();
            return await new ParentAccessAuditLog(ctx, _time).RecordAsync(seed.ParentId, studentId, endpoint);
        }

        (await RecordAsync(seed.StudentId)).ShouldBeTrue();
        _time.Now = _time.Now.AddMinutes(8);                                   // 09:09 aynı kova
        (await RecordAsync(seed.StudentId)).ShouldBeFalse();
        (await RecordAsync(seed.OtherStudentId)).ShouldBeTrue();               // başka çocuk ayrı satır
        (await RecordAsync(seed.StudentId, "children.other")).ShouldBeTrue(); // başka uç ayrı satır
        _time.Now = _time.Now.AddMinutes(1);                                   // 09:10 yeni kova
        (await RecordAsync(seed.StudentId)).ShouldBeTrue();

        // Özet ucu da aynı kuralla: aynı kovada tekrar açmak yeni satır yazmaz.
        await using (var ctx = _db.NewContext())
        {
            var service = NewSummaryService(ctx);
            (await service.GetChildSummaryAsync(ParentUser, seed.StudentId)).ShouldNotBeNull();
            (await service.GetChildSummaryAsync(ParentUser, seed.StudentId)).ShouldNotBeNull();
        }

        await using var read = _db.NewContext();
        (await read.ParentAccessAudits.CountAsync(a => a.StudentId == seed.StudentId && a.Endpoint == ParentAccessEndpoints.ChildSummary))
            .ShouldBe(2);
        (await read.ParentAccessAudits.CountAsync()).ShouldBe(4);
    }

    [Theory]
    [InlineData(9, 0, 0, 9, 0)]
    [InlineData(9, 9, 59, 9, 0)]
    [InlineData(9, 10, 0, 9, 10)]
    [InlineData(23, 59, 59, 23, 50)]
    public void Audit_bucket_start_is_ten_minute_floor(int h, int m, int s, int eh, int em)
        => ParentAccessAuditLog.BucketStart(new DateTime(2026, 10, 7, h, m, s, DateTimeKind.Utc))
            .ShouldBe(new DateTime(2026, 10, 7, eh, em, 0, DateTimeKind.Utc));

    private ParentDashboardService NewSummaryService(AppDbContext ctx)
        => new(ctx, new ParentChildAccess(ctx), new ParentAccessAuditLog(ctx, _time),
            new LocalDayCalendar(LocalDayCalendar.DefaultTimeZoneId, _time), _time);
}
