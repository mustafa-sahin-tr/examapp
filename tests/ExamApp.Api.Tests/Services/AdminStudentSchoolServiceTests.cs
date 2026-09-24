using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #277 (madde 8): admin öğrenci okul değişikliği — #155/#156 audit sırası (Requested fail-closed, yazımdan önce),
/// koşullu UPDATE (eşzamanlı değişiklik → Conflict), profil önbelleği + Keycloak school_id senkronu, reddedilen/bulunamayan
/// hedeflerin izi, idempotent aynı okul ve sınıf ataması görünürlüğünün yeni okulu izlemesi.
/// </summary>
public class AdminStudentSchoolServiceTests : IDisposable
{
    private const string ActorSub = "kc-admin-actor";
    private const string TargetSub = "7f6e5d4c-3b2a-4100-9f8e-aabbccddeeff";

    private readonly TestDb _db = TestDb.Create();
    private readonly IAdminAccountTargetResolver _targets = Substitute.For<IAdminAccountTargetResolver>();
    private readonly IKeycloakService _keycloak = Substitute.For<IKeycloakService>();
    private readonly IDistributedCache _cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));

    public AdminStudentSchoolServiceTests()
    {
        Target(AdminAccountTargetStatus.Resolved, TargetSub);
    }

    public void Dispose() => _db.Dispose();

    private void Target(AdminAccountTargetStatus status, string? sub = null) =>
        _targets.ResolveAsync(Arg.Any<AdminUserTargetType>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AdminAccountTargetResolution(status, sub));

    private sealed record Seed(int SchoolA, int SchoolB, int GradeId, int StudentId);

    private async Task<Seed> SeedAsync(bool studentHasSchool = true)
    {
        await using var ctx = _db.NewContext();
        var a = new School { Name = "A" };
        var b = new School { Name = "B" };
        var g = new Grade { Name = "7" };
        ctx.AddRange(a, b, g);
        await ctx.SaveChangesAsync();
        var s = new Student { UserId = 50, StudentNumber = "n", GradeId = g.Id, SchoolId = studentHasSchool ? a.Id : null };
        ctx.Students.Add(s);
        await ctx.SaveChangesAsync();
        return new Seed(a.Id, b.Id, g.Id, s.Id);
    }

    private async Task<AdminStudentSchoolChangeResult> RunAsync(int studentId, int schoolId, params IInterceptor[] interceptors)
    {
        await using var ctx = interceptors.Length > 0 ? _db.NewContext(interceptors) : _db.NewContext();
        var service = new AdminStudentSchoolService(ctx, _targets, new AdminUserActionAuditService(ctx), _keycloak,
            new UserProfileCacheService(_cache, NullLogger<UserProfileCacheService>.Instance));
        return await service.ChangeSchoolAsync(studentId, schoolId, ActorSub);
    }

    private async Task<int?> SchoolOfAsync(int studentId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.Students.AsNoTracking().Where(s => s.Id == studentId).Select(s => s.SchoolId).SingleAsync();
    }

    private async Task<List<AdminUserActionLog>> AuditAsync()
    {
        await using var ctx = _db.NewContext();
        return await ctx.AdminUserActionLogs.AsNoTracking().OrderBy(l => l.Id).ToListAsync();
    }

    [Fact]
    public async Task Changes_the_school_audits_success_and_refreshes_cache_and_school_claim()
    {
        var s = await SeedAsync();

        var r = await RunAsync(s.StudentId, s.SchoolB);

        r.Status.ShouldBe(AdminStudentSchoolChangeStatus.Success);
        r.Changed.ShouldBeTrue();
        r.PreviousSchoolId.ShouldBe(s.SchoolA);
        r.SchoolId.ShouldBe(s.SchoolB);
        (await SchoolOfAsync(s.StudentId)).ShouldBe(s.SchoolB);

        var log = (await AuditAsync()).ShouldHaveSingleItem();
        log.Action.ShouldBe(AdminUserAction.StudentSchoolChanged);
        log.TargetType.ShouldBe(AdminUserTargetType.Student);
        log.TargetId.ShouldBe(s.StudentId);
        log.ActorKeycloakId.ShouldBe(ActorSub);
        log.Outcome.ShouldBe(AdminUserActionOutcome.Succeeded);
        log.FromSchoolId.ShouldBe(s.SchoolA); // issue #277 review (security L5)
        log.ToSchoolId.ShouldBe(s.SchoolB);

        await _keycloak.Received(1).SetSchoolIdAttributeAsync(TargetSub, s.SchoolB);
        await _targets.Received(1).ResolveAsync(AdminUserTargetType.Student, s.StudentId, ActorSub, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Profile_cache_entry_is_removed_so_the_next_request_resolves_the_new_school()
    {
        var s = await SeedAsync();
        var cache = new UserProfileCacheService(_cache, NullLogger<UserProfileCacheService>.Instance);
        await cache.SetAsync(TargetSub, new UserProfileDto { Id = 50, KeycloakId = TargetSub, Role = "Student", SchoolId = s.SchoolA });

        (await RunAsync(s.StudentId, s.SchoolB)).Status.ShouldBe(AdminStudentSchoolChangeStatus.Success);

        (await cache.GetAsync(TargetSub)).ShouldBeNull();
    }

    [Fact]
    public async Task Schoolless_student_can_be_assigned_a_school()
    {
        var s = await SeedAsync(studentHasSchool: false);

        var r = await RunAsync(s.StudentId, s.SchoolB);

        r.Status.ShouldBe(AdminStudentSchoolChangeStatus.Success);
        r.PreviousSchoolId.ShouldBeNull();
        (await SchoolOfAsync(s.StudentId)).ShouldBe(s.SchoolB);
    }

    [Fact]
    public async Task Same_school_is_an_idempotent_success_without_side_effects_or_audit()
    {
        var s = await SeedAsync();

        var r = await RunAsync(s.StudentId, s.SchoolA);

        r.Status.ShouldBe(AdminStudentSchoolChangeStatus.Success);
        r.Changed.ShouldBeFalse();
        (await AuditAsync()).ShouldBeEmpty();
        await _targets.DidNotReceiveWithAnyArgs().ResolveAsync(default, default, default!, default);
        await _keycloak.DidNotReceiveWithAnyArgs().SetSchoolIdAttributeAsync(default!, default);
    }

    [Fact]
    public async Task Unknown_student_is_TargetNotFound_and_audited_as_NotFound()
    {
        await SeedAsync();

        var r = await RunAsync(98765, 1);

        r.Status.ShouldBe(AdminStudentSchoolChangeStatus.TargetNotFound);
        (await AuditAsync()).ShouldHaveSingleItem().Outcome.ShouldBe(AdminUserActionOutcome.NotFound);
    }

    [Fact]
    public async Task Soft_deleted_student_is_TargetNotFound()
    {
        var s = await SeedAsync();
        await using (var ctx = _db.NewContext())
            await ctx.Students.Where(x => x.Id == s.StudentId).ExecuteUpdateAsync(set => set.SetProperty(x => x.IsDeleted, true));

        (await RunAsync(s.StudentId, s.SchoolB)).Status.ShouldBe(AdminStudentSchoolChangeStatus.TargetNotFound);
    }

    [Fact]
    public async Task Unknown_school_is_SchoolNotFound_and_nothing_changes()
    {
        var s = await SeedAsync();

        (await RunAsync(s.StudentId, 424242)).Status.ShouldBe(AdminStudentSchoolChangeStatus.SchoolNotFound);

        (await SchoolOfAsync(s.StudentId)).ShouldBe(s.SchoolA);
        var log = (await AuditAsync()).ShouldHaveSingleItem(); // issue #277 review: SchoolNotFound da audit'lenir
        log.Outcome.ShouldBe(AdminUserActionOutcome.SchoolNotFound);
        log.FromSchoolId.ShouldBe(s.SchoolA);
        log.ToSchoolId.ShouldBe(424242);
        await _targets.DidNotReceiveWithAnyArgs().ResolveAsync(default, default, default!, default);
    }

    [Theory]
    [InlineData(AdminAccountTargetStatus.ForbiddenSelf, AdminStudentSchoolChangeStatus.ForbiddenSelf, AdminUserActionOutcome.Denied)]
    [InlineData(AdminAccountTargetStatus.ForbiddenProtectedRole, AdminStudentSchoolChangeStatus.ForbiddenProtectedRole, AdminUserActionOutcome.Denied)]
    [InlineData(AdminAccountTargetStatus.AccountNotFound, AdminStudentSchoolChangeStatus.AccountNotFound, AdminUserActionOutcome.NotFound)]
    public async Task Rejected_targets_do_not_change_the_school_and_are_audited(
        AdminAccountTargetStatus targetStatus, AdminStudentSchoolChangeStatus expected, AdminUserActionOutcome outcome)
    {
        var s = await SeedAsync();
        Target(targetStatus);

        (await RunAsync(s.StudentId, s.SchoolB)).Status.ShouldBe(expected);

        (await SchoolOfAsync(s.StudentId)).ShouldBe(s.SchoolA);
        (await AuditAsync()).ShouldHaveSingleItem().Outcome.ShouldBe(outcome);
    }

    [Fact]
    public async Task Upstream_failure_changes_nothing_and_writes_no_audit()
    {
        var s = await SeedAsync();
        Target(AdminAccountTargetStatus.UpstreamFailure);

        (await RunAsync(s.StudentId, s.SchoolB)).Status.ShouldBe(AdminStudentSchoolChangeStatus.UpstreamFailure);

        (await SchoolOfAsync(s.StudentId)).ShouldBe(s.SchoolA);
        (await AuditAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Audit_write_failure_is_fail_closed_and_the_school_is_not_changed()
    {
        var s = await SeedAsync();
        var audit = Substitute.For<IAdminUserActionAuditService>();
        audit.RecordAsync(Arg.Any<ExamApp.Api.Models.Dtos.Admin.AdminUserActionRecord>(), AdminUserActionOutcome.Requested, Arg.Any<CancellationToken>())
            .Returns<long>(_ => throw new InvalidOperationException("db down"));

        await using (var ctx = _db.NewContext())
        {
            var service = new AdminStudentSchoolService(ctx, _targets, audit, _keycloak);
            await Should.ThrowAsync<InvalidOperationException>(() => service.ChangeSchoolAsync(s.StudentId, s.SchoolB, ActorSub));
        }

        (await SchoolOfAsync(s.StudentId)).ShouldBe(s.SchoolA);
    }

    [Fact]
    public async Task Concurrent_change_between_read_and_conditional_update_is_a_Conflict()
    {
        var s = await SeedAsync();
        // Koşullu UPDATE çalışmadan hemen önce "başka bir admin" okulu değiştirir.
        var interceptor = new ConcurrentSchoolWriter(s.StudentId, s.SchoolB);

        var r = await RunAsync(s.StudentId, s.SchoolB, interceptor);

        interceptor.Fired.ShouldBeTrue();
        r.Status.ShouldBe(AdminStudentSchoolChangeStatus.Conflict);
        var conflictLog = (await AuditAsync()).ShouldHaveSingleItem();
        conflictLog.Outcome.ShouldBe(AdminUserActionOutcome.Conflict);
        conflictLog.FromSchoolId.ShouldBe(s.SchoolA);
        conflictLog.ToSchoolId.ShouldBe(s.SchoolB);
        await _keycloak.DidNotReceiveWithAnyArgs().SetSchoolIdAttributeAsync(default!, default);
    }

    [Fact]
    public async Task Keycloak_claim_failure_does_not_undo_the_change()
    {
        var s = await SeedAsync();
        _keycloak.SetSchoolIdAttributeAsync(Arg.Any<string>(), Arg.Any<int?>()).Returns(_ => throw new HttpRequestException("kc down"));

        var r = await RunAsync(s.StudentId, s.SchoolB);

        r.Status.ShouldBe(AdminStudentSchoolChangeStatus.Success);
        (await SchoolOfAsync(s.StudentId)).ShouldBe(s.SchoolB);
        (await AuditAsync()).ShouldHaveSingleItem().Outcome.ShouldBe(AdminUserActionOutcome.Succeeded);
    }

    [Fact]
    public async Task Grade_assignments_follow_the_new_school_while_direct_assignments_are_kept()
    {
        var s = await SeedAsync();
        int wsA, wsB, wsDirect;
        await using (var ctx = _db.NewContext())
        {
            var a = new Worksheet { Name = "A", Description = "", GradeId = s.GradeId };
            var b = new Worksheet { Name = "B", Description = "", GradeId = s.GradeId };
            var d = new Worksheet { Name = "D", Description = "", GradeId = s.GradeId };
            ctx.Worksheets.AddRange(a, b, d);
            await ctx.SaveChangesAsync();
            var start = DateTime.UtcNow.AddDays(-1);
            ctx.WorksheetAssignments.AddRange(
                new WorksheetAssignment { WorksheetId = a.Id, GradeId = s.GradeId, SchoolId = s.SchoolA, StartAt = start },
                new WorksheetAssignment { WorksheetId = b.Id, GradeId = s.GradeId, SchoolId = s.SchoolB, StartAt = start },
                new WorksheetAssignment { WorksheetId = d.Id, StudentId = s.StudentId, StartAt = start });
            await ctx.SaveChangesAsync();
            (wsA, wsB, wsDirect) = (a.Id, b.Id, d.Id);
        }

        (await RunAsync(s.StudentId, s.SchoolB)).Status.ShouldBe(AdminStudentSchoolChangeStatus.Success);

        await using var read = _db.NewContext();
        var visible = await read.ActiveAssignmentsFor(s.StudentId, s.GradeId, s.SchoolB, DateTime.UtcNow)
            .Select(a => a.WorksheetId).ToListAsync();
        visible.ShouldBe(new[] { wsB, wsDirect }, ignoreOrder: true);
        visible.ShouldNotContain(wsA);
    }

    private sealed class ConcurrentSchoolWriter(int studentId, int schoolId) : DbCommandInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!Fired && command.CommandText.Contains("UPDATE \"Students\"", StringComparison.Ordinal))
            {
                Fired = true;
                await using var concurrent = command.Connection!.CreateCommand();
                concurrent.CommandText = $"UPDATE \"Students\" SET \"SchoolId\" = {schoolId} WHERE \"Id\" = {studentId}";
                await concurrent.ExecuteNonQueryAsync(cancellationToken);
            }

            return result;
        }
    }
}
