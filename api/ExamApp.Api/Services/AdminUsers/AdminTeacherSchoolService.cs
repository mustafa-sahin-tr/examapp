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
/// issue #313. <see cref="AdminStudentSchoolService"/> (#277 madde 8) ile aynı hedef çözümü, audit, koşullu yazım ve
/// önbellek/claim senkronu (<see cref="AdminSchoolMembershipSync"/>).
/// <para>
/// Reddedilen durumlar (409, hiçbir şey yazılmaz; audit <c>Denied</c>):
/// <list type="bullet">
/// <item>Askıdaki hesap (<c>AccountSuspendedAt</c>, #289) — önce askı kaldırılmalı.</item>
/// <item>Hesabı onaylanmamış öğretmen (<c>AccountApprovedAt</c> null, #287) — önce hesap onayı. Okul bağlamak hesap
/// onayı değildir.</item>
/// <item>Onaylı (aktif) bağımsız öğretmen (review K1): okul kapsamı <c>SchoolScope.IsIndependent</c> = SchoolId null'a
/// bakar; bağlamak randevulu öğrencilere erişimi keserken tutor araması/takvimi açık kalırdı.</item>
/// </list>
/// </para>
/// <para>
/// Yazılan alanlar (koşullu tek UPDATE, <c>TeacherService.Save</c> kullanılmaz — oradaki "bağımsız → okullu 409" kuralı
/// kullanıcının kendi kaydı içindir ve aynen kalır):
/// <list type="bullet">
/// <item><c>SchoolId</c> = istenen okul; <c>RequestedSchoolId</c> = null (bekleyen/reddedilmiş talep admin kararıyla
/// kapanır; aksi halde sonradan onaylanırsa okulu talep edilen okula geri taşırdı).</item>
/// <item>Bağımsız (Pending/Rejected) öğretmende BAŞKA hiçbir alan değişmez: <c>IsIndependentTutor</c>, başvuru durumu ve
/// nedeni korunur. Karma durum (IsIndependentTutor + SchoolId) bilinçlidir: okul kapsamı artık okul eşitliğidir, randevu
/// (Booking) kapsamı değil; onaysız bağımsız profil aramada zaten görünmez.</item>
/// <item>Bağımsız olmayan öğretmende (hesabı onaylı, yukarıdaki kapılar) <c>ApprovalStatus=Approved</c>: kapanan okul
/// talebinin Pending/Rejected durumu, karar verilecek başvuru kalmadığı halde asılı kalmasın. <c>RejectionReason</c>
/// KORUNUR — başvuru geçmişi (<c>TeacherApprovalService</c>, review U1-a) reddedilmiş talebi audit'ten tanıyıp nedeniyle
/// gösterir; öğretmene ise yalnızca Rejected durumda gösterildiği için görünmez.</item>
/// </list>
/// </para>
/// <para>
/// Atamalar ve okul izolasyonu (bilinçli, veri silinmez/taşınmaz): öğretmenin okul kapsamı her istekte DB'deki
/// <c>Teachers.SchoolId</c>'den çözülür (#190/#222; profil önbelleği burada düşürülür) — eski okulun öğrencileri HEMEN
/// kapsam dışına çıkar. Sınıf atamaları kendi <c>SchoolId</c>'sini taşır (#236): eski okula yapılmış atamalar eski okulun
/// öğrencilerinde kalır, yeni okulun aynı sınıfına SIZMAZ; öğretmenin ilerleme görünümü eski okul öğrencilerini göstermez.
/// </para>
/// Event yayınlanmaz (öğrenci ucu da yayınlamaz). Loglar yalnızca öğretmen id'si, okul id'leri ve aktör sub'ını içerir.
/// </summary>
public class AdminTeacherSchoolService : IAdminTeacherSchoolService
{
    private readonly AppDbContext _context;
    private readonly IAdminAccountTargetResolver _targets;
    private readonly IAdminUserActionAuditService _audit;
    private readonly IKeycloakService? _keycloak;
    private readonly UserProfileCacheService? _profileCache;
    private readonly IBackgroundJobClient? _jobs;
    private readonly ILogger<AdminTeacherSchoolService> _logger;

