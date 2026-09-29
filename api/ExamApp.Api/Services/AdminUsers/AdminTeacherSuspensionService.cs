using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Admin;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Services.AdminUsers;

/// <summary>
/// issue #289. <see cref="AdminStudentSchoolService"/> (#277) ile aynı audit ve koşullu güncelleme deseni. Keycloak'a
/// gidilmez (hesap devre dışı bırakma #155 ayrı akış) — bu yüzden hedef çözücü (<c>IAdminAccountTargetResolver</c>) ve
/// upstream hata yolu yoktur. Öğretmen yetkisi DB'den (<c>IApprovedTeacherGuard</c>) her istekte okunduğu için profil
/// önbelleğini düşürmek gerekmez; askı bir sonraki istekte etkilidir.
/// Loglar yalnızca Teacher.Id ve aktör sub'ını içerir; neden (serbest metin) loglanmaz ve audit'e yazılmaz.
/// </summary>
public class AdminTeacherSuspensionService : IAdminTeacherSuspensionService
{
    public const int ReasonMaxLength = 500;

    private readonly AppDbContext _context;
    private readonly IAdminUserActionAuditService _audit;
    private readonly ILogger<AdminTeacherSuspensionService> _logger;

    public AdminTeacherSuspensionService(
        AppDbContext context,
        IAdminUserActionAuditService audit,
        ILogger<AdminTeacherSuspensionService>? logger = null)
    {
        _context = context;
        _audit = audit;
        _logger = logger ?? NullLogger<AdminTeacherSuspensionService>.Instance;
    }

