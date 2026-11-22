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

public enum AdminTeacherSchoolAdminStatus
{
    Success,
    TargetNotFound,
    /// <summary>Okulu olmayan öğretmen (bağımsız, okul talebi bekleyen ya da okulsuz).</summary>
    NoSchool,
    AccountNotApproved,
    AccountSuspended,
    Conflict
}

public sealed record AdminTeacherSchoolAdminResult(
    AdminTeacherSchoolAdminStatus Status, bool IsSchoolAdmin = false, int? SchoolId = null, bool Changed = false);

/// <summary>
/// issue #1276 (#1071 S1): platform admini onaylı okul öğretmenine okul yöneticisi yetkisi verir / kaldırır.
/// </summary>
public interface IAdminTeacherSchoolAdminService
{
    Task<AdminTeacherSchoolAdminResult> SetAsync(int teacherId, bool isSchoolAdmin, string actorKeycloakId, CancellationToken ct = default);
}

/// <summary>
/// Yetki vermek yalnız okulu dolu, hesabı onaylı, askısız, bağımsız olmayan öğretmende olur (aksi 409, audit <c>Denied</c>).
/// Kaldırmak her durumda serbesttir. Aynı değer yan etkisizdir (<c>changed=false</c>, audit yok). Yazım koşulludur: okunduğu
/// andaki okul / hesap durumu değiştiyse <c>Conflict</c>. Keycloak'a dokunulmaz — yetki DB'den çözülür
/// (<see cref="ISchoolAdminResolver"/>).
/// </summary>
public class AdminTeacherSchoolAdminService : IAdminTeacherSchoolAdminService
{
    private readonly AppDbContext _context;
    private readonly IAdminUserActionAuditService _audit;
    private readonly ILogger<AdminTeacherSchoolAdminService> _logger;

    public AdminTeacherSchoolAdminService(AppDbContext context, IAdminUserActionAuditService audit,
        ILogger<AdminTeacherSchoolAdminService>? logger = null)
    {
        _context = context;
        _audit = audit;
        _logger = logger ?? NullLogger<AdminTeacherSchoolAdminService>.Instance;
    }

    public async Task<AdminTeacherSchoolAdminResult> SetAsync(
        int teacherId, bool isSchoolAdmin, string actorKeycloakId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actorKeycloakId))
            throw new InvalidOperationException("Granting school admin requires the actor's Keycloak subject.");

        var action = isSchoolAdmin ? AdminUserAction.SchoolAdminGranted : AdminUserAction.SchoolAdminRevoked;
        var record = new AdminUserActionRecord(actorKeycloakId, action, AdminUserTargetType.Teacher, teacherId);

        var current = await _context.Teachers.AsNoTracking()
            .Where(t => t.Id == teacherId)
            .Select(t => new { t.SchoolId, t.IsIndependentTutor, t.AccountApprovedAt, t.AccountSuspendedAt, t.IsSchoolAdmin })
            .FirstOrDefaultAsync(ct);
        if (current is null)
        {
            await _audit.TryRecordAsync(record, AdminUserActionOutcome.NotFound);
            return new(AdminTeacherSchoolAdminStatus.TargetNotFound);
        }

        record = isSchoolAdmin ? record with { ToSchoolId = current.SchoolId } : record with { FromSchoolId = current.SchoolId };

        if (current.IsSchoolAdmin == isSchoolAdmin)
            return new(AdminTeacherSchoolAdminStatus.Success, isSchoolAdmin, current.SchoolId, Changed: false);

        if (isSchoolAdmin)
        {
            AdminTeacherSchoolAdminStatus? blocked =
                current.AccountSuspendedAt != null ? AdminTeacherSchoolAdminStatus.AccountSuspended
                : current.AccountApprovedAt == null ? AdminTeacherSchoolAdminStatus.AccountNotApproved
                : current.SchoolId == null || current.IsIndependentTutor ? AdminTeacherSchoolAdminStatus.NoSchool
                : null;
            if (blocked is { } blockedStatus)
            {
                await _audit.TryRecordAsync(record, AdminUserActionOutcome.Denied);
                return new(blockedStatus, false, current.SchoolId);
            }
        }

        var auditId = await _audit.RecordAsync(record, AdminUserActionOutcome.Requested, ct);
        var now = DateTime.UtcNow;
        var schoolId = current.SchoolId;
        int affected;
        if (isSchoolAdmin)
        {
            affected = await _context.Teachers
                .Where(t => t.Id == teacherId && !t.IsSchoolAdmin && t.SchoolId == schoolId && !t.IsIndependentTutor
                    && t.AccountApprovedAt != null && t.AccountSuspendedAt == null)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(t => t.IsSchoolAdmin, true)
                    .SetProperty(t => t.SchoolAdminGrantedAt, now)
                    .SetProperty(t => t.UpdateTime, now), CancellationToken.None);
        }
        else
        {
            affected = await _context.Teachers
                .Where(t => t.Id == teacherId && t.IsSchoolAdmin)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(t => t.IsSchoolAdmin, false)
                    .SetProperty(t => t.SchoolAdminGrantedAt, (DateTime?)null)
                    .SetProperty(t => t.UpdateTime, now), CancellationToken.None);
        }

        if (affected == 0)
        {
            await _audit.TryUpdateOutcomeAsync(auditId, AdminUserActionOutcome.Conflict);
            return new(AdminTeacherSchoolAdminStatus.Conflict);
        }

        await _audit.TryUpdateOutcomeAsync(auditId, AdminUserActionOutcome.Succeeded);
        _logger.LogInformation("[AdminSchoolAdmin] Teacher#{TeacherId} school admin={IsSchoolAdmin} school={SchoolId} actor={Actor}",
            teacherId, isSchoolAdmin, schoolId, actorKeycloakId);
        return new(AdminTeacherSchoolAdminStatus.Success, isSchoolAdmin, schoolId, Changed: true);
    }
}
