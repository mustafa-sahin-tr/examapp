using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.Interfaces;
using Hangfire;
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
    private readonly IBackgroundJobClient? _jobs;
    private readonly ILogger<AdminStudentSchoolService> _logger;

    public AdminStudentSchoolService(
        AppDbContext context,
        IAdminAccountTargetResolver targets,
        IAdminUserActionAuditService audit,
        IKeycloakService? keycloak = null,
        UserProfileCacheService? profileCache = null,
        ILogger<AdminStudentSchoolService>? logger = null,
        IBackgroundJobClient? jobs = null)
    {
        _context = context;
        _targets = targets;
        _audit = audit;
        _keycloak = keycloak;
        _profileCache = profileCache;
        _jobs = jobs;
        _logger = logger ?? NullLogger<AdminStudentSchoolService>.Instance;
    }

    public async Task<AdminStudentSchoolChangeResult> ChangeSchoolAsync(
        int studentId, int schoolId, string actorKeycloakId, int? actorUserId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actorKeycloakId))
            throw new InvalidOperationException("Admin student school change requires the actor's Keycloak subject.");

        // issue #277 review (security L5): audit satırı istenen okulu (ToSchoolId) her sonuçta, önceki okulu (FromSchoolId)
        // öğrenci okunduktan sonra taşır.
        var record = new AdminUserActionRecord(actorKeycloakId, AdminUserAction.StudentSchoolChanged,
            AdminUserTargetType.Student, studentId, ToSchoolId: schoolId);

        // Soft-delete edilmiş öğrenci global filtre ile dışarıda → 404.
        var current = await _context.Students.AsNoTracking()
            .Where(s => s.Id == studentId)
            .Select(s => new { s.SchoolId, s.SchoolVerifiedAt })
            .FirstOrDefaultAsync(ct);
        if (current is null)
        {
            await _audit.TryRecordAsync(record, AdminUserActionOutcome.NotFound);
            return new(AdminStudentSchoolChangeStatus.TargetNotFound);
        }

        var previousSchoolId = current.SchoolId;
        record = record with { FromSchoolId = previousSchoolId };

        if (!await _context.Schools.AsNoTracking().AnyAsync(s => s.Id == schoolId, ct))
        {
            await _audit.TryRecordAsync(record, AdminUserActionOutcome.SchoolNotFound);
            return new(AdminStudentSchoolChangeStatus.SchoolNotFound);
        }

        // issue #361: admin ataması üyeliği HEMEN doğrular. Zaten aynı okulda ve doğrulanmışsa yan etkisiz; aynı okulda ama
        // beklemedeyse (öğrencinin kendi seçimi) bu çağrı onu doğrular (Changed=true — üyelik durumu değişti).
        var previousVerifiedAt = current.SchoolVerifiedAt;
        if (previousSchoolId == schoolId && previousVerifiedAt.HasValue)
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
        int? verifierUserId = actorUserId is > 0 ? actorUserId : null;
        var affected = await _context.Students
            // issue #361: okunduğu andaki doğrulama durumu da koşulda — arada bir okul onayı/reddi olduysa Conflict.
            .Where(s => s.Id == studentId && s.SchoolId == previousSchoolId && s.SchoolVerifiedAt == previousVerifiedAt)
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.SchoolId, schoolId)
                .SetProperty(s => s.SchoolVerifiedAt, now)
                .SetProperty(s => s.SchoolVerifiedByUserId, verifierUserId)
                .SetProperty(s => s.UpdateTime, now), CancellationToken.None);

        if (affected == 0)
        {
            _logger.LogWarning("[AdminStudentSchool] Eşzamanlı değişiklik: Student#{StudentId} okulu okuma/yazma arasında değişti", studentId);
            await _audit.TryUpdateOutcomeAsync(auditId, AdminUserActionOutcome.Conflict);
            return new(AdminStudentSchoolChangeStatus.Conflict);
        }

        // Okul kapsamı (SchoolScope) profil önbelleğinden gelir (#194): düşürülmezse öğrenci önbellek süresi (1 saat) boyunca
        // ESKİ okulun kapsamında kalırdı. Best-effort: Redis/Keycloak hatası DB değişikliğini geri almaz; loglanır.
        // issue #313: öğretmen okul ucuyla ortak (AdminSchoolMembershipSync: retry + gecikmeli ikinci düşürme). Öğrenci ucunun
        // HTTP sözleşmesi değişmez — önbellek düşürülemezse yalnızca loglanır.
        await new AdminSchoolMembershipSync(_keycloak, _profileCache, _jobs, _logger)
            .ApplyAsync(sub, schoolId, AdminUserTargetType.Student, studentId);

        await _audit.TryUpdateOutcomeAsync(auditId, AdminUserActionOutcome.Succeeded);
        _logger.LogInformation("[AdminStudentSchool] Öğrenci okulu değişti: Student#{StudentId} {PreviousSchoolId} → {SchoolId} actor={Actor}",
            studentId, previousSchoolId, schoolId, actorKeycloakId);
        return new(AdminStudentSchoolChangeStatus.Success, previousSchoolId, schoolId, Changed: true);
    }
}