    public AdminTeacherSchoolService(
        AppDbContext context,
        IAdminAccountTargetResolver targets,
        IAdminUserActionAuditService audit,
        IKeycloakService? keycloak = null,
        UserProfileCacheService? profileCache = null,
        ILogger<AdminTeacherSchoolService>? logger = null,
        IBackgroundJobClient? jobs = null)
    {
        _context = context;
        _targets = targets;
        _audit = audit;
        _keycloak = keycloak;
        _profileCache = profileCache;
        _jobs = jobs;
        _logger = logger ?? NullLogger<AdminTeacherSchoolService>.Instance;
    }

    public async Task<AdminTeacherSchoolChangeResult> ChangeSchoolAsync(
        int teacherId, int schoolId, string actorKeycloakId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actorKeycloakId))
            throw new InvalidOperationException("Admin teacher school change requires the actor's Keycloak subject.");

        // Audit satırı istenen okulu (ToSchoolId) her sonuçta, önceki okulu (FromSchoolId) öğretmen okunduktan sonra taşır.
        var record = new AdminUserActionRecord(actorKeycloakId, AdminUserAction.TeacherSchoolChanged,
            AdminUserTargetType.Teacher, teacherId, ToSchoolId: schoolId);

        // Soft-delete edilmiş öğretmen global filtre ile dışarıda → 404.
        var current = await _context.Teachers.AsNoTracking()
            .Where(t => t.Id == teacherId)
            .Select(t => new
            {
                t.SchoolId, t.RequestedSchoolId, t.IsIndependentTutor, t.ApprovalStatus, t.AccountApprovedAt, t.AccountSuspendedAt
            })
            .FirstOrDefaultAsync(ct);
        if (current is null)
        {
            await _audit.TryRecordAsync(record, AdminUserActionOutcome.NotFound);
            return new(AdminTeacherSchoolChangeStatus.TargetNotFound);
        }

        var previousSchoolId = current.SchoolId;
        record = record with { FromSchoolId = previousSchoolId };

        if (!await _context.Schools.AsNoTracking().AnyAsync(s => s.Id == schoolId, ct))
        {
            await _audit.TryRecordAsync(record, AdminUserActionOutcome.SchoolNotFound);
            return new(AdminTeacherSchoolChangeStatus.SchoolNotFound);
        }

        // Review D1: hedef kontrolü (kendisi / korumalı rol → 403) aynı-okul kısa devresinden ÖNCE — admin/servis hesabı ya
        // da çağıranın kendisi için "200 changed=false" dönülmesin.
        var target = await _targets.ResolveAsync(AdminUserTargetType.Teacher, teacherId, actorKeycloakId, ct);
        switch (target.Status)
        {
            case AdminAccountTargetStatus.TargetNotFound:
                await _audit.TryRecordAsync(record, AdminUserActionOutcome.NotFound);
                return new(AdminTeacherSchoolChangeStatus.TargetNotFound);
            case AdminAccountTargetStatus.AccountNotFound:
                await _audit.TryRecordAsync(record, AdminUserActionOutcome.NotFound);
                return new(AdminTeacherSchoolChangeStatus.AccountNotFound);
            case AdminAccountTargetStatus.ForbiddenSelf:
                _logger.LogWarning("[AdminTeacherSchool] Admin kendi öğretmen kaydının okulunu değiştirmeye çalıştı: actor={Actor}", actorKeycloakId);
                await _audit.TryRecordAsync(record, AdminUserActionOutcome.Denied);
                return new(AdminTeacherSchoolChangeStatus.ForbiddenSelf);
            case AdminAccountTargetStatus.ForbiddenProtectedRole:
                _logger.LogWarning("[AdminTeacherSchool] Korumalı hedef reddedildi: Teacher#{TeacherId} actor={Actor}", teacherId, actorKeycloakId);
                await _audit.TryRecordAsync(record, AdminUserActionOutcome.Denied);
                return new(AdminTeacherSchoolChangeStatus.ForbiddenProtectedRole);
            case AdminAccountTargetStatus.UpstreamFailure:
                return new(AdminTeacherSchoolChangeStatus.UpstreamFailure);
        }

        // Durum kapıları (review U1-b, U2, K1). Askı önce: askıdaki öğretmenin AccountApprovedAt'i de null'dur (#289).
        AdminTeacherSchoolChangeStatus? blocked =
            current.AccountSuspendedAt != null ? AdminTeacherSchoolChangeStatus.AccountSuspended
            : current.AccountApprovedAt == null ? AdminTeacherSchoolChangeStatus.AccountNotApproved
            : current.IsIndependentTutor && current.ApprovalStatus == TeacherApprovalStatus.Approved
                ? AdminTeacherSchoolChangeStatus.IndependentActive
                : null;
        if (blocked is { } blockedStatus)
        {
            _logger.LogInformation("[AdminTeacherSchool] Teacher#{TeacherId} durum kapısında reddedildi: {Status}", teacherId, blockedStatus);
            await _audit.TryRecordAsync(record, AdminUserActionOutcome.Denied);
            return new(blockedStatus);
        }

        // Aynı okul ve kapanacak talep yok: yan etkisiz başarı (audit, Keycloak, önbellek yok). Review Ö3: bekleyen/reddedilmiş
        // talep varsa (normalde okullu öğretmende olmaz) yazım yoluna devam edilir ki talep temizlensin.
        if (previousSchoolId == schoolId && current.RequestedSchoolId == null)
            return new(AdminTeacherSchoolChangeStatus.Success, previousSchoolId, schoolId, Changed: false);

        var sub = target.KeycloakId!;

        // Fail-closed: iz yazılamazsa istisna yukarı çıkar (500), okul değişmez.
        var auditId = await _audit.RecordAsync(record, AdminUserActionOutcome.Requested, ct);

        // Bu noktadan sonra istemci iptali akışı yarıda bırakmasın (DB değişip önbellek eski okulla kalmasın).
        // Koşullu güncelleme: okunduğu andaki okul, talep, bağımsızlık ve başvuru durumu hâlâ aynıysa VE hesap hâlâ onaylı/
        // askısızsa — eşzamanlı admin değişikliği, talep onayı/reddi, askıya alma ya da öğretmenin bağımsızlığa geçişi ile
        // yarışta son yazan kazanmasın. ExecuteUpdate SaveChanges audit'ini atlar; UpdateTime açıkça yazılır.
        var now = DateTime.UtcNow;
        var previousRequestedSchoolId = current.RequestedSchoolId;
        var previousIndependent = current.IsIndependentTutor;
        var previousStatus = current.ApprovalStatus;
        var affected = await _context.Teachers
            .Where(t => t.Id == teacherId
                && t.SchoolId == previousSchoolId
                && t.RequestedSchoolId == previousRequestedSchoolId
                && t.IsIndependentTutor == previousIndependent
                && t.ApprovalStatus == previousStatus
                && t.AccountApprovedAt != null
                && t.AccountSuspendedAt == null)
            .ExecuteUpdateAsync(set => set
                .SetProperty(t => t.SchoolId, schoolId)
                .SetProperty(t => t.RequestedSchoolId, (int?)null)
                // Bağımsız (Pending/Rejected) öğretmende başvuru durumu korunur; okul öğretmeni onaylı hale gelir.
                .SetProperty(t => t.ApprovalStatus, t => t.IsIndependentTutor ? t.ApprovalStatus : TeacherApprovalStatus.Approved)
                .SetProperty(t => t.UpdateTime, now), CancellationToken.None);

        if (affected == 0)
        {
            _logger.LogWarning("[AdminTeacherSchool] Eşzamanlı değişiklik: Teacher#{TeacherId} okul/talep/hesap durumu okuma/yazma arasında değişti", teacherId);
            await _audit.TryUpdateOutcomeAsync(auditId, AdminUserActionOutcome.Conflict);
            return new(AdminTeacherSchoolChangeStatus.Conflict);
        }

        var cacheInvalidated = await new AdminSchoolMembershipSync(_keycloak, _profileCache, _jobs, _logger)
            .ApplyAsync(sub, schoolId, AdminUserTargetType.Teacher, teacherId);

        await _audit.TryUpdateOutcomeAsync(auditId,
            cacheInvalidated ? AdminUserActionOutcome.Succeeded : AdminUserActionOutcome.SucceededCacheStale);
        _logger.LogInformation("[AdminTeacherSchool] Öğretmen okulu değişti: Teacher#{TeacherId} {PreviousSchoolId} → {SchoolId} actor={Actor} cacheStale={CacheStale}",
            teacherId, previousSchoolId, schoolId, actorKeycloakId, !cacheInvalidated);
        return new(AdminTeacherSchoolChangeStatus.Success, previousSchoolId, schoolId, Changed: true,
            ProfileCacheStale: !cacheInvalidated);
    }
}
