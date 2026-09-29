using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Models.Dtos.Teachers;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.TeacherApprovals;
using ExamApp.Api.Services.Teachers;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #289: admin öğretmen hesap onayını askıya alır / askıyı kaldırır. Guard <c>Suspended</c> döner; askı audit'lenir
/// (neden audit'e yazılmaz); askıdaki öğretmenin başvuru onayı askıyı sessizce kaldırmaz ve onay kuyruğu onu
/// "hesap onayı bekliyor" diye göstermez.
/// </summary>
public class TeacherSuspensionTests : IDisposable
{
    private const string AdminSub = "kc-admin-289";
    private const int AdminUserId = 999;

    private readonly TestDb _db = TestDb.Create();
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();

    public TeacherSuspensionTests()
    {
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(Array.Empty<UserLookupResultDto>());
    }

    public void Dispose() => _db.Dispose();

    // ---------------- yardımcılar ----------------

    private async Task<Teacher> SeedAsync(Teacher teacher)
    {
        await using var ctx = _db.NewContext();
        ctx.Teachers.Add(teacher);
        await ctx.SaveChangesAsync();
        return teacher;
    }

    private Task<Teacher> SeedApprovedAsync(int userId) => SeedAsync(new Teacher
    {
        UserId = userId, ApprovalStatus = TeacherApprovalStatus.Approved, AccountApprovedAt = DateTime.UtcNow.AddDays(-10)
    });

    private async Task<Teacher> ReloadAsync(int teacherId)
    {
        await using var ctx = _db.NewContext();
        return await ctx.Teachers.AsNoTracking().SingleAsync(t => t.Id == teacherId);
    }

    private async Task<List<AdminUserActionLog>> AuditRowsAsync()
    {
        await using var ctx = _db.NewContext();
        return await ctx.AdminUserActionLogs.AsNoTracking().OrderBy(a => a.Id).ToListAsync();
    }

    private async Task<AdminTeacherSuspensionResult> SuspendAsync(int teacherId, string? reason = "Şikayet incelemesi")
    {
        await using var ctx = _db.NewContext();
        return await new AdminTeacherSuspensionService(ctx, new AdminUserActionAuditService(ctx))
            .SuspendAsync(teacherId, reason, AdminSub);
    }

    private async Task<AdminTeacherSuspensionResult> UnsuspendAsync(int teacherId)
    {
        await using var ctx = _db.NewContext();
        return await new AdminTeacherSuspensionService(ctx, new AdminUserActionAuditService(ctx))
            .UnsuspendAsync(teacherId, AdminSub);
    }

    private async Task<TeacherApprovalCheck> GuardAsync(int userId)
    {
        await using var ctx = _db.NewContext();
        return await new ApprovedTeacherGuard(ctx).CheckAsync(userId);
    }

    private async Task<ResponseBaseDto> ApproveApplicationAsync(int teacherId)
    {
        await using var ctx = _db.NewContext();
        return await new TeacherApprovalService(ctx, _authApi, auditService: new AdminUserActionAuditService(ctx))
            .ApproveAsync(teacherId, AdminUserId, AdminSub);
    }

    private async Task<Paged<TeacherApplicationListItemDto>> ListAsync(TeacherApplicationStatusFilter filter)
    {
        await using var ctx = _db.NewContext();
        return await new TeacherApprovalService(ctx, _authApi).ListApplicationsAsync(filter, 1, 100);
    }

    // ---------------- askıya alma ----------------

    [Fact]
    public async Task Suspend_closes_account_approval_records_reason_and_guard_reports_Suspended()
    {
        var teacher = await SeedApprovedAsync(501);
        (await GuardAsync(501)).ShouldBe(TeacherApprovalCheck.Approved);
        var before = DateTime.UtcNow;

        var result = await SuspendAsync(teacher.Id, "  Şikayet incelemesi  ");

        result.Status.ShouldBe(AdminTeacherSuspensionStatus.Success);
        result.AccountApprovedAt.ShouldBeNull();
        result.AccountSuspendedAt.ShouldNotBeNull();

        var row = await ReloadAsync(teacher.Id);
        row.AccountApprovedAt.ShouldBeNull();
        row.AccountSuspendedAt.ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(before.AddSeconds(-1));
        row.AccountSuspensionReason.ShouldBe("Şikayet incelemesi"); // trim'li
        row.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Approved); // başvuru durumu değişmez

