using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #156: admin hesap aksiyonu audit'i — satır + sonuç güncellemesi, string enum'lar. Issue #155: #155/#156 servislerinin
/// ortak best-effort yardımcıları (TryRecordAsync / TryUpdateOutcomeAsync) hatayı yutar ve loglar.
/// </summary>
public class AdminUserActionAuditServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Record_then_update_outcome()
    {
        var before = DateTime.UtcNow;
        long id;
        await using (var ctx = _db.NewContext())
        {
            var service = new AdminUserActionAuditService(ctx);
            id = await service.RecordAsync(
                new AdminUserActionRecord(" kc-admin ", AdminUserAction.PasswordReset, AdminUserTargetType.Student, 9),
                AdminUserActionOutcome.Requested);
            await service.UpdateOutcomeAsync(id, AdminUserActionOutcome.SessionRevokeFailed);
        }

        await using var read = _db.NewContext();
        var row = await read.AdminUserActionLogs.SingleAsync();
        row.Id.ShouldBe(id);
        row.ActorKeycloakId.ShouldBe("kc-admin");
        row.TargetType.ShouldBe(AdminUserTargetType.Student);
        row.TargetId.ShouldBe(9);
        row.Outcome.ShouldBe(AdminUserActionOutcome.SessionRevokeFailed);
        row.OccurredAtUtc.ShouldBeGreaterThanOrEqualTo(before.AddSeconds(-1));
        (await read.Database.SqlQueryRaw<string>("SELECT \"Outcome\" AS \"Value\" FROM \"AdminUserActionLogs\"").SingleAsync())
            .ShouldBe("SessionRevokeFailed");
    }

    [Fact]
    public async Task Missing_actor_is_rejected_and_unknown_row_update_throws()
    {
        await using var ctx = _db.NewContext();
        var service = new AdminUserActionAuditService(ctx);

        await Should.ThrowAsync<InvalidOperationException>(() => service.RecordAsync(
            new AdminUserActionRecord(" ", AdminUserAction.PasswordReset, AdminUserTargetType.Teacher, 1), AdminUserActionOutcome.Requested));
        await Should.ThrowAsync<InvalidOperationException>(() => service.UpdateOutcomeAsync(12345, AdminUserActionOutcome.Succeeded));
    }

    [Fact]
    public async Task TryRecord_writes_row_and_swallows_and_logs_failures()
    {
        var logger = new CapturingLogger<AdminUserActionAuditService>();
        await using (var ctx = _db.NewContext())
        {
            var service = new AdminUserActionAuditService(ctx, logger);
            await service.TryRecordAsync(
                new AdminUserActionRecord("kc-admin", AdminUserAction.AccountDisabled, AdminUserTargetType.Teacher, 3),
                AdminUserActionOutcome.Denied);

            // Aktör boş → RecordAsync fırlatır; Try* yutar.
            await Should.NotThrowAsync(() => service.TryRecordAsync(
                new AdminUserActionRecord(" ", AdminUserAction.AccountDisabled, AdminUserTargetType.Teacher, 3),
                AdminUserActionOutcome.Denied));
        }

        await using var read = _db.NewContext();
        var row = await read.AdminUserActionLogs.SingleAsync();
        row.Action.ShouldBe(AdminUserAction.AccountDisabled);
        row.Outcome.ShouldBe(AdminUserActionOutcome.Denied);
        logger.Entries.ShouldContain(e => e.Contains("Audit yazılamadı"));
    }

    [Fact]
    public async Task TryUpdateOutcome_swallows_and_logs_failures()
    {
        var logger = new CapturingLogger<AdminUserActionAuditService>();
        await using var ctx = _db.NewContext();
        var service = new AdminUserActionAuditService(ctx, logger);

        await Should.NotThrowAsync(() => service.TryUpdateOutcomeAsync(12345, AdminUserActionOutcome.Succeeded));

        logger.Entries.ShouldContain(e => e.Contains("Audit sonucu güncellenemedi"));
    }
}
