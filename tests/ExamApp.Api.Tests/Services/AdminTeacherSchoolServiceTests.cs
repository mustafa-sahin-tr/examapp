using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Services.TeacherApprovals;
using ExamApp.Api.Tests.Support;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #313: admin öğretmen okul bağlama/değiştirme — öğrenci ucuyla (#277 madde 8) paralel audit sırası, koşullu UPDATE,
/// profil önbelleği + Keycloak school_id senkronu; bağımsız öğretmenin bayrakları, bekleyen okul talebinin temizlenmesi ve
/// okul değişince eski okulun öğrencilerine erişimin kalmaması.
/// </summary>
public class AdminTeacherSchoolServiceTests : IDisposable
{
    private const string ActorSub = "kc-admin";
    private const string TargetSub = "kc-t1";
    private const int TeacherUserId = 70;

    private readonly TestDb _db = TestDb.Create();
    private readonly IAdminAccountTargetResolver _targets = Substitute.For<IAdminAccountTargetResolver>();
    private readonly IKeycloakService _keycloak = Substitute.For<IKeycloakService>();
    private readonly IDistributedCache _cache = new MemoryDistributedCache(Options.Create(new MemoryDistributedCacheOptions()));
    private readonly IBackgroundJobClient _jobs = Substitute.For<IBackgroundJobClient>();

    public AdminTeacherSchoolServiceTests()
    {
        Target(AdminAccountTargetStatus.Resolved, TargetSub);
    }

    public void Dispose() => _db.Dispose();

    private void Target(AdminAccountTargetStatus status, string? sub = null) =>
        _targets.ResolveAsync(Arg.Any<AdminUserTargetType>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new AdminAccountTargetResolution(status, sub));

    private sealed record Seed(int SchoolA, int SchoolB, int GradeId, int TeacherId);

    private async Task<Seed> SeedAsync(Action<Teacher>? configure = null)
    {
        await using var ctx = _db.NewContext();
        var a = new School { Name = "A" };
        var b = new School { Name = "B" };
        var g = new Grade { Name = "7" };
        ctx.AddRange(a, b, g);
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
        return new Seed(a.Id, b.Id, g.Id, t.Id);
    }

    private Task<AdminTeacherSchoolChangeResult> RunAsync(int teacherId, int schoolId, params IInterceptor[] interceptors)
        => RunWithCacheAsync(_cache, teacherId, schoolId, interceptors);

    private async Task<AdminTeacherSchoolChangeResult> RunWithCacheAsync(
        IDistributedCache cache, int teacherId, int schoolId, params IInterceptor[] interceptors)
    {
        await using var ctx = interceptors.Length > 0 ? _db.NewContext(interceptors) : _db.NewContext();
        var service = new AdminTeacherSchoolService(ctx, _targets, new AdminUserActionAuditService(ctx), _keycloak,
            new UserProfileCacheService(cache, NullLogger<UserProfileCacheService>.Instance), jobs: _jobs);
        return await service.ChangeSchoolAsync(teacherId, schoolId, ActorSub);
    }

    private bool ScheduledSecondInvalidation() =>
        _jobs.ReceivedCalls().Any(c =>
            c.GetMethodInfo().Name == nameof(IBackgroundJobClient.Create)
            && c.GetArguments().OfType<Job>().Any(j => j.Method.Name == nameof(UserProfileCacheService.RemoveAsync)
                && (string?)j.Args[0] == TargetSub)
            && c.GetArguments().OfType<IState>().Any(st => st is ScheduledState));

    private async Task<Teacher> TeacherAsync(int teacherId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.Teachers.AsNoTracking().SingleAsync(t => t.Id == teacherId);
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

        var r = await RunAsync(s.TeacherId, s.SchoolB);

        r.Status.ShouldBe(AdminTeacherSchoolChangeStatus.Success);
        r.Changed.ShouldBeTrue();
        r.PreviousSchoolId.ShouldBe(s.SchoolA);
        r.SchoolId.ShouldBe(s.SchoolB);
        var teacher = await TeacherAsync(s.TeacherId);
        teacher.SchoolId.ShouldBe(s.SchoolB);
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Approved);

