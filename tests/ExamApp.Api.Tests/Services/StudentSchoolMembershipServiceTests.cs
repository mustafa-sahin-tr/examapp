using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Api.Services.StudentSchoolMemberships;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #361 (4c): bekleyen öğrenci okul üyeliği onayı. Onaylayıcı: platform admin (tüm okullar) ya da aynı okulun ONAYLI,
/// askıda olmayan öğretmeni. Başka okulun öğretmeni / onaysız / askıdaki / bağımsız öğretmen hiçbir başvuruyu görmez ve karar
/// veremez (404 — varlık sızdırılmaz, #402 ile tutarlı).
/// </summary>
public class StudentSchoolMembershipServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();
    private readonly IKeycloakService _keycloak = Substitute.For<IKeycloakService>();

    private const int AdminUserId = 900;
    private const int TeacherAUserId = 1;
    private const int TeacherBUserId = 2;
    private const int UnapprovedTeacherAUserId = 3;
    private const int SuspendedTeacherAUserId = 4;
    private const int IndependentTeacherUserId = 5;

    private sealed record World(int SchoolA, int SchoolB, int PendingA, int PendingA2, int PendingB, int VerifiedA, int Schoolless);

    public StudentSchoolMembershipServiceTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(call => Task.FromResult<IReadOnlyList<UserLookupResultDto>>(
                ((IEnumerable<int>)call[0]).Select(id => new UserLookupResultDto { Id = id, KeycloakId = $"kc-{id}", FullName = $"User {id}" }).ToList()));
    }

    public void Dispose() => _db.Dispose();

    private StudentSchoolMembershipService NewService(AppDbContext ctx, IAdminUserActionAuditService? audit = null) =>
        new(ctx, _authApi, audit ?? new AdminUserActionAuditService(ctx), _keycloak);

    private static StudentSchoolApprover Teacher(int userId) => new(userId, IsAdmin: false, $"kc-t-{userId}");
    private static StudentSchoolApprover Admin() => new(AdminUserId, IsAdmin: true, "kc-admin");

    private async Task<World> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var a = new School { Name = "Okul A" };
        var b = new School { Name = "Okul B" };
        var grade = new Grade { Name = "8" };
        ctx.AddRange(a, b, grade);
        await ctx.SaveChangesAsync();

        var approved = DateTime.UtcNow.AddDays(-10);
        ctx.Teachers.AddRange(
            new Teacher { UserId = TeacherAUserId, SchoolId = a.Id, AccountApprovedAt = approved },
            new Teacher { UserId = TeacherBUserId, SchoolId = b.Id, AccountApprovedAt = approved },
            new Teacher { UserId = UnapprovedTeacherAUserId, SchoolId = a.Id, AccountApprovedAt = null },
            new Teacher { UserId = SuspendedTeacherAUserId, SchoolId = a.Id, AccountApprovedAt = null, AccountSuspendedAt = approved },
            new Teacher { UserId = IndependentTeacherUserId, SchoolId = null, IsIndependentTutor = true, AccountApprovedAt = approved });

        var pendingA = new Student { UserId = 10, StudentNumber = "20260010", GradeId = grade.Id, SchoolId = a.Id };
        var pendingA2 = new Student { UserId = 11, StudentNumber = "20260011", SchoolId = a.Id };
        var pendingB = new Student { UserId = 12, StudentNumber = "20260012", SchoolId = b.Id };
        var verifiedA = new Student { UserId = 13, StudentNumber = "20260013", SchoolId = a.Id, SchoolVerifiedAt = approved };
        var schoolless = new Student { UserId = 14, StudentNumber = "20260014" };
        ctx.Students.AddRange(pendingA, pendingA2, pendingB, verifiedA, schoolless);
        await ctx.SaveChangesAsync();

        return new World(a.Id, b.Id, pendingA.Id, pendingA2.Id, pendingB.Id, verifiedA.Id, schoolless.Id);
    }

    private async Task<Student> Reload(int studentId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.Students.AsNoTracking().SingleAsync(s => s.Id == studentId);
    }

    // ---- Liste ----

    [Fact]
    public async Task List_SchoolTeacher_SeesOnlyOwnSchoolPending_WithMaskedNumberAndNames()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();

        var page = await NewService(ctx).ListPendingAsync(Teacher(TeacherAUserId), schoolIdFilter: w.SchoolB, page: 1, pageSize: 20);

        page.TotalCount.ShouldBe(2); // filtre öğretmende yok sayılır; B okulu sızmaz
        page.Items.Select(i => i.StudentId).ShouldBe(new[] { w.PendingA, w.PendingA2 }, ignoreOrder: true);
        page.Items.ShouldAllBe(i => i.SchoolId == w.SchoolA && i.SchoolName == "Okul A");
        var first = page.Items.Single(i => i.StudentId == w.PendingA);
        first.FullName.ShouldBe("User 10");
        first.GradeName.ShouldBe("8");
        first.StudentNumber.ShouldBe(StudentNumberMask.Apply("20260010"));
        first.StudentNumber.ShouldNotBe("20260010");
    }

    [Fact]
    public async Task List_Admin_SeesAllPending_AndCanFilterBySchool()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        (await service.ListPendingAsync(Admin(), null, 1, 20)).TotalCount.ShouldBe(3);
        var filtered = await service.ListPendingAsync(Admin(), w.SchoolB, 1, 20);
        filtered.Items.Select(i => i.StudentId).ShouldBe(new[] { w.PendingB });
    }

    [Theory]
    [InlineData(UnapprovedTeacherAUserId)]
    [InlineData(SuspendedTeacherAUserId)]
    [InlineData(IndependentTeacherUserId)]
    [InlineData(777)] // öğretmen kaydı yok
    public async Task List_TeacherWithoutApprovedSchoolMembership_SeesNothing(int userId)
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        (await service.ListPendingAsync(Teacher(userId), null, 1, 20)).TotalCount.ShouldBe(0);
        (await service.CountPendingAsync(Teacher(userId))).ShouldBe(0);
    }

    [Fact]
    public async Task Count_MatchesScope()
    {
        await SeedAsync();
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        (await service.CountPendingAsync(Teacher(TeacherAUserId))).ShouldBe(2);
        (await service.CountPendingAsync(Teacher(TeacherBUserId))).ShouldBe(1);
        (await service.CountPendingAsync(Admin())).ShouldBe(3);
    }

    // ---- Onay ----

    [Fact]
    public async Task Approve_SameSchoolTeacher_VerifiesMembership_AndInvalidatesSchoolClaim()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).ApproveAsync(Teacher(TeacherAUserId), w.PendingA);

        result.Status.ShouldBe(StudentSchoolDecisionStatus.Success);
        var row = await Reload(w.PendingA);
        row.SchoolId.ShouldBe(w.SchoolA);
        row.SchoolVerifiedAt.ShouldNotBeNull();
        row.SchoolVerifiedByUserId.ShouldBe(TeacherAUserId);
        await using var check = _db.NewContext();
        (await UserSchoolResolver.ResolveAsync(check, 10)).ShouldBe(w.SchoolA);
        await _keycloak.Received(1).SetSchoolIdAttributeAsync("kc-10", w.SchoolA);
    }

    [Fact]
    public async Task Approve_OtherSchoolTeacher_IsNotFound_AndChangesNothing()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).ApproveAsync(Teacher(TeacherBUserId), w.PendingA);

        result.Status.ShouldBe(StudentSchoolDecisionStatus.NotFound);
        (await Reload(w.PendingA)).SchoolVerifiedAt.ShouldBeNull();
        await _keycloak.DidNotReceiveWithAnyArgs().SetSchoolIdAttributeAsync(default!, default);
    }

    [Theory]
    [InlineData(UnapprovedTeacherAUserId)]
    [InlineData(SuspendedTeacherAUserId)]
    [InlineData(IndependentTeacherUserId)]
    public async Task Approve_TeacherWithoutApprovedSchoolMembership_IsNotFound(int userId)
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();

        (await NewService(ctx).ApproveAsync(Teacher(userId), w.PendingA)).Status.ShouldBe(StudentSchoolDecisionStatus.NotFound);
        (await Reload(w.PendingA)).SchoolVerifiedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Approve_NonPendingOrUnknownStudent_IsNotFound()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        (await service.ApproveAsync(Teacher(TeacherAUserId), w.VerifiedA)).Status.ShouldBe(StudentSchoolDecisionStatus.NotFound);
        (await service.ApproveAsync(Teacher(TeacherAUserId), w.Schoolless)).Status.ShouldBe(StudentSchoolDecisionStatus.NotFound);
        (await service.ApproveAsync(Teacher(TeacherAUserId), 99999)).Status.ShouldBe(StudentSchoolDecisionStatus.NotFound);
        (await service.ApproveAsync(Admin(), 99999)).Status.ShouldBe(StudentSchoolDecisionStatus.NotFound);
    }

    [Fact]
    public async Task Approve_SoftDeletedStudent_IsNotFound()
    {
        var w = await SeedAsync();
        await using (var del = _db.NewContext())
            await del.Students.Where(s => s.Id == w.PendingA).ExecuteUpdateAsync(set => set.SetProperty(s => s.IsDeleted, true));

        await using var ctx = _db.NewContext();
        (await NewService(ctx).ApproveAsync(Admin(), w.PendingA)).Status.ShouldBe(StudentSchoolDecisionStatus.NotFound);
    }

    [Fact]
    public async Task Approve_Admin_AnySchool_Succeeds_SecondDecisionIsNotFound()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        (await service.ApproveAsync(Admin(), w.PendingB)).Status.ShouldBe(StudentSchoolDecisionStatus.Success);
        var row = await Reload(w.PendingB);
        row.SchoolVerifiedAt.ShouldNotBeNull();
        row.SchoolVerifiedByUserId.ShouldBe(AdminUserId);

        // Artık beklemede değil — tekrar onay/ret yok.
        (await service.ApproveAsync(Admin(), w.PendingB)).Status.ShouldBe(StudentSchoolDecisionStatus.NotFound);
        (await service.RejectAsync(Teacher(TeacherBUserId), w.PendingB)).Status.ShouldBe(StudentSchoolDecisionStatus.NotFound);
    }

    // ---- Ret ----

    [Fact]
    public async Task Reject_SameSchoolTeacher_ClearsSchool()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();

        var result = await NewService(ctx).RejectAsync(Teacher(TeacherAUserId), w.PendingA2);

        result.Status.ShouldBe(StudentSchoolDecisionStatus.Success);
        var row = await Reload(w.PendingA2);
        row.SchoolId.ShouldBeNull();
        row.SchoolVerifiedAt.ShouldBeNull();
        await using var check = _db.NewContext();
        (await NewService(check).CountPendingAsync(Teacher(TeacherAUserId))).ShouldBe(1);
    }

    [Fact]
    public async Task Reject_OtherSchoolTeacher_IsNotFound_AndKeepsRequest()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();

        (await NewService(ctx).RejectAsync(Teacher(TeacherAUserId), w.PendingB)).Status.ShouldBe(StudentSchoolDecisionStatus.NotFound);
        (await Reload(w.PendingB)).SchoolId.ShouldBe(w.SchoolB);
    }

    [Fact]
    public async Task Reject_AdminCanReject_VerifiedMembershipCannotBeRejected()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);

        (await service.RejectAsync(Admin(), w.PendingB)).Status.ShouldBe(StudentSchoolDecisionStatus.Success);
        (await service.RejectAsync(Admin(), w.VerifiedA)).Status.ShouldBe(StudentSchoolDecisionStatus.NotFound);
        (await Reload(w.VerifiedA)).SchoolId.ShouldBe(w.SchoolA);
    }

    [Fact]
    public async Task Decision_AfterAnotherDecision_IsNotFound()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();
        var service = NewService(ctx);
        var approver = Teacher(TeacherAUserId);

        (await service.RejectAsync(approver, w.PendingA)).Status.ShouldBe(StudentSchoolDecisionStatus.Success);
        // Reddedilen öğrencinin okulu temizlendi — artık bu okulda bekleyen başvuru yok.
        (await service.ApproveAsync(approver, w.PendingA)).Status.ShouldBe(StudentSchoolDecisionStatus.NotFound);
        (await Reload(w.PendingA)).SchoolVerifiedAt.ShouldBeNull();
    }

    // ---- issue #361 review: karar izi (fail-closed audit) ----

    private async Task<List<AdminUserActionLog>> AuditAsync()
    {
        await using var ctx = _db.NewContext();
        return await ctx.AdminUserActionLogs.AsNoTracking().OrderBy(l => l.Id).ToListAsync();
    }

    [Fact]
    public async Task Approve_by_teacher_is_audited_with_teacher_actor_and_school()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();

        await NewService(ctx).ApproveAsync(Teacher(TeacherAUserId), w.PendingA);

        var log = (await AuditAsync()).ShouldHaveSingleItem();
        log.Action.ShouldBe(AdminUserAction.StudentSchoolApproved);
        log.ActorKeycloakId.ShouldBe($"kc-t-{TeacherAUserId}");
        log.TargetType.ShouldBe(AdminUserTargetType.Student);
        log.TargetId.ShouldBe(w.PendingA);
        log.ToSchoolId.ShouldBe(w.SchoolA);
        // Önbellek servisi yok (DI'siz) → karar başarılı ama önbellek düşürülemedi olarak izlenir.
        log.Outcome.ShouldBeOneOf(AdminUserActionOutcome.Succeeded, AdminUserActionOutcome.SucceededCacheStale);
    }

    [Fact]
    public async Task Reject_by_admin_is_audited_succeeded()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();

        await NewService(ctx).RejectAsync(Admin(), w.PendingB);

        var log = (await AuditAsync()).ShouldHaveSingleItem();
        log.Action.ShouldBe(AdminUserAction.StudentSchoolRejected);
        log.ActorKeycloakId.ShouldBe("kc-admin");
        log.Outcome.ShouldBe(AdminUserActionOutcome.Succeeded);
        log.ToSchoolId.ShouldBe(w.SchoolB);
    }

    [Fact]
    public async Task Out_of_scope_decision_is_audited_not_found()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();

        await NewService(ctx).ApproveAsync(Teacher(TeacherBUserId), w.PendingA);

        var log = (await AuditAsync()).ShouldHaveSingleItem();
        log.Outcome.ShouldBe(AdminUserActionOutcome.NotFound);
        log.ActorKeycloakId.ShouldBe($"kc-t-{TeacherBUserId}");
        log.ToSchoolId.ShouldBeNull();
    }

    [Fact]
    public async Task Audit_write_failure_blocks_the_decision()
    {
        var w = await SeedAsync();
        var audit = Substitute.For<IAdminUserActionAuditService>();
        audit.RecordAsync(Arg.Any<AdminUserActionRecord>(), Arg.Any<AdminUserActionOutcome>(), Arg.Any<CancellationToken>())
            .Returns<Task<long>>(_ => throw new InvalidOperationException("audit down"));
        await using var ctx = _db.NewContext();

        await Should.ThrowAsync<InvalidOperationException>(() => NewService(ctx, audit).ApproveAsync(Teacher(TeacherAUserId), w.PendingA));
        await Should.ThrowAsync<InvalidOperationException>(() => NewService(ctx, audit).RejectAsync(Teacher(TeacherAUserId), w.PendingA));

        var row = await Reload(w.PendingA);
        row.SchoolId.ShouldBe(w.SchoolA);
        row.SchoolVerifiedAt.ShouldBeNull();
    }

    // ---- issue #361 review: ret izi + aynı okul bekleme süresi ----

    [Fact]
    public async Task Reject_records_rejected_school_time_and_actor()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();

        await NewService(ctx).RejectAsync(Teacher(TeacherAUserId), w.PendingA);

        var row = await Reload(w.PendingA);
        row.SchoolId.ShouldBeNull();
        row.LastRejectedSchoolId.ShouldBe(w.SchoolA);
        row.SchoolRejectedAt.ShouldNotBeNull();
        row.SchoolRejectedByUserId.ShouldBe(TeacherAUserId);
    }

    private async Task<ResponseBaseDto> ReRegisterAsync(int userId, int schoolId)
    {
        await using var ctx = _db.NewContext();
        return await new StudentService(ctx, _authApi, new SchoolAccessPolicy(ctx))
            .Save(userId, new RegisterStudentDto { StudentNumber = "20260010", SchoolId = schoolId, GradeId = await ctx.Grades.Select(g => g.Id).FirstAsync() });
    }

    [Fact]
    public async Task Rejected_student_cannot_request_same_school_within_cooldown_but_can_choose_another()
    {
        var w = await SeedAsync();
        await using (var ctx = _db.NewContext())
            await NewService(ctx).RejectAsync(Teacher(TeacherAUserId), w.PendingA);

        var same = await ReRegisterAsync(10, w.SchoolA);
        same.Success.ShouldBeFalse();
        same.Message.ShouldContain("7");
        (await Reload(w.PendingA)).SchoolId.ShouldBeNull();

        var other = await ReRegisterAsync(10, w.SchoolB);
        other.Success.ShouldBeTrue(other.Message);
        var row = await Reload(w.PendingA);
        row.SchoolId.ShouldBe(w.SchoolB);
        row.SchoolVerifiedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Rejected_school_can_be_requested_again_after_cooldown()
    {
        var w = await SeedAsync();
        await using (var ctx = _db.NewContext())
        {
            await NewService(ctx).RejectAsync(Teacher(TeacherAUserId), w.PendingA);
            var expired = DateTime.UtcNow - StudentService.SchoolRejectCooldown - TimeSpan.FromMinutes(1);
            await ctx.Students.Where(s => s.Id == w.PendingA)
                .ExecuteUpdateAsync(set => set.SetProperty(s => s.SchoolRejectedAt, expired));
        }

        var again = await ReRegisterAsync(10, w.SchoolA);

        again.Success.ShouldBeTrue(again.Message);
        var row = await Reload(w.PendingA);
        row.SchoolId.ShouldBe(w.SchoolA);
        row.SchoolVerifiedAt.ShouldBeNull();
    }

    [Fact]
    public async Task Check_constraint_rejects_verified_membership_without_school()
    {
        var w = await SeedAsync();
        await using var ctx = _db.NewContext();

        await Should.ThrowAsync<Exception>(() => ctx.Students.Where(s => s.Id == w.VerifiedA)
            .ExecuteUpdateAsync(set => set.SetProperty(s => s.SchoolId, (int?)null)));
        (await Reload(w.VerifiedA)).SchoolId.ShouldBe(w.SchoolA);
    }
}