    public async Task<AdminTeacherSuspensionResult> SuspendAsync(
        int teacherId, string? reason, string actorKeycloakId, CancellationToken ct = default)
    {
        RequireActor(actorKeycloakId);

        var trimmedReason = reason?.Trim();
        if (string.IsNullOrEmpty(trimmedReason))
            return new(AdminTeacherSuspensionStatus.ReasonRequired);
        if (trimmedReason.Length > ReasonMaxLength)
            return new(AdminTeacherSuspensionStatus.ReasonTooLong);

        var record = new AdminUserActionRecord(actorKeycloakId, AdminUserAction.TeacherSuspended,
            AdminUserTargetType.Teacher, teacherId);

        var current = await ReadStateAsync(teacherId, ct);
        if (current is null)
        {
            await _audit.TryRecordAsync(record, AdminUserActionOutcome.NotFound);
            return new(AdminTeacherSuspensionStatus.TargetNotFound);
        }

        if (current.AccountSuspendedAt is not null)
        {
            await _audit.TryRecordAsync(record, AdminUserActionOutcome.Conflict);
            return new(AdminTeacherSuspensionStatus.AlreadySuspended, current.AccountApprovedAt, current.AccountSuspendedAt);
        }

        if (current.AccountApprovedAt is null)
        {
            // Hesabı hiç onaylanmamış öğretmen zaten kapalı; onay bekleyen başvurusu varsa ret akışı (#157) kullanılır.
            await _audit.TryRecordAsync(record, AdminUserActionOutcome.Conflict);
            return new(AdminTeacherSuspensionStatus.AccountNotApproved);
        }

        // Fail-closed: iz yazılamazsa istisna yukarı çıkar (500), askıya alınmaz.
        var auditId = await _audit.RecordAsync(record, AdminUserActionOutcome.Requested, ct);

        // Koşullu güncelleme: okunduğu an hâlâ "onaylı ve askıda değil" ise. Eşzamanlı askıya alma/açma ya da silme 0 satır
        // üretir → Conflict. ExecuteUpdate SaveChanges audit'ini atlar; UpdateTime açıkça yazılır. Bu noktadan sonra istemci
        // iptali akışı yarıda bırakmasın (DB değişip audit Requested'da kalmasın).
        var now = DateTime.UtcNow;
        var affected = await _context.Teachers
            .Where(t => t.Id == teacherId && t.AccountApprovedAt != null && t.AccountSuspendedAt == null)
            .ExecuteUpdateAsync(set => set
                .SetProperty(t => t.AccountApprovedAt, (DateTime?)null)
                .SetProperty(t => t.AccountSuspendedAt, now)
                .SetProperty(t => t.AccountSuspensionReason, trimmedReason)
                .SetProperty(t => t.UpdateTime, now), CancellationToken.None);

        if (affected == 0)
        {
            _logger.LogWarning("[AdminTeacherSuspension] Eşzamanlı değişiklik: Teacher#{TeacherId} askıya alınamadı", teacherId);
            await _audit.TryUpdateOutcomeAsync(auditId, AdminUserActionOutcome.Conflict);
            return new(AdminTeacherSuspensionStatus.Conflict);
        }

        await _audit.TryUpdateOutcomeAsync(auditId, AdminUserActionOutcome.Succeeded);
        _logger.LogInformation("[AdminTeacherSuspension] Öğretmen hesap onayı askıya alındı: Teacher#{TeacherId} actor={Actor}",
            teacherId, actorKeycloakId);
        return new(AdminTeacherSuspensionStatus.Success, AccountApprovedAt: null, AccountSuspendedAt: now);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <c>AccountApprovedAt = now</c> yazılır: ilk hesap onayı tarihi (#287) askıya almada silinmişti ve bilinçli olarak geri
    /// getirilmez (ilk onay anı <c>AdminUserActionLogs</c>'taki <c>TeacherApproved</c> kaydındadır).
    /// </remarks>
    public async Task<AdminTeacherSuspensionResult> UnsuspendAsync(
        int teacherId, string actorKeycloakId, CancellationToken ct = default)
    {
        RequireActor(actorKeycloakId);

        var record = new AdminUserActionRecord(actorKeycloakId, AdminUserAction.TeacherUnsuspended,
            AdminUserTargetType.Teacher, teacherId);

        var current = await ReadStateAsync(teacherId, ct);
        if (current is null)
        {
            await _audit.TryRecordAsync(record, AdminUserActionOutcome.NotFound);
            return new(AdminTeacherSuspensionStatus.TargetNotFound);
        }

        if (current.AccountSuspendedAt is null)
        {
            await _audit.TryRecordAsync(record, AdminUserActionOutcome.Conflict);
            return new(AdminTeacherSuspensionStatus.NotSuspended, current.AccountApprovedAt);
        }

        var auditId = await _audit.RecordAsync(record, AdminUserActionOutcome.Requested, ct);

        var now = DateTime.UtcNow;
        var affected = await _context.Teachers
            .Where(t => t.Id == teacherId && t.AccountSuspendedAt != null)
            .ExecuteUpdateAsync(set => set
                .SetProperty(t => t.AccountApprovedAt, now)
                .SetProperty(t => t.AccountSuspendedAt, (DateTime?)null)
                .SetProperty(t => t.AccountSuspensionReason, (string?)null)
                .SetProperty(t => t.UpdateTime, now), CancellationToken.None);

        if (affected == 0)
        {
            _logger.LogWarning("[AdminTeacherSuspension] Eşzamanlı değişiklik: Teacher#{TeacherId} askısı kaldırılamadı", teacherId);
            await _audit.TryUpdateOutcomeAsync(auditId, AdminUserActionOutcome.Conflict);
            return new(AdminTeacherSuspensionStatus.Conflict);
        }

        await _audit.TryUpdateOutcomeAsync(auditId, AdminUserActionOutcome.Succeeded);
        _logger.LogInformation("[AdminTeacherSuspension] Öğretmen hesap onayının askısı kaldırıldı: Teacher#{TeacherId} actor={Actor}",
            teacherId, actorKeycloakId);
        return new(AdminTeacherSuspensionStatus.Success, AccountApprovedAt: now, AccountSuspendedAt: null);
    }

    private sealed record TeacherState(DateTime? AccountApprovedAt, DateTime? AccountSuspendedAt);

    /// <summary>Soft-delete edilmiş öğretmen global filtre ile dışarıda → null (404).</summary>
    private Task<TeacherState?> ReadStateAsync(int teacherId, CancellationToken ct) =>
        _context.Teachers.AsNoTracking()
            .Where(t => t.Id == teacherId)
            .Select(t => new TeacherState(t.AccountApprovedAt, t.AccountSuspendedAt))
            .FirstOrDefaultAsync(ct)!;

    private static void RequireActor(string actorKeycloakId)
    {
        if (string.IsNullOrWhiteSpace(actorKeycloakId))
            throw new InvalidOperationException("Admin teacher suspension requires the actor's Keycloak subject.");
    }
}
