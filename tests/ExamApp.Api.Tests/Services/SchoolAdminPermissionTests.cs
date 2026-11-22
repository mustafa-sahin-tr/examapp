using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Teachers;
using ExamApp.Api.Services;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #1276 (#1071 S1): platform admini öğretmene okul yöneticisi yetkisi verir / kaldırır; yetki DB'den çözülür ve
/// öğretmen okuldan ayrılınca (okul değişimi, bağımsızlığa geçiş, askı) düşer.
/// </summary>
public class SchoolAdminPermissionTests : IDisposable
{
    private const string ActorSub = "kc-admin-1276";
    private const int TeacherUserId = 1276;

    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    private sealed record Seed(int SchoolA, int SchoolB, int TeacherId);

    private async Task<Seed> SeedAsync(Action<Teacher>? configure = null)
    {
        await using var ctx = _db.NewContext();
        var a = new School { Name = "A" };
        var b = new School { Name = "B" };
        ctx.AddRange(a, b);
        await ctx.SaveChangesAsync();
        var t = new Teacher
        {
            UserId = TeacherUserId,
            SchoolId = a.Id,
            ApprovalStatus = TeacherApprovalStatus.Approved,
            AccountApprovedAt = DateTime.UtcNow.AddDays(-10)
        };
        configure?.Invoke(t);
        ctx.Teachers.Add(t);
        await ctx.SaveChangesAsync();
        return new Seed(a.Id, b.Id, t.Id);
    }

    private async Task<AdminTeacherSchoolAdminResult> SetAsync(int teacherId, bool value)
    {
        await using var ctx = _db.NewContext();
        return await new AdminTeacherSchoolAdminService(ctx, new AdminUserActionAuditService(ctx))
            .SetAsync(teacherId, value, ActorSub);
    }

    private async Task<Teacher> TeacherAsync(int id)
    {
        await using var ctx = _db.NewContext();
        return await ctx.Teachers.AsNoTracking().SingleAsync(t => t.Id == id);
    }

    private async Task<List<AdminUserActionLog>> AuditAsync()
    {
        await using var ctx = _db.NewContext();
        return await ctx.AdminUserActionLogs.AsNoTracking().OrderBy(a => a.Id).ToListAsync();
    }

    private async Task<int?> AdministeredSchoolAsync()
    {
        await using var ctx = _db.NewContext();
        return await new SchoolAdminResolver(ctx).GetAdministeredSchoolIdAsync(TeacherUserId);
    }

    [Fact]
    public async Task Grant_sets_flag_writes_audit_and_resolver_returns_the_school()
    {
        var seed = await SeedAsync();

        var result = await SetAsync(seed.TeacherId, true);

        result.Status.ShouldBe(AdminTeacherSchoolAdminStatus.Success);
        result.Changed.ShouldBeTrue();
        result.SchoolId.ShouldBe(seed.SchoolA);
        var teacher = await TeacherAsync(seed.TeacherId);
        teacher.IsSchoolAdmin.ShouldBeTrue();
        teacher.SchoolAdminGrantedAt.ShouldNotBeNull();
        var audit = (await AuditAsync()).ShouldHaveSingleItem();
        audit.Action.ShouldBe(AdminUserAction.SchoolAdminGranted);
        audit.Outcome.ShouldBe(AdminUserActionOutcome.Succeeded);
        audit.ToSchoolId.ShouldBe(seed.SchoolA);
        audit.ActorKeycloakId.ShouldBe(ActorSub);

        (await AdministeredSchoolAsync()).ShouldBe(seed.SchoolA);
        await using var ctx = _db.NewContext();
        var resolver = new SchoolAdminResolver(ctx);
        (await resolver.IsSchoolAdminAsync(TeacherUserId, seed.SchoolA)).ShouldBeTrue();
        (await resolver.IsSchoolAdminAsync(TeacherUserId, seed.SchoolB)).ShouldBeFalse();
    }