        (await GuardAsync(501)).ShouldBe(TeacherApprovalCheck.Suspended);
    }

    [Fact]
    public async Task Suspend_writes_Requested_then_Succeeded_audit_without_the_reason()
    {
        var teacher = await SeedApprovedAsync(502);

        await SuspendAsync(teacher.Id, "Gizli PII içeren neden");

        var audit = (await AuditRowsAsync()).ShouldHaveSingleItem();
        audit.Action.ShouldBe(AdminUserAction.TeacherSuspended);
        audit.TargetType.ShouldBe(AdminUserTargetType.Teacher);
        audit.TargetId.ShouldBe(teacher.Id);
        audit.ActorKeycloakId.ShouldBe(AdminSub);
        audit.Outcome.ShouldBe(AdminUserActionOutcome.Succeeded); // Requested satırı güncellendi

        // Neden audit tablosunda hiçbir kolonda yok (log serbest metin taşımaz).
        typeof(AdminUserActionLog).GetProperties()
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => (string?)p.GetValue(audit))
            .ShouldNotContain(v => v != null && v.Contains("PII"));
    }

    [Fact]
    public async Task Requested_audit_is_written_before_the_side_effect()
    {
        var teacher = await SeedApprovedAsync(503);
        await using var ctx = _db.NewContext();
        var audit = Substitute.For<IAdminUserActionAuditService>();
        DateTime? approvedAtWhenRequested = DateTime.MinValue;
        audit.RecordAsync(Arg.Any<AdminUserActionRecord>(), AdminUserActionOutcome.Requested, Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                approvedAtWhenRequested = (await ReloadAsync(teacher.Id)).AccountApprovedAt;
                return 42L;
            });

        var result = await new AdminTeacherSuspensionService(ctx, audit).SuspendAsync(teacher.Id, "neden", AdminSub);

        result.Status.ShouldBe(AdminTeacherSuspensionStatus.Success);
        approvedAtWhenRequested.ShouldNotBeNull(); // Requested yazılırken hesap hâlâ onaylıydı
        await audit.Received(1).RecordAsync(
            Arg.Is<AdminUserActionRecord>(r => r.Action == AdminUserAction.TeacherSuspended && r.TargetId == teacher.Id),
            AdminUserActionOutcome.Requested, Arg.Any<CancellationToken>());
        await audit.Received(1).TryUpdateOutcomeAsync(42L, AdminUserActionOutcome.Succeeded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Suspend_requires_a_reason(string? reason)
    {
        var teacher = await SeedApprovedAsync(504);

        (await SuspendAsync(teacher.Id, reason)).Status.ShouldBe(AdminTeacherSuspensionStatus.ReasonRequired);

        (await ReloadAsync(teacher.Id)).AccountApprovedAt.ShouldNotBeNull();
        (await AuditRowsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Suspend_reason_longer_than_500_is_rejected_but_500_after_trim_is_accepted()
    {
        var teacher = await SeedApprovedAsync(505);

        (await SuspendAsync(teacher.Id, new string('x', 501))).Status.ShouldBe(AdminTeacherSuspensionStatus.ReasonTooLong);
        (await ReloadAsync(teacher.Id)).AccountSuspendedAt.ShouldBeNull();
        (await AuditRowsAsync()).ShouldBeEmpty();

        (await SuspendAsync(teacher.Id, "  " + new string('y', 500) + "  ")).Status.ShouldBe(AdminTeacherSuspensionStatus.Success);
        (await ReloadAsync(teacher.Id)).AccountSuspensionReason!.Length.ShouldBe(500);
    }

    [Fact]
    public async Task Suspend_missing_teacher_is_NotFound_and_audited()
    {
        (await SuspendAsync(424242)).Status.ShouldBe(AdminTeacherSuspensionStatus.TargetNotFound);

        var audit = (await AuditRowsAsync()).ShouldHaveSingleItem();
        audit.Outcome.ShouldBe(AdminUserActionOutcome.NotFound);
        audit.Action.ShouldBe(AdminUserAction.TeacherSuspended);
    }

    [Fact]
    public async Task Suspend_already_suspended_or_never_approved_teacher_is_a_state_conflict_without_side_effect()
    {
        var teacher = await SeedApprovedAsync(506);
        await SuspendAsync(teacher.Id, "ilk");
        var suspendedAt = (await ReloadAsync(teacher.Id)).AccountSuspendedAt;

        (await SuspendAsync(teacher.Id, "ikinci")).Status.ShouldBe(AdminTeacherSuspensionStatus.AlreadySuspended);
        var row = await ReloadAsync(teacher.Id);
        row.AccountSuspendedAt.ShouldBe(suspendedAt);
        row.AccountSuspensionReason.ShouldBe("ilk");

        var pending = await SeedAsync(new Teacher { UserId = 507, ApprovalStatus = TeacherApprovalStatus.Pending });
        (await SuspendAsync(pending.Id)).Status.ShouldBe(AdminTeacherSuspensionStatus.AccountNotApproved);
        (await ReloadAsync(pending.Id)).AccountSuspendedAt.ShouldBeNull();

        var audits = await AuditRowsAsync();
        audits.Select(a => a.Outcome).ShouldBe(
            [AdminUserActionOutcome.Succeeded, AdminUserActionOutcome.Conflict, AdminUserActionOutcome.Conflict]);
    }

    [Fact]
    public async Task Suspend_race_after_read_updates_nothing_and_marks_audit_Conflict()
    {
        var teacher = await SeedApprovedAsync(508);
        await using var ctx = _db.NewContext();
        var audit = Substitute.For<IAdminUserActionAuditService>();
        audit.RecordAsync(Arg.Any<AdminUserActionRecord>(), AdminUserActionOutcome.Requested, Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                // Okuma ile koşullu yazma arasında başka bir admin askıya aldı.
                await using var other = _db.NewContext();
                await other.Teachers.Where(t => t.Id == teacher.Id).ExecuteUpdateAsync(s => s
                    .SetProperty(t => t.AccountApprovedAt, (DateTime?)null)
                    .SetProperty(t => t.AccountSuspendedAt, DateTime.UtcNow)
                    .SetProperty(t => t.AccountSuspensionReason, "diğer admin"));
                return 7L;
            });

        var result = await new AdminTeacherSuspensionService(ctx, audit).SuspendAsync(teacher.Id, "benim nedenim", AdminSub);

        result.Status.ShouldBe(AdminTeacherSuspensionStatus.Conflict);
        (await ReloadAsync(teacher.Id)).AccountSuspensionReason.ShouldBe("diğer admin");
        await audit.Received(1).TryUpdateOutcomeAsync(7L, AdminUserActionOutcome.Conflict);
        await audit.DidNotReceive().TryUpdateOutcomeAsync(Arg.Any<long>(), AdminUserActionOutcome.Succeeded);
    }

    // ---------------- geri açma ----------------

    [Fact]
    public async Task Unsuspend_reopens_account_clears_suspension_and_is_audited()
    {
        var teacher = await SeedApprovedAsync(510);
        await SuspendAsync(teacher.Id);
        var before = DateTime.UtcNow;

        var result = await UnsuspendAsync(teacher.Id);

        result.Status.ShouldBe(AdminTeacherSuspensionStatus.Success);
        result.AccountApprovedAt.ShouldNotBeNull();
        result.AccountSuspendedAt.ShouldBeNull();

        var row = await ReloadAsync(teacher.Id);
        row.AccountApprovedAt.ShouldNotBeNull().ShouldBeGreaterThanOrEqualTo(before.AddSeconds(-1));
        row.AccountSuspendedAt.ShouldBeNull();
        row.AccountSuspensionReason.ShouldBeNull();
        (await GuardAsync(510)).ShouldBe(TeacherApprovalCheck.Approved);

        var audits = await AuditRowsAsync();
        audits.Select(a => (a.Action, a.Outcome)).ShouldBe([
            (AdminUserAction.TeacherSuspended, AdminUserActionOutcome.Succeeded),
            (AdminUserAction.TeacherUnsuspended, AdminUserActionOutcome.Succeeded)
        ]);
    }

    [Fact]
    public async Task Unsuspend_not_suspended_or_missing_teacher_fails_without_side_effect()
    {
        var teacher = await SeedApprovedAsync(511);
        var approvedAt = (await ReloadAsync(teacher.Id)).AccountApprovedAt;

        (await UnsuspendAsync(teacher.Id)).Status.ShouldBe(AdminTeacherSuspensionStatus.NotSuspended);
        (await ReloadAsync(teacher.Id)).AccountApprovedAt.ShouldBe(approvedAt);

        (await UnsuspendAsync(989898)).Status.ShouldBe(AdminTeacherSuspensionStatus.TargetNotFound);

        (await AuditRowsAsync()).Select(a => (a.Action, a.Outcome)).ShouldBe([
            (AdminUserAction.TeacherUnsuspended, AdminUserActionOutcome.Conflict),
            (AdminUserAction.TeacherUnsuspended, AdminUserActionOutcome.NotFound)
        ]);
    }

    // ---------------- madde 4: başvuru onayı ve onay kuyruğu ----------------

    [Fact]
    public async Task Approving_a_suspended_teachers_new_application_does_not_lift_the_suspension()
    {
        var teacher = await SeedApprovedAsync(520);
        await SuspendAsync(teacher.Id);

        // Askıdaki öğretmen bağımsız tutor başvurusu açar (TeacherService.Save'in yaptığı geçiş).
        await using (var ctx = _db.NewContext())
        {
            await ctx.Teachers.Where(t => t.Id == teacher.Id).ExecuteUpdateAsync(s => s
                .SetProperty(t => t.IsIndependentTutor, true)
                .SetProperty(t => t.ApprovalStatus, TeacherApprovalStatus.Pending));
        }

        var pending = await ListAsync(TeacherApplicationStatusFilter.Pending);
        var item = pending.Items.ShouldHaveSingleItem();
        item.TeacherId.ShouldBe(teacher.Id);
        item.RequiresAccountApproval.ShouldBeFalse(); // gerçek bir tutor başvurusu, hesap onayı değil
        item.AccountSuspended.ShouldBeTrue(); // code review Orta-2: kuyruk satırı askıyı gösterir

        await using (var ctx = _db.NewContext())
        {
            var detail = await new TeacherApprovalService(ctx, _authApi).GetApplicationAsync(teacher.Id);
            detail.ShouldNotBeNull().AccountSuspended.ShouldBeTrue();
        }

        (await ApproveApplicationAsync(teacher.Id)).Success.ShouldBeTrue();

        var row = await ReloadAsync(teacher.Id);
        row.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Approved);
        row.AccountApprovedAt.ShouldBeNull();
        row.AccountSuspendedAt.ShouldNotBeNull();
        (await GuardAsync(520)).ShouldBe(TeacherApprovalCheck.Suspended);
    }

    [Theory]
    [InlineData(TeacherApprovalStatus.Approved)] // L5 görünümü: Approved + AccountApprovedAt null
    [InlineData(TeacherApprovalStatus.Pending)]  // talepsiz Pending + AccountApprovedAt null
    public async Task Suspended_teacher_without_a_request_is_not_in_the_approval_queue_and_cannot_be_approved(
        TeacherApprovalStatus status)
    {
        var teacher = await SeedAsync(new Teacher
        {
            UserId = 530, ApprovalStatus = status, AccountApprovedAt = null,
            AccountSuspendedAt = DateTime.UtcNow, AccountSuspensionReason = "neden"
        });

        (await ListAsync(TeacherApplicationStatusFilter.Pending)).Items.ShouldBeEmpty();
        (await ListAsync(TeacherApplicationStatusFilter.All)).Items.ShouldBeEmpty();

        var approve = await ApproveApplicationAsync(teacher.Id);
        approve.Success.ShouldBeFalse();
        approve.NotFound.ShouldBeFalse();

        var row = await ReloadAsync(teacher.Id);
        row.AccountApprovedAt.ShouldBeNull();
        row.AccountSuspendedAt.ShouldNotBeNull();
    }

    [Fact]
    public async Task Previously_approved_then_suspended_teacher_shows_as_decided_not_pending_in_all_list()
    {
        // #287 hesap onayı admin kararıyla (audit TeacherApproved) verilmiş, sonra askıya alınmış öğretmen.
        var teacher = await SeedAsync(new Teacher { UserId = 540, ApprovalStatus = TeacherApprovalStatus.Pending });
        (await ApproveApplicationAsync(teacher.Id)).Success.ShouldBeTrue();
        await SuspendAsync(teacher.Id);

        (await ListAsync(TeacherApplicationStatusFilter.Pending)).Items.ShouldBeEmpty();

        // "Tümü" listesinde karar audit'inden tanınır: onaylanmış, hesap onayı beklemiyor (L5 Pending görünümü değil).
        var item = (await ListAsync(TeacherApplicationStatusFilter.All)).Items.ShouldHaveSingleItem();
        item.TeacherId.ShouldBe(teacher.Id);
        item.Status.ShouldBe("Approved");
        item.RequiresAccountApproval.ShouldBeFalse();
        item.DecidedAt.ShouldNotBeNull();
        item.AccountSuspended.ShouldBeTrue();
    }

    [Fact]
    public async Task Application_rows_report_not_suspended_and_serialize_as_accountSuspended()
    {
        var teacher = await SeedAsync(new Teacher { UserId = 545, ApprovalStatus = TeacherApprovalStatus.Pending });

        var item = (await ListAsync(TeacherApplicationStatusFilter.Pending)).Items.ShouldHaveSingleItem();
        item.TeacherId.ShouldBe(teacher.Id);
        item.AccountSuspended.ShouldBeFalse();

        var json = System.Text.Json.JsonSerializer.Serialize(item, new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        json.ShouldContain("\"accountSuspended\":false");
        var detailJson = System.Text.Json.JsonSerializer.Serialize(new TeacherApplicationDetailDto { AccountSuspended = true },
            new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web));
        detailJson.ShouldContain("\"accountSuspended\":true");
    }

    // ---------------- öğretmen erişim DTO'su ----------------

    [Fact]
    public void Teacher_access_state_reports_suspension_without_the_reason()
    {
        var state = TeacherApprovalState.From(new Teacher
        {
            ApprovalStatus = TeacherApprovalStatus.Approved, AccountApprovedAt = null,
            AccountSuspendedAt = DateTime.UtcNow, AccountSuspensionReason = "gizli neden"
        });

        state.TeacherAccountApproved.ShouldBeFalse();
        state.TeacherAccountSuspended.ShouldBeTrue();
        state.RejectionReason.ShouldBeNull();
        state.ToString().ShouldNotContain("gizli neden");

        var approved = TeacherApprovalState.From(new Teacher { AccountApprovedAt = DateTime.UtcNow });
        approved.TeacherAccountApproved.ShouldBeTrue();
        approved.TeacherAccountSuspended.ShouldBeFalse();
    }

    // ---------------- admin öğretmen listesi ----------------

    [Fact]
    public async Task Admin_teacher_list_carries_suspension_fields()
    {
        var active = await SeedApprovedAsync(550);
        var suspended = await SeedApprovedAsync(551);
        await SuspendAsync(suspended.Id, "Belge süresi doldu");

        var directory = Substitute.For<IAdminUserDirectory>();
        directory.ResolveWithAccountStatusAsync(Arg.Any<IReadOnlyCollection<int>>(), Arg.Any<CancellationToken>())
            .Returns((IReadOnlyDictionary<int, UserLookupResultDto>)new Dictionary<int, UserLookupResultDto>());
        await using var ctx = _db.NewContext();
        var page = await new AdminTeacherService(ctx, directory).ListAsync(1, 20, null, false);

        var a = page.Items.Single(i => i.Id == active.Id);
        a.AccountApproved.ShouldBeTrue();
        a.AccountSuspended.ShouldBeFalse();
        a.AccountSuspendedAt.ShouldBeNull();
        a.AccountSuspensionReason.ShouldBeNull();

        var s = page.Items.Single(i => i.Id == suspended.Id);
        s.AccountApproved.ShouldBeFalse();
        s.AccountSuspended.ShouldBeTrue();
        s.AccountSuspendedAt.ShouldNotBeNull();
        s.AccountSuspensionReason.ShouldBe("Belge süresi doldu");
    }
}
