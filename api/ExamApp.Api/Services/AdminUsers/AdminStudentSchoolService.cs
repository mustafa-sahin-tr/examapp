using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Services.AdminUsers;

/// <summary>
/// issue #277 (madde 8). <see cref="AdminAccountStatusService"/> (#155) ile aynı hedef çözümü, audit ve hata deseni.
/// <para>
/// Yan etkiler (bilinçli, veri silinmez):
/// <list type="bullet">
/// <item>Sınıf hedefli atamalar (<c>WorksheetStudentAccess</c>) okul eşitliğiyle değerlendirildiği için öğrenci eski
/// okulun sınıf atamalarını HEMEN görmez olur, yeni okulunkileri görür; platform geneli atamalar değişmez.</item>
/// <item>Öğrenciye DOĞRUDAN yapılmış atamalar (StudentId) ve geçmiş test oturumları korunur — öğrenci onları görmeye
/// devam eder. Eski okulun öğretmenleri ise okul kapsamı (<c>ISchoolAccessPolicy</c>) nedeniyle öğrenciyi artık
/// listelerinde/ilerleme görünümlerinde görmez; yeni okulun öğretmenleri görür.</item>
/// <item>Bağımsız öğretmen Booking'leri okuldan bağımsızdır, etkilenmez.</item>
/// <item>Kullanıcının öğretmen kaydı da varsa (eski çift kayıt) okul kapsamı Teachers.SchoolId'den gelir
/// (<c>SchoolContextResolver</c>); bu değişiklik o kapsamı değiştirmez.</item>
/// </list>
/// </para>
/// Loglar yalnızca öğrenci id'si, okul id'leri ve aktör sub'ını içerir (PII yok).
/// </summary>
public class AdminStudentSchoolService : IAdminStudentSchoolService
{
    private readonly AppDbContext _context;
    private readonly IAdminAccountTargetResolver _targets;
    private readonly IAdminUserActionAuditService _audit;
    private readonly IKeycloakService? _keycloak;
    private readonly UserProfileCacheService? _profileCache;
    private readonly ILogger<AdminStudentSchoolService> _logger;

    public AdminStudentSchoolService(
        AppDbContext context,
        IAdminAccountTargetResolver targets,
        IAdminUserActionAuditService audit,
        IKeycloakService? keycloak = null,
        UserProfileCacheService? profileCache = null,
        ILogger<AdminStudentSchoolService>? logger = null)
    {
        _context = context;
        _targets = targets;
        _audit = audit;
        _keycloak = keycloak;
        _profileCache = profileCache;
        _logger = logger ?? NullLogger<AdminStudentSchoolService>.Instance;
    }