    [Fact]
    public async Task Same_value_is_a_no_op_without_audit()
    {
        var seed = await SeedAsync();

        var result = await SetAsync(seed.TeacherId, false);

        result.Status.ShouldBe(AdminTeacherSchoolAdminStatus.Success);
        result.Changed.ShouldBeFalse();
        (await AuditAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Revoke_clears_flag_and_resolver()
    {
        var seed = await SeedAsync(t => { t.IsSchoolAdmin = true; t.SchoolAdminGrantedAt = DateTime.UtcNow; });

        var result = await SetAsync(seed.TeacherId, false);

        result.Changed.ShouldBeTrue();
        var teacher = await TeacherAsync(seed.TeacherId);
        teacher.IsSchoolAdmin.ShouldBeFalse();
        teacher.SchoolAdminGrantedAt.ShouldBeNull();
        var audit = (await AuditAsync()).ShouldHaveSingleItem();
        audit.Action.ShouldBe(AdminUserAction.SchoolAdminRevoked);
        audit.FromSchoolId.ShouldBe(seed.SchoolA);
        (await AdministeredSchoolAsync()).ShouldBeNull();
    }

    public static TheoryData<string, AdminTeacherSchoolAdminStatus> Ineligible => new()
    {
        { "noSchool", AdminTeacherSchoolAdminStatus.NoSchool },
        { "pendingSchoolRequest", AdminTeacherSchoolAdminStatus.NoSchool },
        { "independent", AdminTeacherSchoolAdminStatus.NoSchool },
        { "notApproved", AdminTeacherSchoolAdminStatus.AccountNotApproved },
        { "suspended", AdminTeacherSchoolAdminStatus.AccountSuspended }
    };

    [Theory]
    [MemberData(nameof(Ineligible))]
    public async Task Ineligible_teacher_cannot_be_granted(string kind, AdminTeacherSchoolAdminStatus expected)
    {
        var seed = await SeedAsync(t =>
        {
            switch (kind)
            {
                case "noSchool": t.SchoolId = null; break;
                case "pendingSchoolRequest": t.RequestedSchoolId = t.SchoolId; t.SchoolId = null; t.ApprovalStatus = TeacherApprovalStatus.Pending; break;
                case "independent": t.IsIndependentTutor = true; break;
                case "notApproved": t.AccountApprovedAt = null; break;
                case "suspended": t.AccountApprovedAt = null; t.AccountSuspendedAt = DateTime.UtcNow; break;
            }
        });

        var result = await SetAsync(seed.TeacherId, true);

        result.Status.ShouldBe(expected);
        (await TeacherAsync(seed.TeacherId)).IsSchoolAdmin.ShouldBeFalse();
        (await AuditAsync()).ShouldHaveSingleItem().Outcome.ShouldBe(AdminUserActionOutcome.Denied);
    }

    [Fact]
    public async Task Unknown_teacher_is_not_found()
    {
        var result = await SetAsync(424242, true);
        result.Status.ShouldBe(AdminTeacherSchoolAdminStatus.TargetNotFound);
    }

    [Fact]
    public async Task Resolver_ignores_a_stale_flag_on_a_suspended_teacher()
    {
        await SeedAsync(t => { t.IsSchoolAdmin = true; t.AccountApprovedAt = null; t.AccountSuspendedAt = DateTime.UtcNow; });
        (await AdministeredSchoolAsync()).ShouldBeNull();
    }

    [Fact]
    public async Task School_change_clears_the_permission_but_same_school_keeps_it()
    {
        var seed = await SeedAsync(t => { t.IsSchoolAdmin = true; t.SchoolAdminGrantedAt = DateTime.UtcNow; });
        var targets = Substitute.For<IAdminAccountTargetResolver>();
        targets.ResolveAsync(Arg.Any<AdminUserTargetType>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AdminAccountTargetResolution(AdminAccountTargetStatus.Resolved, "kc-t"));
        var cache = new UserProfileCacheService(
            new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions())), NullLogger<UserProfileCacheService>.Instance);

        async Task<AdminTeacherSchoolChangeResult> ChangeAsync(int schoolId)
        {
            await using var ctx = _db.NewContext();
            return await new AdminTeacherSchoolService(ctx, targets, new AdminUserActionAuditService(ctx), Substitute.For<IKeycloakService>(), cache)
                .ChangeSchoolAsync(seed.TeacherId, schoolId, ActorSub);
        }

        (await ChangeAsync(seed.SchoolA)).Changed.ShouldBeFalse();
        (await TeacherAsync(seed.TeacherId)).IsSchoolAdmin.ShouldBeTrue();

        (await ChangeAsync(seed.SchoolB)).Changed.ShouldBeTrue();
        var teacher = await TeacherAsync(seed.TeacherId);
        teacher.SchoolId.ShouldBe(seed.SchoolB);
        teacher.IsSchoolAdmin.ShouldBeFalse();
        teacher.SchoolAdminGrantedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Suspension_clears_the_permission()
    {
        var seed = await SeedAsync(t => { t.IsSchoolAdmin = true; t.SchoolAdminGrantedAt = DateTime.UtcNow; });

        await using (var ctx = _db.NewContext())
        {
            var result = await new AdminTeacherSuspensionService(ctx, new AdminUserActionAuditService(ctx))
                .SuspendAsync(seed.TeacherId, "İnceleme", ActorSub);
            result.Status.ShouldBe(AdminTeacherSuspensionStatus.Success);
        }

        var teacher = await TeacherAsync(seed.TeacherId);
        teacher.IsSchoolAdmin.ShouldBeFalse();
        teacher.SchoolAdminGrantedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Switching_to_independent_tutor_clears_the_permission()
    {
        var seed = await SeedAsync(t => { t.IsSchoolAdmin = true; t.SchoolAdminGrantedAt = DateTime.UtcNow; });
        var authApi = Substitute.For<IAuthApiClient>();

        await using (var ctx = _db.NewContext())
            await new TeacherService(ctx, authApi).Save(TeacherUserId, new RegisterTeacherDto { IsIndependentTutor = true });

        var teacher = await TeacherAsync(seed.TeacherId);
        teacher.SchoolId.ShouldBeNull();
        teacher.IsSchoolAdmin.ShouldBeFalse();
    }
}
