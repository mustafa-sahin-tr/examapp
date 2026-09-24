using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.TeacherApprovals;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #94: admin onay paneli — bağımsız öğretmen başvurularını listeleme/onaylama/reddetme.
/// Issue #157: karar bildirimi outbox event'i + admin karar audit'i.
/// </summary>
public class TeacherApprovalServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private const int AdminUserId = 999;
    private const string AdminSub = "kc-admin-999";
    private readonly IAuthApiClient _authApi = Substitute.For<IAuthApiClient>();
    private readonly IAdminUserActionAuditService _audit = Substitute.For<IAdminUserActionAuditService>();

    private TeacherApprovalService NewService(AppDbContext ctx) => new(ctx, _authApi);

    private TeacherApprovalService NewServiceWithAudit(AppDbContext ctx) =>
        new(ctx, _authApi, auditService: _audit);

    private void UserResolvesToSub(int userId, string? sub) =>
        _authApi.GetUsersByIdsAsync(Arg.Is<IEnumerable<int>>(ids => ids.Contains(userId)), Arg.Any<CancellationToken>())
            .Returns(new List<UserLookupResultDto>
            {
                new() { Id = userId, KeycloakId = sub ?? string.Empty }
            });

    private static Teacher IndependentPending(int userId) => new()
    {
        UserId = userId,
        IsIndependentTutor = true,
        ApprovalStatus = TeacherApprovalStatus.Pending
    };

    // ---- issue #262: liste maskeli, detay tam e-posta ----

    private void UserResolvesTo(int userId, string fullName, string email) =>
        _authApi.GetUsersByIdsAsync(Arg.Is<IEnumerable<int>>(ids => ids.Contains(userId)), Arg.Any<CancellationToken>())
            .Returns(new List<UserLookupResultDto> { new() { Id = userId, FullName = fullName, Email = email } });

    [Fact]
    public async Task Issue262_list_returns_masked_email()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Add(IndependentPending(11));
            await ctx.SaveChangesAsync();
        }
        UserResolvesTo(11, "Ali Veli", "ali.veli@okul.k12.tr");

        await using var check = _db.NewContext();
        var item = (await NewService(check).GetPendingApplicationsAsync()).Single();

        item.FullName.ShouldBe("Ali Veli");
        item.Email.ShouldBe("a***@okul.k12.tr");
    }

    [Fact]
    public async Task Issue262_detail_returns_full_email_for_a_pending_application()
    {
        int teacherId;
        await using (var ctx = _db.NewContext())
        {
            var t = new Teacher { UserId = 12, IsIndependentTutor = false, ApprovalStatus = TeacherApprovalStatus.Pending };
            var school = new School { Name = "Konya Lisesi" };
            ctx.Schools.Add(school);
            await ctx.SaveChangesAsync();
            t.RequestedSchoolId = school.Id;
            ctx.Teachers.Add(t);
            await ctx.SaveChangesAsync();
            teacherId = t.Id;
        }
        UserResolvesTo(12, "Ayşe Yılmaz", "ayse@okul.k12.tr");

        await using var check = _db.NewContext();
        var detail = await NewService(check).GetPendingApplicationAsync(teacherId);

        detail.ShouldNotBeNull();
        detail.TeacherId.ShouldBe(teacherId);
        detail.UserId.ShouldBe(12);
        detail.FullName.ShouldBe("Ayşe Yılmaz");
        detail.Email.ShouldBe("ayse@okul.k12.tr");
        detail.IsIndependentTutor.ShouldBeFalse();
        detail.RequestedSchoolName.ShouldBe("Konya Lisesi");
    }

    [Fact]
    public async Task Issue262_detail_is_null_for_unknown_or_already_decided_applications()
    {
        int decidedId;
        await using (var ctx = _db.NewContext())
        {
            var decided = new Teacher { UserId = 13, IsIndependentTutor = true, ApprovalStatus = TeacherApprovalStatus.Approved };
            ctx.Teachers.Add(decided);
            await ctx.SaveChangesAsync();
            decidedId = decided.Id;
        }

        await using var check = _db.NewContext();
        (await NewService(check).GetPendingApplicationAsync(decidedId)).ShouldBeNull();
        (await NewService(check).GetPendingApplicationAsync(987_654)).ShouldBeNull();
        await _authApi.DidNotReceiveWithAnyArgs().GetUsersByIdsAsync(default!, default);
    }

    [Fact]
    public async Task Issue262_detail_survives_auth_api_outage_with_empty_name_and_email()
    {
        int teacherId;
        await using (var ctx = _db.NewContext())
        {
            var t = IndependentPending(14);
            ctx.Teachers.Add(t);
            await ctx.SaveChangesAsync();
            teacherId = t.Id;
        }
        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<UserLookupResultDto>>(new HttpRequestException("down")));

        await using var check = _db.NewContext();
        var detail = await NewService(check).GetPendingApplicationAsync(teacherId);

        detail.ShouldNotBeNull();
        detail.FullName.ShouldBe(string.Empty);
        detail.Email.ShouldBe(string.Empty);
    }

    // ---- GetPendingApplicationsAsync ----

    [Fact]
    public async Task GetPendingApplicationsAsync_ReturnsOnlyIndependentPendingTeachers()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.Teachers.Add(IndependentPending(1)); // eligible
            ctx.Teachers.Add(new Teacher { UserId = 2, IsIndependentTutor = true, ApprovalStatus = TeacherApprovalStatus.Approved });
            ctx.Teachers.Add(new Teacher { UserId = 3, IsIndependentTutor = true, ApprovalStatus = TeacherApprovalStatus.Rejected });
            ctx.Teachers.Add(new Teacher { UserId = 4, IsIndependentTutor = false, ApprovalStatus = TeacherApprovalStatus.Approved });
            // school-bound teacher somehow marked Pending should still be excluded (not independent)
            ctx.Teachers.Add(new Teacher { UserId = 5, IsIndependentTutor = false, ApprovalStatus = TeacherApprovalStatus.Pending });
            await ctx.SaveChangesAsync();
        }

        await using var check = _db.NewContext();
        var result = await NewService(check).GetPendingApplicationsAsync();

        result.Count.ShouldBe(1);
        result[0].UserId.ShouldBe(1);
    }

    [Fact]
    public async Task GetPendingApplicationsAsync_NoPendingApplications_ReturnsEmptyList()
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx).GetPendingApplicationsAsync();

        result.ShouldBeEmpty();
    }

    // ---- ApproveAsync ----

    [Fact]
    public async Task ApproveAsync_ValidPendingIndependentTeacher_SucceedsAndSetsApproved()
    {
        int teacherId;
        await using (var ctx = _db.NewContext())
        {
            var teacher = IndependentPending(10);
            ctx.Teachers.Add(teacher);
            await ctx.SaveChangesAsync();
            teacherId = teacher.Id;
        }

        await using var actCtx = _db.NewContext();
        var response = await NewService(actCtx).ApproveAsync(teacherId, AdminUserId, AdminSub);

        response.Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        var reloaded = await check.Teachers.SingleAsync(t => t.Id == teacherId);
        reloaded.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Approved);
    }

    [Fact]
    public async Task ApproveAsync_AlreadyApprovedTeacher_FailsWithConflictAndDoesNotChangeState()
    {
        int teacherId;
        await using (var ctx = _db.NewContext())
        {
            var teacher = new Teacher { UserId = 11, IsIndependentTutor = true, ApprovalStatus = TeacherApprovalStatus.Approved };
            ctx.Teachers.Add(teacher);
            await ctx.SaveChangesAsync();
            teacherId = teacher.Id;
        }

        await using var actCtx = _db.NewContext();
        var response = await NewService(actCtx).ApproveAsync(teacherId, AdminUserId, AdminSub);

        response.Success.ShouldBeFalse();
        response.Conflict.ShouldBeTrue();

        await using var check = _db.NewContext();
        (await check.Teachers.SingleAsync(t => t.Id == teacherId)).ApprovalStatus.ShouldBe(TeacherApprovalStatus.Approved);
    }

    [Fact]
    public async Task ApproveAsync_AlreadyRejectedTeacher_FailsWithConflictAndDoesNotChangeState()
    {
        int teacherId;
        await using (var ctx = _db.NewContext())
        {
            var teacher = new Teacher
            {
                UserId = 12, IsIndependentTutor = true,
                ApprovalStatus = TeacherApprovalStatus.Rejected, RejectionReason = "önceki neden"
            };
            ctx.Teachers.Add(teacher);
            await ctx.SaveChangesAsync();
            teacherId = teacher.Id;
        }

        await using var actCtx = _db.NewContext();
        var response = await NewService(actCtx).ApproveAsync(teacherId, AdminUserId, AdminSub);

        response.Success.ShouldBeFalse();
        response.Conflict.ShouldBeTrue();

        await using var check = _db.NewContext();
        var reloaded = await check.Teachers.SingleAsync(t => t.Id == teacherId);
        reloaded.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Rejected);
        reloaded.RejectionReason.ShouldBe("önceki neden");
    }

    [Fact]
    public async Task ApproveAsync_NonIndependentTeacherMarkedPending_Fails()
    {
        int teacherId;
        await using (var ctx = _db.NewContext())
        {
            var teacher = new Teacher { UserId = 13, IsIndependentTutor = false, ApprovalStatus = TeacherApprovalStatus.Pending };
            ctx.Teachers.Add(teacher);
            await ctx.SaveChangesAsync();
            teacherId = teacher.Id;
        }

        await using var actCtx = _db.NewContext();
        var response = await NewService(actCtx).ApproveAsync(teacherId, AdminUserId, AdminSub);

        response.Success.ShouldBeFalse();

        await using var check = _db.NewContext();
        (await check.Teachers.SingleAsync(t => t.Id == teacherId)).ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
    }

    [Fact]
    public async Task ApproveAsync_UnknownTeacherId_ReturnsNotFound()
    {
        await using var ctx = _db.NewContext();
        var response = await NewService(ctx).ApproveAsync(12345, AdminUserId, AdminSub);

        response.Success.ShouldBeFalse();
        response.NotFound.ShouldBeTrue();
    }

    // ---- RejectAsync ----

    [Fact]
    public async Task RejectAsync_ValidPendingTeacherWithReason_SucceedsAndStoresReason()
    {
        int teacherId;
        await using (var ctx = _db.NewContext())
        {
            var teacher = IndependentPending(20);
            ctx.Teachers.Add(teacher);
            await ctx.SaveChangesAsync();
            teacherId = teacher.Id;
        }

        await using var actCtx = _db.NewContext();
        var response = await NewService(actCtx).RejectAsync(teacherId, "Belge eksik", AdminUserId, AdminSub);

        response.Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        var reloaded = await check.Teachers.SingleAsync(t => t.Id == teacherId);
        reloaded.ApprovalStatus.ShouldBe(TeacherApprovalStatus.Rejected);
        reloaded.RejectionReason.ShouldBe("Belge eksik");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task RejectAsync_EmptyOrWhitespaceReason_FailsValidation(string? reason)
    {
        int teacherId;
        await using (var ctx = _db.NewContext())
        {
            var teacher = IndependentPending(21);
            ctx.Teachers.Add(teacher);
            await ctx.SaveChangesAsync();
            teacherId = teacher.Id;
        }

        await using var actCtx = _db.NewContext();
        var response = await NewService(actCtx).RejectAsync(teacherId, reason!, AdminUserId, AdminSub);

        response.Success.ShouldBeFalse();

        await using var check = _db.NewContext();
        (await check.Teachers.SingleAsync(t => t.Id == teacherId)).ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
    }

    [Fact]
    public async Task RejectAsync_AlreadyDecidedTeacher_FailsWithConflict()
    {
        int teacherId;
        await using (var ctx = _db.NewContext())
        {
            var teacher = new Teacher { UserId = 22, IsIndependentTutor = true, ApprovalStatus = TeacherApprovalStatus.Approved };
            ctx.Teachers.Add(teacher);
            await ctx.SaveChangesAsync();
            teacherId = teacher.Id;
        }

        await using var actCtx = _db.NewContext();
        var response = await NewService(actCtx).RejectAsync(teacherId, "gerekçe", AdminUserId, AdminSub);

        response.Success.ShouldBeFalse();
        response.Conflict.ShouldBeTrue();

        await using var check = _db.NewContext();
        (await check.Teachers.SingleAsync(t => t.Id == teacherId)).ApprovalStatus.ShouldBe(TeacherApprovalStatus.Approved);
    }

    [Fact]
    public async Task RejectAsync_ReasonExceedsMaxLength_FailsValidation()
    {
        int teacherId;
        await using (var ctx = _db.NewContext())
        {
            var teacher = IndependentPending(23);
            ctx.Teachers.Add(teacher);
            await ctx.SaveChangesAsync();
            teacherId = teacher.Id;
        }

        var tooLong = new string('a', 501);

        await using var actCtx = _db.NewContext();
        var response = await NewService(actCtx).RejectAsync(teacherId, tooLong, AdminUserId, AdminSub);

        response.Success.ShouldBeFalse();

        await using var check = _db.NewContext();
        (await check.Teachers.SingleAsync(t => t.Id == teacherId)).ApprovalStatus.ShouldBe(TeacherApprovalStatus.Pending);
    }

    // ---- issue #157: karar bildirimi outbox + admin karar audit'i ----

    [Fact]
    public async Task ApproveAsync_SubResolved_WritesOutboxEventWithoutReasonOrAdminIdentity()
    {
        int teacherId;
        await using (var ctx = _db.NewContext())
        {
            var teacher = IndependentPending(30);
            ctx.Teachers.Add(teacher);
            await ctx.SaveChangesAsync();
            teacherId = teacher.Id;
        }
        UserResolvesToSub(30, "kc-teacher-30");

        await using var actCtx = _db.NewContext();
        var response = await NewService(actCtx).ApproveAsync(teacherId, AdminUserId, AdminSub);
        response.Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        var outbox = await check.OutboxMessages
            .SingleAsync(m => m.Type == OutboxEventRegistry.NameFor<TeacherApplicationDecidedEvent>());
        var payload = System.Text.Json.JsonSerializer.Deserialize<TeacherApplicationDecidedEvent>(outbox.Content)!;

        payload.TeacherId.ShouldBe(teacherId);
        payload.TargetKeycloakId.ShouldBe("kc-teacher-30");
        payload.Approved.ShouldBeTrue();
        payload.EventId.ShouldNotBe(Guid.Empty);

        // Güvenlik kararı (issue #157): payload'da ret gerekçesi/admin kimliği yok — TeacherApplicationDecidedEvent
        // tipinde zaten böyle bir alan tanımlı değil; serileştirilmiş içerik de sadece beklenen alanları taşır.
        outbox.Content.ShouldNotContain("Reason");
        outbox.Content.ShouldNotContain(AdminSub);
    }

    [Fact]
    public async Task RejectAsync_SubResolved_WritesOutboxEventWithoutReasonOrAdminIdentity()
    {
        int teacherId;
        await using (var ctx = _db.NewContext())
        {
            var teacher = IndependentPending(31);
            ctx.Teachers.Add(teacher);
            await ctx.SaveChangesAsync();
            teacherId = teacher.Id;
        }
        UserResolvesToSub(31, "kc-teacher-31");

        await using var actCtx = _db.NewContext();
        var response = await NewService(actCtx).RejectAsync(teacherId, "Belge eksik", AdminUserId, AdminSub);
        response.Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        var outbox = await check.OutboxMessages
            .SingleAsync(m => m.Type == OutboxEventRegistry.NameFor<TeacherApplicationDecidedEvent>());
        var payload = System.Text.Json.JsonSerializer.Deserialize<TeacherApplicationDecidedEvent>(outbox.Content)!;

        payload.TeacherId.ShouldBe(teacherId);
        payload.TargetKeycloakId.ShouldBe("kc-teacher-31");
        payload.Approved.ShouldBeFalse();

        outbox.Content.ShouldNotContain("Belge eksik");
        outbox.Content.ShouldNotContain(AdminSub);
    }

    [Fact]
    public async Task ApproveAsync_SubLookupFails_CommitsDecisionButSkipsOutbox()
    {
        int teacherId;
        await using (var ctx = _db.NewContext())
        {
            var teacher = IndependentPending(32);
            ctx.Teachers.Add(teacher);
            await ctx.SaveChangesAsync();
            teacherId = teacher.Id;
        }

        _authApi.GetUsersByIdsAsync(Arg.Any<IEnumerable<int>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<IReadOnlyList<UserLookupResultDto>>(new HttpRequestException("auth-api down")));

        await using var actCtx = _db.NewContext();
        var response = await NewService(actCtx).ApproveAsync(teacherId, AdminUserId, AdminSub);
        response.Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        (await check.Teachers.SingleAsync(t => t.Id == teacherId)).ApprovalStatus.ShouldBe(TeacherApprovalStatus.Approved);
        (await check.OutboxMessages.CountAsync(m => m.Type == OutboxEventRegistry.NameFor<TeacherApplicationDecidedEvent>()))
            .ShouldBe(0);
    }

    [Fact]
    public async Task RejectAsync_SubLookupReturnsEmpty_CommitsDecisionButSkipsOutbox()
    {
        int teacherId;
        await using (var ctx = _db.NewContext())
        {
            var teacher = IndependentPending(33);
            ctx.Teachers.Add(teacher);
            await ctx.SaveChangesAsync();
            teacherId = teacher.Id;
        }
        UserResolvesToSub(33, sub: null); // lookup succeeds but no KeycloakId

        await using var actCtx = _db.NewContext();
        var response = await NewService(actCtx).RejectAsync(teacherId, "gerekçe", AdminUserId, AdminSub);
        response.Success.ShouldBeTrue();

        await using var check = _db.NewContext();
        (await check.Teachers.SingleAsync(t => t.Id == teacherId)).ApprovalStatus.ShouldBe(TeacherApprovalStatus.Rejected);
        (await check.OutboxMessages.CountAsync(m => m.Type == OutboxEventRegistry.NameFor<TeacherApplicationDecidedEvent>()))
            .ShouldBe(0);
    }

    [Fact]
    public async Task ApproveAsync_WithActorSub_RecordsAuditAsTeacherApproved()
    {
        int teacherId;
        await using (var ctx = _db.NewContext())
        {
            var teacher = IndependentPending(34);
            ctx.Teachers.Add(teacher);
            await ctx.SaveChangesAsync();
            teacherId = teacher.Id;
        }
        UserResolvesToSub(34, "kc-teacher-34");

        await using var actCtx = _db.NewContext();
        var response = await NewServiceWithAudit(actCtx).ApproveAsync(teacherId, AdminUserId, AdminSub);
        response.Success.ShouldBeTrue();

        await _audit.Received(1).TryRecordAsync(
            Arg.Is<AdminUserActionRecord>(r =>
                r.ActorKeycloakId == AdminSub &&
                r.Action == AdminUserAction.TeacherApproved &&
                r.TargetType == AdminUserTargetType.Teacher &&
                r.TargetId == teacherId),
            AdminUserActionOutcome.Succeeded);
    }

    [Fact]
    public async Task RejectAsync_WithActorSub_RecordsAuditAsTeacherRejected()
    {
        int teacherId;
        await using (var ctx = _db.NewContext())
        {
            var teacher = IndependentPending(35);
            ctx.Teachers.Add(teacher);
            await ctx.SaveChangesAsync();
            teacherId = teacher.Id;
        }
        UserResolvesToSub(35, "kc-teacher-35");

        await using var actCtx = _db.NewContext();
        var response = await NewServiceWithAudit(actCtx).RejectAsync(teacherId, "gerekçe", AdminUserId, AdminSub);
        response.Success.ShouldBeTrue();

        await _audit.Received(1).TryRecordAsync(
            Arg.Is<AdminUserActionRecord>(r =>
                r.ActorKeycloakId == AdminSub &&
                r.Action == AdminUserAction.TeacherRejected &&
                r.TargetType == AdminUserTargetType.Teacher &&
                r.TargetId == teacherId),
            AdminUserActionOutcome.Succeeded);
    }

    [Fact]
    public async Task ApproveAsync_BlankActorSub_DoesNotRecordAudit()
    {
        // issue #157 review: interface artık actorAdminKeycloakId'yi ZORUNLU kılıyor (controller KeyCloakId
        // yoksa zaten 403 döner) ama TryAuditDecisionAsync yine de boş/whitespace değere karşı savunmacı
        // kalır — bu, "her zaman dolu gelir" varsayımının kırılması durumunda audit'in sessizce NRE atmak
        // yerine no-op olmasını sağlar.
        int teacherId;
        await using (var ctx = _db.NewContext())
        {
            var teacher = IndependentPending(36);
            ctx.Teachers.Add(teacher);
            await ctx.SaveChangesAsync();
            teacherId = teacher.Id;
        }
        UserResolvesToSub(36, "kc-teacher-36");

        await using var actCtx = _db.NewContext();
        var response = await NewServiceWithAudit(actCtx).ApproveAsync(teacherId, AdminUserId, "   ");
        response.Success.ShouldBeTrue();

        await _audit.DidNotReceive().TryRecordAsync(Arg.Any<AdminUserActionRecord>(), Arg.Any<AdminUserActionOutcome>());
    }

    public void Dispose() => _db.Dispose();
}