        var log = (await AuditAsync()).ShouldHaveSingleItem();
        log.Action.ShouldBe(AdminUserAction.TeacherSchoolChanged);
        log.TargetType.ShouldBe(AdminUserTargetType.Teacher);
        log.TargetId.ShouldBe(s.TeacherId);
        log.ActorKeycloakId.ShouldBe(ActorSub);
        log.Outcome.ShouldBe(AdminUserActionOutcome.Succeeded);
        log.FromSchoolId.ShouldBe(s.SchoolA);
        log.ToSchoolId.ShouldBe(s.SchoolB);

        r.ProfileCacheStale.ShouldBeFalse();
        await _keycloak.Received(1).SetSchoolIdAttributeAsync(TargetSub, s.SchoolB);
        await _targets.Received(1).ResolveAsync(AdminUserTargetType.Teacher, s.TeacherId, ActorSub, Arg.Any<CancellationToken>());
        // Review O2: GetOrSet yarışını kapatan gecikmeli ikinci önbellek düşürmesi planlandı.
        ScheduledSecondInvalidation().ShouldBeTrue();
    }

    [Fact]
    public async Task Profile_cache_entry_is_removed_so_the_next_request_resolves_the_new_school()
    {
        var s = await SeedAsync();
        var cache = new UserProfileCacheService(_cache, NullLogger<UserProfileCacheService>.Instance);
        await cache.SetAsync(TargetSub, new UserProfileDto { Id = TeacherUserId, KeycloakId = TargetSub, Role = "Teacher", SchoolId = s.SchoolA });

        (await RunAsync(s.TeacherId, s.SchoolB)).Status.ShouldBe(AdminTeacherSchoolChangeStatus.Success);

        (await cache.GetAsync(TargetSub)).ShouldBeNull();
    }

    [Fact]
    public async Task Same_school_is_an_idempotent_success_without_side_effects_or_audit()
    {
        var s = await SeedAsync();

        var r = await RunAsync(s.TeacherId, s.SchoolA);

        r.Status.ShouldBe(AdminTeacherSchoolChangeStatus.Success);
        r.Changed.ShouldBeFalse();
        r.PreviousSchoolId.ShouldBe(s.SchoolA);
        r.SchoolId.ShouldBe(s.SchoolA);
        (await AuditAsync()).ShouldBeEmpty();
        await _keycloak.DidNotReceiveWithAnyArgs().SetSchoolIdAttributeAsync(default!, default);
        ScheduledSecondInvalidation().ShouldBeFalse();
    }

    [Theory]
    [InlineData(AdminAccountTargetStatus.ForbiddenSelf, AdminTeacherSchoolChangeStatus.ForbiddenSelf)]
    [InlineData(AdminAccountTargetStatus.ForbiddenProtectedRole, AdminTeacherSchoolChangeStatus.ForbiddenProtectedRole)]
    public async Task Same_school_still_runs_the_target_check_first(AdminAccountTargetStatus targetStatus, AdminTeacherSchoolChangeStatus expected)
    {
        // Review D1: admin/servis hesabı ya da çağıranın kendisi için "200 changed=false" değil 403.
        var s = await SeedAsync();
        Target(targetStatus);

        (await RunAsync(s.TeacherId, s.SchoolA)).Status.ShouldBe(expected);

        (await AuditAsync()).ShouldHaveSingleItem().Outcome.ShouldBe(AdminUserActionOutcome.Denied);
    }

    [Fact]
    public async Task Same_school_with_a_leftover_request_clears_the_request()
    {
        // Review Ö3: aynı okul kısa devresi yalnızca kapanacak talep yokken.
        var s = await SeedAsync(t => t.ApprovalStatus = TeacherApprovalStatus.Rejected);
        await using (var ctx = _db.NewContext())
            await ctx.Teachers.Where(t => t.Id == s.TeacherId)
                .ExecuteUpdateAsync(set => set.SetProperty(t => t.RequestedSchoolId, s.SchoolB));

        var r = await RunAsync(s.TeacherId, s.SchoolA);

        r.Status.ShouldBe(AdminTeacherSchoolChangeStatus.Success);
        r.Changed.ShouldBeTrue();
        var teacher = await TeacherAsync(s.TeacherId);
        teacher.SchoolId.ShouldBe(s.SchoolA);
        teacher.RequestedSchoolId.ShouldBeNull();
        (await AuditAsync()).ShouldHaveSingleItem().Outcome.ShouldBe(AdminUserActionOutcome.Succeeded);
    }

    [Fact]
    public async Task Independent_tutor_keeps_independent_flags_and_only_school_is_written()
    {
        var s = await SeedAsync(t =>
        {
            t.SchoolId = null;
            t.IsIndependentTutor = true;
            t.ApprovalStatus = TeacherApprovalStatus.Rejected;
            t.RejectionReason = "eksik belge";
            t.HourlyRate = 300m;
            t.TeachesOnline = true;
        });

        var r = await RunAsync(s.TeacherId, s.SchoolB);

        r.Status.ShouldBe(AdminTeacherSchoolChangeStatus.Success);
        r.PreviousSchoolId.ShouldBeNull();
        var teacher = await TeacherAsync(s.TeacherId);
        teacher.SchoolId.ShouldBe(s.SchoolB);
        teacher.IsIndependentTutor.ShouldBeTrue();
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Rejected);
        teacher.RejectionReason.ShouldBe("eksik belge");
        teacher.HourlyRate.ShouldBe(300m);
        teacher.TeachesOnline.ShouldBeTrue();
    }

    [Fact]
    public async Task Active_independent_tutor_with_booked_student_is_IndependentActive_and_nothing_is_written()
    {
        // Review K1: bağlamak randevulu öğrencilere erişimi keserken tutor araması/takvimi açık kalırdı.
        var s = await SeedAsync(t =>
        {
            t.SchoolId = null;
            t.IsIndependentTutor = true;
            t.ApprovalStatus = TeacherApprovalStatus.Approved;
        });
        int bookedStudent;
        await using (var setup = _db.NewContext())
        {
            var st = new Student { UserId = 90, StudentNumber = "k", SchoolId = s.SchoolA, SchoolVerifiedAt = DateTime.UtcNow, GradeId = s.GradeId };
            setup.Students.Add(st);
            await setup.SaveChangesAsync();
            BookingSeed.Add(setup, s.TeacherId, st.Id, BookingStatus.Approved, 9);
            await setup.SaveChangesAsync();
            bookedStudent = st.Id;
        }

        var r = await RunAsync(s.TeacherId, s.SchoolB);

        r.Status.ShouldBe(AdminTeacherSchoolChangeStatus.IndependentActive);
        var teacher = await TeacherAsync(s.TeacherId);
        teacher.SchoolId.ShouldBeNull();
        teacher.IsIndependentTutor.ShouldBeTrue();
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Approved);
        (await AuditAsync()).ShouldHaveSingleItem().Outcome.ShouldBe(AdminUserActionOutcome.Denied);
        await _keycloak.DidNotReceiveWithAnyArgs().SetSchoolIdAttributeAsync(default!, default);

        // Randevulu öğrenciye erişim sürer (kapsam hâlâ bağımsız/booking).
        await using var ctx = _db.NewContext();
        (await new SchoolAccessPolicy(ctx).CanAccessStudentAsync(SchoolScope.For(TeacherUserId, null), bookedStudent)).ShouldBeTrue();
    }

    [Fact]
    public async Task Suspended_account_is_AccountSuspended_and_nothing_is_written()
    {
        // Review U1-b: askıdaki öğretmenin AccountApprovedAt'i null'dur; askı kapısı önce gelir.
        var s = await SeedAsync(t =>
        {
            t.AccountApprovedAt = null;
            t.AccountSuspendedAt = DateTime.UtcNow.AddDays(-1);
        });

        (await RunAsync(s.TeacherId, s.SchoolB)).Status.ShouldBe(AdminTeacherSchoolChangeStatus.AccountSuspended);

        (await TeacherAsync(s.TeacherId)).SchoolId.ShouldBe(s.SchoolA);
        (await AuditAsync()).ShouldHaveSingleItem().Outcome.ShouldBe(AdminUserActionOutcome.Denied);
    }

    [Fact]
    public async Task Pending_school_request_is_cleared_and_application_resolved_for_approved_account()
    {
        var s = await SeedAsync(t =>
        {
            t.SchoolId = null;
            t.ApprovalStatus = TeacherApprovalStatus.Pending;
        });
        await using (var ctx = _db.NewContext())
            await ctx.Teachers.Where(t => t.Id == s.TeacherId)
                .ExecuteUpdateAsync(set => set.SetProperty(t => t.RequestedSchoolId, s.SchoolA));

        (await RunAsync(s.TeacherId, s.SchoolB)).Status.ShouldBe(AdminTeacherSchoolChangeStatus.Success);

        var teacher = await TeacherAsync(s.TeacherId);
        teacher.SchoolId.ShouldBe(s.SchoolB);
        teacher.RequestedSchoolId.ShouldBeNull();
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Approved);
        teacher.IsIndependentTutor.ShouldBeFalse();
    }

    [Fact]
    public async Task Unapproved_account_is_AccountNotApproved_and_nothing_is_written()
    {
        // Review U2: okul bağlamak hesap onayı değildir (#287) — önce hesap onayı.
        var s = await SeedAsync(t =>
        {
            t.SchoolId = null;
            t.ApprovalStatus = TeacherApprovalStatus.Pending;
            t.AccountApprovedAt = null;
        });
        await using (var ctx = _db.NewContext())
            await ctx.Teachers.Where(t => t.Id == s.TeacherId)
                .ExecuteUpdateAsync(set => set.SetProperty(t => t.RequestedSchoolId, s.SchoolA));

        (await RunAsync(s.TeacherId, s.SchoolB)).Status.ShouldBe(AdminTeacherSchoolChangeStatus.AccountNotApproved);

        var teacher = await TeacherAsync(s.TeacherId);
        teacher.SchoolId.ShouldBeNull();
        teacher.RequestedSchoolId.ShouldBe(s.SchoolA);
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
        (await AuditAsync()).ShouldHaveSingleItem().Outcome.ShouldBe(AdminUserActionOutcome.Denied);
    }

    [Fact]
    public async Task Pending_independent_tutor_bound_to_school_gets_school_scope_instead_of_booking_scope()
    {
        // Review D2: bağımsız (onaysız) öğretmen bağlanınca karma durum (IsIndependentTutor + SchoolId) bilinçlidir — okul
        // kapsamı SchoolScope.IsIndependent = SchoolId null'a bakar; artık okul eşitliği geçerli, randevu kapsamı değil.
        // Onaysız bağımsız profil aramada görünmez ve yeni slot açamaz (BookingService Approved ister).
        var s = await SeedAsync(t =>
        {
            t.SchoolId = null;
            t.IsIndependentTutor = true;
            t.ApprovalStatus = TeacherApprovalStatus.Pending;
        });
        int bookedOtherSchool, newSchoolStudent;
        await using (var setup = _db.NewContext())
        {
            var booked = new Student { UserId = 91, StudentNumber = "x", SchoolId = s.SchoolA, SchoolVerifiedAt = DateTime.UtcNow, GradeId = s.GradeId };
            var inB = new Student { UserId = 92, StudentNumber = "y", SchoolId = s.SchoolB, SchoolVerifiedAt = DateTime.UtcNow, GradeId = s.GradeId };
            setup.Students.AddRange(booked, inB);
            await setup.SaveChangesAsync();
            BookingSeed.Add(setup, s.TeacherId, booked.Id, BookingStatus.Approved, 9);
            await setup.SaveChangesAsync();
            (bookedOtherSchool, newSchoolStudent) = (booked.Id, inB.Id);
        }

        (await RunAsync(s.TeacherId, s.SchoolB)).Status.ShouldBe(AdminTeacherSchoolChangeStatus.Success);

        await using var ctx = _db.NewContext();
        var teacher = await ctx.Teachers.AsNoTracking().SingleAsync(t => t.Id == s.TeacherId);
        teacher.IsIndependentTutor.ShouldBeTrue();
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
        var scope = SchoolScope.For(TeacherUserId, await UserSchoolResolver.ResolveAsync(ctx, TeacherUserId));
        scope.IsIndependent.ShouldBeFalse();
        var policy = new SchoolAccessPolicy(ctx);
        (await policy.CanAccessStudentAsync(scope, bookedOtherSchool)).ShouldBeFalse();
        (await policy.CanAccessStudentAsync(scope, newSchoolStudent)).ShouldBeTrue();
    }

    [Fact]
    public async Task Rejected_school_request_history_still_shows_the_rejection_after_binding()
    {
        // Review U1-a: normalizasyon Approved'a çeker, ama başvuru geçmişi reddedilmiş talebi "yeni okula onaylı" göstermez.
        var s = await SeedAsync(t =>
        {
            t.SchoolId = null;
            t.ApprovalStatus = TeacherApprovalStatus.Pending;
        });
        await using (var ctx = _db.NewContext())
            await ctx.Teachers.Where(t => t.Id == s.TeacherId)
                .ExecuteUpdateAsync(set => set.SetProperty(t => t.RequestedSchoolId, s.SchoolA));
        var authApi = Substitute.For<IAuthApiClient>();
        authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<ExamApp.Api.Models.Dtos.UserLookupResultDto>());
        await using (var ctx = _db.NewContext())
            (await new TeacherApprovalService(ctx, authApi, auditService: new AdminUserActionAuditService(ctx))
                .RejectAsync(s.TeacherId, "belge yok", 1, ActorSub)).Success.ShouldBeTrue();

        (await RunAsync(s.TeacherId, s.SchoolB)).Status.ShouldBe(AdminTeacherSchoolChangeStatus.Success);

        var teacher = await TeacherAsync(s.TeacherId);
        teacher.SchoolId.ShouldBe(s.SchoolB);
        teacher.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Approved); // işlevsellik için normalize
        teacher.RequestedSchoolId.ShouldBeNull();

        await using var read = _db.NewContext();
        var service = new TeacherApprovalService(read, authApi);
        var detail = (await service.GetApplicationAsync(s.TeacherId)).ShouldNotBeNull();
        detail.Status.ShouldBe(nameof(TeacherApprovalStatus.Rejected));
        detail.RequestedSchoolId.ShouldBeNull(); // bağlanılan okul talep edilmiş gibi gösterilmez
        detail.RejectionReason.ShouldBe("belge yok");
        detail.DecidedAt.ShouldNotBeNull();
        var item = (await service.ListApplicationsAsync(TeacherApplicationStatusFilter.All, 1, 20)).Items.ShouldHaveSingleItem();
        item.Status.ShouldBe(nameof(TeacherApprovalStatus.Rejected));
        item.RequestedSchoolId.ShouldBeNull();
        (await service.ListApplicationsAsync(TeacherApplicationStatusFilter.Pending, 1, 20)).Items.ShouldBeEmpty();
    }

    [Fact]
    public async Task Transient_cache_failure_is_retried_and_reported_as_fresh()
    {
        var s = await SeedAsync();
        var cache = Substitute.For<IDistributedCache>();
        var calls = 0;
        cache.RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++calls == 1 ? throw new InvalidOperationException("redis blip") : Task.CompletedTask);

        var r = await RunWithCacheAsync(cache, s.TeacherId, s.SchoolB);

        r.Status.ShouldBe(AdminTeacherSchoolChangeStatus.Success);
        r.ProfileCacheStale.ShouldBeFalse();
        calls.ShouldBe(2);
        (await AuditAsync()).ShouldHaveSingleItem().Outcome.ShouldBe(AdminUserActionOutcome.Succeeded);
    }

    [Fact]
    public async Task Persistent_cache_failure_keeps_the_change_and_reports_stale_cache()
    {
        // Review O1: denemeler tükenince okul yine değişmiş olur; audit SucceededCacheStale, yanıt profileCacheStale=true.
        var s = await SeedAsync();
        var cache = Substitute.For<IDistributedCache>();
        var calls = 0;
        cache.RemoveAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(_ => { calls++; throw new InvalidOperationException("redis down"); });

        var r = await RunWithCacheAsync(cache, s.TeacherId, s.SchoolB);

        r.Status.ShouldBe(AdminTeacherSchoolChangeStatus.Success);
        r.Changed.ShouldBeTrue();
        r.ProfileCacheStale.ShouldBeTrue();
        calls.ShouldBe(3);
        (await TeacherAsync(s.TeacherId)).SchoolId.ShouldBe(s.SchoolB);
        (await AuditAsync()).ShouldHaveSingleItem().Outcome.ShouldBe(AdminUserActionOutcome.SucceededCacheStale);
        await _keycloak.Received(1).SetSchoolIdAttributeAsync(TargetSub, s.SchoolB);
        ScheduledSecondInvalidation().ShouldBeTrue(); // gecikmeli ikinci deneme yine planlanır
    }

    [Fact]
    public async Task Unknown_teacher_is_TargetNotFound_and_audited_as_NotFound()
    {
        await SeedAsync();

        var r = await RunAsync(98765, 1);

        r.Status.ShouldBe(AdminTeacherSchoolChangeStatus.TargetNotFound);
        var log = (await AuditAsync()).ShouldHaveSingleItem();
        log.Outcome.ShouldBe(AdminUserActionOutcome.NotFound);
        log.Action.ShouldBe(AdminUserAction.TeacherSchoolChanged);
    }

    [Fact]
    public async Task Soft_deleted_teacher_is_TargetNotFound()
    {
        var s = await SeedAsync();
        await using (var ctx = _db.NewContext())
            await ctx.Teachers.Where(x => x.Id == s.TeacherId).ExecuteUpdateAsync(set => set.SetProperty(x => x.IsDeleted, true));

        (await RunAsync(s.TeacherId, s.SchoolB)).Status.ShouldBe(AdminTeacherSchoolChangeStatus.TargetNotFound);
    }

    [Fact]
    public async Task Unknown_school_is_SchoolNotFound_and_nothing_changes()
    {
        var s = await SeedAsync();

        (await RunAsync(s.TeacherId, 424242)).Status.ShouldBe(AdminTeacherSchoolChangeStatus.SchoolNotFound);

        (await TeacherAsync(s.TeacherId)).SchoolId.ShouldBe(s.SchoolA);
        var log = (await AuditAsync()).ShouldHaveSingleItem();
        log.Outcome.ShouldBe(AdminUserActionOutcome.SchoolNotFound);
        log.FromSchoolId.ShouldBe(s.SchoolA);
        log.ToSchoolId.ShouldBe(424242);
        await _targets.DidNotReceiveWithAnyArgs().ResolveAsync(default, default, default!, default);
    }

    [Theory]
    [InlineData(AdminAccountTargetStatus.ForbiddenSelf, AdminTeacherSchoolChangeStatus.ForbiddenSelf, AdminUserActionOutcome.Denied)]
    [InlineData(AdminAccountTargetStatus.ForbiddenProtectedRole, AdminTeacherSchoolChangeStatus.ForbiddenProtectedRole, AdminUserActionOutcome.Denied)]
    [InlineData(AdminAccountTargetStatus.AccountNotFound, AdminTeacherSchoolChangeStatus.AccountNotFound, AdminUserActionOutcome.NotFound)]
    [InlineData(AdminAccountTargetStatus.TargetNotFound, AdminTeacherSchoolChangeStatus.TargetNotFound, AdminUserActionOutcome.NotFound)]
    public async Task Rejected_targets_do_not_change_the_school_and_are_audited(
        AdminAccountTargetStatus targetStatus, AdminTeacherSchoolChangeStatus expected, AdminUserActionOutcome outcome)
    {
        var s = await SeedAsync();
        Target(targetStatus);

        (await RunAsync(s.TeacherId, s.SchoolB)).Status.ShouldBe(expected);

        (await TeacherAsync(s.TeacherId)).SchoolId.ShouldBe(s.SchoolA);
        (await AuditAsync()).ShouldHaveSingleItem().Outcome.ShouldBe(outcome);
        await _keycloak.DidNotReceiveWithAnyArgs().SetSchoolIdAttributeAsync(default!, default);
    }

    [Fact]
    public async Task Upstream_failure_changes_nothing_and_writes_no_audit()
    {
        var s = await SeedAsync();
        Target(AdminAccountTargetStatus.UpstreamFailure);

        (await RunAsync(s.TeacherId, s.SchoolB)).Status.ShouldBe(AdminTeacherSchoolChangeStatus.UpstreamFailure);

        (await TeacherAsync(s.TeacherId)).SchoolId.ShouldBe(s.SchoolA);
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
            var service = new AdminTeacherSchoolService(ctx, _targets, audit, _keycloak);
            await Should.ThrowAsync<InvalidOperationException>(() => service.ChangeSchoolAsync(s.TeacherId, s.SchoolB, ActorSub));
        }

        (await TeacherAsync(s.TeacherId)).SchoolId.ShouldBe(s.SchoolA);
    }

    [Fact]
    public async Task Concurrent_change_between_read_and_conditional_update_is_a_Conflict()
    {
        var s = await SeedAsync();
        // Koşullu UPDATE çalışmadan hemen önce "öğretmen bağımsızlığa geçer" (TeacherService.Save yarışı).
        var interceptor = new ConcurrentTeacherWriter(s.TeacherId);

        var r = await RunAsync(s.TeacherId, s.SchoolB, interceptor);

        interceptor.Fired.ShouldBeTrue();
        r.Status.ShouldBe(AdminTeacherSchoolChangeStatus.Conflict);
        var teacher = await TeacherAsync(s.TeacherId);
        teacher.SchoolId.ShouldBeNull();
        teacher.IsIndependentTutor.ShouldBeTrue();
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

        var r = await RunAsync(s.TeacherId, s.SchoolB);

        r.Status.ShouldBe(AdminTeacherSchoolChangeStatus.Success);
        (await TeacherAsync(s.TeacherId)).SchoolId.ShouldBe(s.SchoolB);
        (await AuditAsync()).ShouldHaveSingleItem().Outcome.ShouldBe(AdminUserActionOutcome.Succeeded);
    }

    /// <summary>
    /// Okul değişince: öğretmenin DB'den çözülen okul kapsamı yeni okul; eski okulun öğrencisi kapsam dışı. Eski okula yapılmış
    /// sınıf ataması (kendi SchoolId'si = A) yeni okulun aynı sınıftaki öğrencisine SIZMAZ, eski okulun öğrencisinde kalır;
    /// öğretmenin atama ilerleme görünümü eski okulun öğrencisini artık listelemez.
    /// </summary>
    [Fact]
    public async Task After_school_change_old_school_students_are_out_of_reach_and_old_grade_assignment_does_not_leak()
    {
        var s = await SeedAsync();
        int studentA, studentB, wsId;
        await using (var setup = _db.NewContext())
        {
            setup.SetCurrentUser(TeacherUserId);
            var stA = new Student { UserId = 80, StudentNumber = "a", SchoolId = s.SchoolA, SchoolVerifiedAt = DateTime.UtcNow, GradeId = s.GradeId };
            var stB = new Student { UserId = 81, StudentNumber = "b", SchoolId = s.SchoolB, SchoolVerifiedAt = DateTime.UtcNow, GradeId = s.GradeId };
            var ws = new Worksheet { Name = "W", Description = "", GradeId = s.GradeId };
            setup.AddRange(stA, stB, ws);
            await setup.SaveChangesAsync();
            setup.WorksheetAssignments.Add(new WorksheetAssignment
            {
                WorksheetId = ws.Id, GradeId = s.GradeId, SchoolId = s.SchoolA, StartAt = DateTime.UtcNow.AddDays(-1)
            });
            await setup.SaveChangesAsync();
            (studentA, studentB, wsId) = (stA.Id, stB.Id, ws.Id);
        }

        // Önce: öğretmen A okulunda, A öğrencisini görür.
        await using (var before = _db.NewContext())
        {
            var scopeBefore = SchoolScope.For(TeacherUserId, await UserSchoolResolver.ResolveAsync(before, TeacherUserId));
            (await new SchoolAccessPolicy(before).CanAccessStudentAsync(scopeBefore, studentA)).ShouldBeTrue();
        }

        (await RunAsync(s.TeacherId, s.SchoolB)).Status.ShouldBe(AdminTeacherSchoolChangeStatus.Success);

        await using var ctx = _db.NewContext();
        var resolvedSchool = await UserSchoolResolver.ResolveAsync(ctx, TeacherUserId);
        resolvedSchool.ShouldBe(s.SchoolB);
        var scope = SchoolScope.For(TeacherUserId, resolvedSchool);
        var policy = new SchoolAccessPolicy(ctx);

        (await policy.CanAccessStudentAsync(scope, studentA)).ShouldBeFalse();
        (await policy.CanAccessStudentAsync(scope, studentB)).ShouldBeTrue();
        (await policy.ApplyScope(ctx.Students.AsNoTracking(), scope).Select(x => x.Id).ToListAsync())
            .ShouldBe(new[] { studentB });

        // Atama kendi SchoolId'sini (A) taşır: yeni okulun öğrencisine açılmaz, eski okulun öğrencisinde kalır.
        var now = DateTime.UtcNow;
        (await ctx.ActiveAssignmentsFor(studentB, s.GradeId, s.SchoolB, now).AnyAsync()).ShouldBeFalse();
        (await ctx.ActiveAssignmentsFor(studentA, s.GradeId, s.SchoolA, now).Select(a => a.WorksheetId).ToListAsync())
            .ShouldBe(new[] { wsId });

        // Öğretmenin ilerleme görünümü: eski sınıf ataması hâlâ listede ama hiçbir öğrenci (ne A ne B) görünmez.
        var overview = await new WorksheetAssignmentService(ctx, policy).GetWorksheetAssignmentsForTeacherAsync(wsId, scope);
        overview.Assignments.ShouldHaveSingleItem().Students.ShouldBeEmpty();
    }

    private sealed class ConcurrentTeacherWriter(int teacherId) : DbCommandInterceptor
    {
        public bool Fired { get; private set; }

        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            System.Data.Common.DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (!Fired && command.CommandText.Contains("UPDATE \"Teachers\"", StringComparison.Ordinal))
            {
                Fired = true;
                await using var concurrent = command.Connection!.CreateCommand();
                concurrent.CommandText =
                    $"UPDATE \"Teachers\" SET \"SchoolId\" = NULL, \"IsIndependentTutor\" = 1 WHERE \"Id\" = {teacherId}";
                await concurrent.ExecuteNonQueryAsync(cancellationToken);
            }

            return result;
        }
    }
}