    public async Task<AdminStudentSchoolChangeResult> ChangeSchoolAsync(
        int studentId, int schoolId, string actorKeycloakId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actorKeycloakId))
            throw new InvalidOperationException("Admin student school change requires the actor's Keycloak subject.");

        var record = new AdminUserActionRecord(actorKeycloakId, AdminUserAction.StudentSchoolChanged,
            AdminUserTargetType.Student, studentId);

        // Soft-delete edilmiş öğrenci global filtre ile dışarıda → 404.
        var current = await _context.Students.AsNoTracking()
            .Where(s => s.Id == studentId)
            .Select(s => new { s.SchoolId })
            .FirstOrDefaultAsync(ct);
        if (current is null)
        {
            await _audit.TryRecordAsync(record, AdminUserActionOutcome.NotFound);
            return new(AdminStudentSchoolChangeStatus.TargetNotFound);
        }

        if (!await _context.Schools.AsNoTracking().AnyAsync(s => s.Id == schoolId, ct))
            return new(AdminStudentSchoolChangeStatus.SchoolNotFound);

        var previousSchoolId = current.SchoolId;
        if (previousSchoolId == schoolId)
            return new(AdminStudentSchoolChangeStatus.Success, previousSchoolId, schoolId, Changed: false);

        var target = await _targets.ResolveAsync(AdminUserTargetType.Student, studentId, actorKeycloakId, ct);
        switch (target.Status)
        {
            case AdminAccountTargetStatus.TargetNotFound:
                await _audit.TryRecordAsync(record, AdminUserActionOutcome.NotFound);
                return new(AdminStudentSchoolChangeStatus.TargetNotFound);
            case AdminAccountTargetStatus.AccountNotFound:
                await _audit.TryRecordAsync(record, AdminUserActionOutcome.NotFound);
                return new(AdminStudentSchoolChangeStatus.AccountNotFound);
            case AdminAccountTargetStatus.ForbiddenSelf:
                _logger.LogWarning("[AdminStudentSchool] Admin kendi öğrenci kaydının okulunu değiştirmeye çalıştı: actor={Actor}", actorKeycloakId);
                await _audit.TryRecordAsync(record, AdminUserActionOutcome.Denied);
                return new(AdminStudentSchoolChangeStatus.ForbiddenSelf);
            case AdminAccountTargetStatus.ForbiddenProtectedRole:
                _logger.LogWarning("[AdminStudentSchool] Korumalı hedef reddedildi: Student#{StudentId} actor={Actor}", studentId, actorKeycloakId);
                await _audit.TryRecordAsync(record, AdminUserActionOutcome.Denied);
                return new(AdminStudentSchoolChangeStatus.ForbiddenProtectedRole);
            case AdminAccountTargetStatus.UpstreamFailure:
                return new(AdminStudentSchoolChangeStatus.UpstreamFailure);
        }

        var sub = target.KeycloakId!;

        // Fail-closed: iz yazılamazsa istisna yukarı çıkar (500), okul değişmez.
        var auditId = await _audit.RecordAsync(record, AdminUserActionOutcome.Requested, ct);

        // Bu noktadan sonra istemci iptali akışı yarıda bırakmasın (DB değişip önbellek eski okulla kalmasın).
        // Koşullu güncelleme: okunduğu andaki okul hâlâ aynıysa (eşzamanlı admin değişikliği / öğrencinin ilk okul ataması
        // #259 ile yarışta son yazan kazanmasın). ExecuteUpdate SaveChanges audit'ini atlar; UpdateTime açıkça yazılır.
        var now = DateTime.UtcNow;
        var affected = await _context.Students
            .Where(s => s.Id == studentId && s.SchoolId == previousSchoolId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.SchoolId, schoolId)
                .SetProperty(s => s.UpdateTime, now), CancellationToken.None);

        if (affected == 0)
        {
            _logger.LogWarning("[AdminStudentSchool] Eşzamanlı değişiklik: Student#{StudentId} okulu okuma/yazma arasında değişti", studentId);
            await _audit.TryUpdateOutcomeAsync(auditId, AdminUserActionOutcome.Conflict);
            return new(AdminStudentSchoolChangeStatus.Conflict);
        }

        // Okul kapsamı (SchoolScope) profil önbelleğinden gelir (#194): düşürülmezse öğrenci önbellek süresi (1 saat) boyunca
        // ESKİ okulun kapsamında kalırdı. Best-effort: Redis/Keycloak hatası DB değişikliğini geri almaz; loglanır.
        await TryInvalidateProfileAsync(sub, studentId);
        await TrySyncSchoolClaimAsync(sub, schoolId, studentId);

        await _audit.TryUpdateOutcomeAsync(auditId, AdminUserActionOutcome.Succeeded);
        _logger.LogInformation("[AdminStudentSchool] Öğrenci okulu değişti: Student#{StudentId} {PreviousSchoolId} → {SchoolId} actor={Actor}",
            studentId, previousSchoolId, schoolId, actorKeycloakId);
        return new(AdminStudentSchoolChangeStatus.Success, previousSchoolId, schoolId, Changed: true);
    }

    private async Task TryInvalidateProfileAsync(string sub, int studentId)
    {
        if (_profileCache is null)
            return;
        try
        {
            await _profileCache.RemoveAsync(sub);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[AdminStudentSchool] Profil önbelleği düşürülemedi (eski okul en geç 1 saat sürebilir): Student#{StudentId}", studentId);
        }
    }

    private async Task TrySyncSchoolClaimAsync(string sub, int schoolId, int studentId)
    {
        if (_keycloak is null)
            return;
        try
        {
            // JWT "school_id" yalnızca ipucudur (#189); yetki DB'den çözülür. Yine de uyuşmazlık uyarısı üretmesin.
            await _keycloak.SetSchoolIdAttributeAsync(sub, schoolId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[AdminStudentSchool] Keycloak school_id attribute güncellenemedi: Student#{StudentId}", studentId);
        }
    }
}
