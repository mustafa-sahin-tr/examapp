using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.Parents;
using ExamApp.Foundation.Contracts;
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
/// <item>issue #541: okulsuz yapma (schoolId null) eski okulun bütün kapsamını kaldırır (öğretmen listeleri, sınıf
/// atamaları, okul liderliği); yalnız doğrudan ve platform geneli atamalar kalır.</item>
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
    private readonly IAuthApiClient? _authApi;
    private readonly ILogger<AdminStudentSchoolService> _logger;

    public AdminStudentSchoolService(
        AppDbContext context,
        IAdminAccountTargetResolver targets,
        IAdminUserActionAuditService audit,
        IKeycloakService? keycloak = null,
        UserProfileCacheService? profileCache = null,
        ILogger<AdminStudentSchoolService>? logger = null,
        IBackgroundJobClient? jobs = null,
        IAuthApiClient? authApi = null)
    {
        _context = context;
        _targets = targets;
        _audit = audit;
        _keycloak = keycloak;
        _profileCache = profileCache;
        _jobs = jobs;
        _authApi = authApi;
        _logger = logger ?? NullLogger<AdminStudentSchoolService>.Instance;
    }

    public async Task<AdminStudentSchoolChangeResult> ChangeSchoolAsync(
        int studentId, int? schoolId, string actorKeycloakId, int? actorUserId = null, CancellationToken ct = default)
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
            .Select(s => new { s.UserId, s.SchoolId, s.SchoolVerifiedAt })
            .FirstOrDefaultAsync(ct);
        if (current is null)
        {
            await _audit.TryRecordAsync(record, AdminUserActionOutcome.NotFound);
            return new(AdminStudentSchoolChangeStatus.TargetNotFound);
        }

        var previousSchoolId = current.SchoolId;
        record = record with { FromSchoolId = previousSchoolId };

        // issue #541: okulsuz yapma (schoolId null) okul doğrulaması gerektirmez; zaten okulsuzsa yan etkisiz.
        if (schoolId is null)
        {
            if (previousSchoolId is null)
                return new(AdminStudentSchoolChangeStatus.Success, null, null, Changed: false);
        }
        else if (!await _context.Schools.AsNoTracking().AnyAsync(s => s.Id == schoolId, ct))
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
        // issue #541: okulsuz yapmada üyelik doğrulaması da temizlenir (doğrulanmış okulsuz üyelik anlamsız).
        var now = DateTime.UtcNow;
        DateTime? verifiedAt = schoolId is null ? null : now;
        int? verifierUserId = schoolId is not null && actorUserId is > 0 ? actorUserId : null;
        // issue #542: okul gerçekten değişince (aynı okulda yalnız üyelik doğrulaması değil) bildirim event'leri okul yazımıyla AYNI
        // transaction'da: öğrenciye ve o an Active her veliye birer tane. Veli listesi / adlar transaction dışında (salt okuma,
        // auth-api fail-soft); transaction içinde Active veliler kilitli yeniden okunur (TestSessionService #423 deseni).
        var notify = previousSchoolId != schoolId;
        var plan = notify ? await PlanNotificationsAsync(studentId, current.UserId, previousSchoolId, schoolId, ct) : null;
        var affected = 0;
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();
            await using var tx = await _context.Database.BeginTransactionAsync(CancellationToken.None);
            affected = await _context.Students
                // issue #361: okunduğu andaki doğrulama durumu da koşulda — arada bir okul onayı/reddi olduysa Conflict.
                .Where(s => s.Id == studentId && s.SchoolId == previousSchoolId && s.SchoolVerifiedAt == previousVerifiedAt)
                .ExecuteUpdateAsync(set => set
                    .SetProperty(s => s.SchoolId, schoolId)
                    .SetProperty(s => s.SchoolVerifiedAt, verifiedAt)
                    .SetProperty(s => s.SchoolVerifiedByUserId, verifierUserId)
                    .SetProperty(s => s.UpdateTime, now), CancellationToken.None);
            if (affected == 0)
                return; // commit yok → dispose'da rollback

            if (plan != null)
            {
                var stillActive = await ParentNotificationSupport.ActiveParentIdsLockedAsync(_context, studentId, CancellationToken.None);
                AddNotifications(plan, sub, stillActive, now);
                await _context.SaveChangesAsync(CancellationToken.None);
            }

            await tx.CommitAsync(CancellationToken.None);
        });

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
        _logger.LogInformation("[AdminStudentSchool] Öğrenci okulu değişti: Student#{StudentId} {PreviousSchoolId} → {SchoolId} (null = okulsuz) actor={Actor}",
            studentId, previousSchoolId, schoolId, actorKeycloakId);
        return new(AdminStudentSchoolChangeStatus.Success, previousSchoolId, schoolId, Changed: true);
    }

    private sealed record NotificationPlan(
        int StudentId, int StudentUserId, int? FromSchoolId, string FromSchoolName, int? ToSchoolId, string ToSchoolName,
        IReadOnlyList<ActiveParentRecipient> Parents, IReadOnlyDictionary<int, NotificationUser> Users);

    private async Task<NotificationPlan> PlanNotificationsAsync(
        int studentId, int studentUserId, int? fromSchoolId, int? toSchoolId, CancellationToken ct)
    {
        var (fromName, toName) = await SchoolChangeNotifications.SchoolNamesAsync(_context, fromSchoolId, toSchoolId, ct);
        var parents = await ParentNotificationSupport.ActiveParentsAsync(_context, studentId, ct);
        var users = parents.Count == 0
            ? new Dictionary<int, NotificationUser>()
            : await ParentNotificationSupport.LookupAsync(_authApi, parents.Select(p => p.ParentUserId).Append(studentUserId), _logger, ct);
        return new NotificationPlan(studentId, studentUserId, fromSchoolId, fromName, toSchoolId, toName, parents, users);
    }

    private void AddNotifications(NotificationPlan plan, string studentSub, IReadOnlySet<int> stillActiveParentIds, DateTime now)
    {
        SchoolChangedEvent Event(string audience, int recipientUserId, string recipientSub, string studentName) => new()
        {
            EventId = Guid.NewGuid(),
            Audience = audience,
            RecipientUserId = recipientUserId,
            RecipientKeycloakId = recipientSub,
            SubjectUserId = plan.StudentUserId,
            StudentId = plan.StudentId,
            StudentDisplayName = studentName,
            FromSchoolId = plan.FromSchoolId,
            FromSchoolName = plan.FromSchoolName,
            ToSchoolId = plan.ToSchoolId,
            ToSchoolName = plan.ToSchoolName,
            ChangedAtUtc = now
        };

        SchoolChangeNotifications.Add(_context, Event(SchoolChangedEvent.StudentAudience, plan.StudentUserId, studentSub, string.Empty));
        var studentName = ParentNotificationSupport.Of(plan.Users, plan.StudentUserId).DisplayName;
        // Security M1 (#423): yalnız hem planda hem transaction içi okumada Active olan veliler.
        foreach (var parent in plan.Parents.Where(p => stillActiveParentIds.Contains(p.ParentId)))
        {
            SchoolChangeNotifications.Add(_context, Event(SchoolChangedEvent.ParentAudience, parent.ParentUserId,
                ParentNotificationSupport.Of(plan.Users, parent.ParentUserId).KeycloakId, studentName));
        }
    }
}
