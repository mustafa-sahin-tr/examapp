using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Localization;
using ExamApp.Foundation.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Services.TeacherApprovals;

public class TeacherApprovalService : ITeacherApprovalService
{
    private const int RejectionReasonMaxLength = 500;

    private readonly AppDbContext _context;
    private readonly IAuthApiClient _authApiClient;

    // Client'a ulasan mesajlar sozlukten gelir (issue #184). Localizer opsiyoneldir: DI disinda
    // olusturulan (birim test) ornekler varsayilan dile kilitli fallback'e duser.
    private readonly IStringLocalizer<Messages> _localizer;

    // issue #234: okul bağlantısı onaylanınca öğretmenin profil önbelleği (SchoolId) düşürülür ve Keycloak
    // "school_id" ipucu güncellenir. DI her zaman verir; DI'siz kurulan (birim test) örneklerde null → atlanır.
    private readonly UserProfileCacheService? _profileCache;
    private readonly IKeycloakService? _keycloakService;
    private readonly ILogger<TeacherApprovalService> _logger;

    // issue #157: karar audit'i (AdminUserActionLogs). DI her zaman verir; DI'siz kurulan (birim test)
    // örneklerde null → audit atlanır (best-effort, karar bundan etkilenmez).
    private readonly IAdminUserActionAuditService? _audit;

    public TeacherApprovalService(AppDbContext context, IAuthApiClient authApiClient,
        IStringLocalizer<Messages>? localizer = null,
        UserProfileCacheService? profileCache = null,
        IKeycloakService? keycloakService = null,
        ILogger<TeacherApprovalService>? logger = null,
        IAdminUserActionAuditService? auditService = null)
    {
        _context = context;
        _authApiClient = authApiClient;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
        _profileCache = profileCache;
        _keycloakService = keycloakService;
        _logger = logger ?? NullLogger<TeacherApprovalService>.Instance;
        _audit = auditService;
    }

    public async Task<List<PendingTeacherApplicationDto>> GetPendingApplicationsAsync(CancellationToken ct = default)
    {
        var pending = await PendingApplications()
            .OrderBy(t => t.CreateTime)
            .Select(t => new PendingTeacherApplicationDto
            {
                TeacherId = t.Id,
                UserId = t.UserId,
                AppliedAt = t.CreateTime,
                IsIndependentTutor = t.IsIndependentTutor,
                RequestedSchoolId = t.IsIndependentTutor ? null : t.RequestedSchoolId,
                RequestedSchoolName = t.IsIndependentTutor || t.RequestedSchool == null ? null : t.RequestedSchool.Name
            })
            .ToListAsync(ct);

        if (pending.Count == 0)
            return pending;

        var users = await ResolveUsersAsync(pending.Select(p => p.UserId).Distinct().ToList(), ct);
        foreach (var item in pending)
        {
            if (users.TryGetValue(item.UserId, out var user))
            {
                item.FullName = user.FullName;
                // issue #262: listede tam e-posta dönülmez (KVKK veri minimizasyonu); tam adres detay ucunda.
                item.Email = EmailMask.Apply(user.Email);
            }
        }

        return pending;
    }

    public async Task<TeacherApplicationDetailDto?> GetPendingApplicationAsync(int teacherId, CancellationToken ct = default)
    {
        var detail = await PendingApplications()
            .Where(t => t.Id == teacherId)
            .Select(t => new TeacherApplicationDetailDto
            {
                TeacherId = t.Id,
                UserId = t.UserId,
                AppliedAt = t.CreateTime,
                IsIndependentTutor = t.IsIndependentTutor,
                RequestedSchoolId = t.IsIndependentTutor ? null : t.RequestedSchoolId,
                RequestedSchoolName = t.IsIndependentTutor || t.RequestedSchool == null ? null : t.RequestedSchool.Name
            })
            .FirstOrDefaultAsync(ct);

        if (detail == null)
            return null;

        var users = await ResolveUsersAsync([detail.UserId], ct);
        if (users.TryGetValue(detail.UserId, out var user))
        {
            detail.FullName = user.FullName;
            detail.Email = user.Email ?? string.Empty; // tam adres — çağıran (controller) erişimi audit'ler
        }

        return detail;
    }

    /// <summary>Onay bekleyen başvurular: bağımsız öğretmen (#94) ya da okul bağlantısı talebi (#234). Soft-deleted hariç (global filtre).</summary>
    private IQueryable<Teacher> PendingApplications() => _context.Teachers
        .AsNoTracking()
        .Where(t => t.ApprovalStatus == TeacherApprovalStatus.Pending
                    && (t.IsIndependentTutor || t.RequestedSchoolId != null));

    public async Task<ResponseBaseDto> ApproveAsync(int teacherId, int adminUserId, string actorAdminKeycloakId, CancellationToken ct = default)
    {
        var teacher = await _context.Teachers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == teacherId, ct);
        var guardError = GuardPendingApplication(teacher);
        if (guardError != null)
            return guardError;

        // issue #234: okul bağlantısı talebinde okul bağı YALNIZCA burada kurulur (RequestedSchoolId → SchoolId).
        var isIndependent = teacher!.IsIndependentTutor;
        var expectedRequestedSchoolId = teacher.RequestedSchoolId;
        var linksSchool = !isIndependent;
        var newSchoolId = teacher.SchoolId;
        var newRequestedSchoolId = teacher.RequestedSchoolId;
        if (linksSchool)
        {
            var requestedSchoolId = expectedRequestedSchoolId!.Value;
            if (!await _context.Schools.AnyAsync(s => s.Id == requestedSchoolId, ct))
                return Fail(_localizer["admin.teacherApplication.requestedSchoolNotFound"]);

            newSchoolId = requestedSchoolId;
            newRequestedSchoolId = null;
        }

        // issue #157: bildirim outbox'ı için başvuru sahibinin Keycloak sub'ı, transaction AÇILMADAN ÖNCE
        // (I/O'yu tx dışında tutmak için, WorksheetAccessRequestService ile aynı desen) best-effort çözülür.
        // Lookup başarısız/boşsa outbox YAZILMAZ, karar yine commit edilir (mimari karar #157).
        var targetKeycloakId = await TryResolveTargetKeycloakIdAsync(teacher.UserId, teacherId, ct);

        var now = DateTime.UtcNow;

        // Koşullu güncelleme (code review #234): okuma ile yazma arasında öğretmen talebini değiştirdiyse
        // (bağımsızlığa geçiş, yeni talep) ya da başka bir admin karar verdiyse 0 satır etkilenir → alreadyDecided.
        // ExecuteUpdate SaveChanges audit'ini atlar; UpdateTime/UpdateUserId burada açıkça yazılır.
        // issue #157: ExecuteUpdate + outbox insert tek transaction'da (retry-on-failure için execution
        // strategy içinde — WorksheetAccessRequestService.CreateRequestAsync ile aynı desen).
        var affected = 0;
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // issue #157 review: retry'da bir önceki denemenin ChangeTracker'a eklediği (ama commit
            // edilmemiş) outbox mesajı temizlenmezse ikinci deneme aynı OutboxMessage'ı BİR DAHA ekler
            // (Guid Id farklı olsa da aynı karar için iki event) → duplicate bildirim riski.
            _context.ChangeTracker.Clear();

            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            affected = await PendingApplicationQuery(teacherId, isIndependent, expectedRequestedSchoolId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(t => t.ApprovalStatus, TeacherApprovalStatus.Approved)
                    .SetProperty(t => t.RejectionReason, (string?)null)
                    .SetProperty(t => t.SchoolId, newSchoolId)
                    .SetProperty(t => t.RequestedSchoolId, newRequestedSchoolId)
                    .SetProperty(t => t.UpdateTime, now)
                    .SetProperty(t => t.UpdateUserId, adminUserId), ct);

            if (affected > 0 && !string.IsNullOrWhiteSpace(targetKeycloakId))
            {
                AddDecisionOutbox(teacherId, targetKeycloakId, approved: true, isIndependent, now);
                await _context.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);
        });

        if (affected == 0)
            return AlreadyDecided();

        // issue #157 review: audit, best-effort ama karardan hemen sonra — SyncSchoolMembershipAsync
        // (Keycloak/Redis I/O) atmasa da fırlatsa audit izi kaybolmasın.
        await TryAuditDecisionAsync(AdminUserAction.TeacherApproved, teacherId, actorAdminKeycloakId);

        // Önbellek/Keycloak yalnızca güncelleme gerçekten uygulandıysa.
        if (linksSchool)
            await SyncSchoolMembershipAsync(teacher.UserId, newSchoolId, ct);

        return Ok(_localizer["admin.teacherApplication.approved"], teacher.Id);
    }

    public async Task<ResponseBaseDto> RejectAsync(int teacherId, string reason, int adminUserId, string actorAdminKeycloakId, CancellationToken ct = default)
    {
        var trimmedReason = reason?.Trim();
        if (string.IsNullOrWhiteSpace(trimmedReason))
            return Fail(_localizer["admin.teacherApplication.rejectReasonRequired"]);

        if (trimmedReason.Length > RejectionReasonMaxLength)
            return Fail(_localizer["admin.teacherApplication.rejectReasonTooLong", RejectionReasonMaxLength]);

        var teacher = await _context.Teachers.AsNoTracking().FirstOrDefaultAsync(t => t.Id == teacherId, ct);
        var guardError = GuardPendingApplication(teacher);
        if (guardError != null)
            return guardError;

        // issue #157: bkz. ApproveAsync — aynı best-effort sub çözümü, aynı gerekçe.
        var targetKeycloakId = await TryResolveTargetKeycloakIdAsync(teacher!.UserId, teacherId, ct);

        // issue #234: redde okul bağı kurulmaz. RequestedSchoolId hangi okul talebinin reddedildiğini göstermek
        // için korunur; SchoolId null kalır, Rejected durumu talebin artık beklemediğini belirtir.
        // Koşullu güncelleme: ApproveAsync ile aynı yarış koruması.
        var now = DateTime.UtcNow;
        var affected = 0;
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            // issue #157 review: bkz. ApproveAsync — retry'da çift outbox'ı önler.
            _context.ChangeTracker.Clear();

            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            affected = await PendingApplicationQuery(teacherId, teacher.IsIndependentTutor, teacher.RequestedSchoolId)
                .ExecuteUpdateAsync(setters => setters
                    .SetProperty(t => t.ApprovalStatus, TeacherApprovalStatus.Rejected)
                    .SetProperty(t => t.RejectionReason, trimmedReason)
                    .SetProperty(t => t.UpdateTime, now)
                    .SetProperty(t => t.UpdateUserId, adminUserId), ct);

            if (affected > 0 && !string.IsNullOrWhiteSpace(targetKeycloakId))
            {
                AddDecisionOutbox(teacherId, targetKeycloakId, approved: false, teacher.IsIndependentTutor, now);
                await _context.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);
        });

        if (affected == 0)
            return AlreadyDecided();

        await TryAuditDecisionAsync(AdminUserAction.TeacherRejected, teacherId, actorAdminKeycloakId);

        return Ok(_localizer["admin.teacherApplication.rejected"], teacher.Id);
    }

    /// <summary>
    /// issue #157: karar bildirimi outbox event'ini ChangeTracker'a ekler (SaveChanges çağıran metotta).
    /// Payload'da ret gerekçesi ve admin kimliği YOK (security review).
    /// </summary>
    private void AddDecisionOutbox(int teacherId, string targetKeycloakId, bool approved, bool isIndependentTutor, DateTime decidedAt)
    {
        var @event = new TeacherApplicationDecidedEvent
        {
            EventId = Guid.NewGuid(),
            TeacherId = teacherId,
            TargetKeycloakId = targetKeycloakId,
            Approved = approved,
            IsIndependentTutor = isIndependentTutor,
            DecidedAtUtc = decidedAt
        };

        _context.OutboxMessages.Add(new OutboxMessage
        {
            Type = OutboxEventRegistry.NameFor<TeacherApplicationDecidedEvent>(),
            Content = JsonSerializer.Serialize(@event),
            CreatedAt = decidedAt
        });
    }

    /// <summary>
    /// issue #157: kararın gideceği başvuru sahibinin Keycloak sub'ı best-effort çözülür. auth-api
    /// erişilemezse ya da kullanıcı için sub bulunamazsa null döner ve çağıran outbox'ı atlar — karar
    /// yine de commit edilir (bildirim kaybı Keycloak lookup'ından daha az kritik kabul edildi).
    /// </summary>
    private async Task<string?> TryResolveTargetKeycloakIdAsync(int userId, int teacherId, CancellationToken ct)
    {
        try
        {
            var users = await _authApiClient.GetUsersByIdsAsync(new[] { userId }, ct);
            var keycloakId = users.FirstOrDefault(u => u.Id == userId)?.KeycloakId;
            if (string.IsNullOrWhiteSpace(keycloakId))
            {
                _logger.LogWarning(
                    "TeacherApplicationDecided: no KeycloakId for user {UserId} (TeacherId={TeacherId}); notification outbox skipped.",
                    userId, teacherId);
                return null;
            }
            return keycloakId;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex,
                "TeacherApplicationDecided: KeycloakId lookup failed for user {UserId} (TeacherId={TeacherId}); notification outbox skipped.",
                userId, teacherId);
            return null;
        }
    }

    /// <summary>
    /// issue #157: karar audit'i best-effort — karar zaten commit edildikten SONRA çağrılır, audit yazımı
    /// başarısız olsa da karar geri alınmaz (AdminAccountStatusService'teki "Requested→Succeeded" akışının
    /// aksine burada dış sistem çağrısı yok; tek DB yazımı zaten atomik şekilde tamamlandı, audit yalnızca
    /// iz amaçlı ek bir kayıttır). actorAdminKeycloakId boşsa (DI'siz test) sessizce atlanır.
    /// </summary>
    private async Task TryAuditDecisionAsync(AdminUserAction action, int teacherId, string? actorAdminKeycloakId)
    {
        if (_audit == null || string.IsNullOrWhiteSpace(actorAdminKeycloakId))
            return;

        var record = new AdminUserActionRecord(actorAdminKeycloakId, action, AdminUserTargetType.Teacher, teacherId);
        await _audit.TryRecordAsync(record, AdminUserActionOutcome.Succeeded);
    }

    /// <summary>
    /// Kararın okunduğu andaki başvuru hâlâ aynıysa eşleşen tek satır: Pending, aynı bağımsızlık bayrağı ve aynı
    /// okul talebi. Aradaki değişiklik (ör. öğretmenin bağımsızlığa geçip talebini geri çekmesi) güncellemeyi boşa düşürür.
    /// </summary>
    private IQueryable<Teacher> PendingApplicationQuery(int teacherId, bool isIndependent, int? requestedSchoolId)
    {
        return _context.Teachers.Where(t => t.Id == teacherId
            && t.ApprovalStatus == TeacherApprovalStatus.Pending
            && t.IsIndependentTutor == isIndependent
            && t.RequestedSchoolId == requestedSchoolId);
    }

    private ResponseBaseDto AlreadyDecided() => new()
    {
        Success = false,
        Conflict = true,
        Message = _localizer["admin.teacherApplication.alreadyDecided"]
    };

    /// <summary>
    /// Karar alabilen başvurular: bağımsız öğretmen (issue #94) ya da okul bağlantısı talebi olan öğretmen
    /// (RequestedSchoolId dolu, issue #234) — ikisi de Pending olmalı. Talebi olmayan okula bağlı öğretmen uygun
    /// değildir; zaten karar verilmiş başvuru tekrar onaylanamaz/reddedilemez (idempotency).
    /// </summary>
    private ResponseBaseDto? GuardPendingApplication(Teacher? teacher)
    {
        if (teacher == null)
            return new ResponseBaseDto { Success = false, NotFound = true, Message = _localizer["admin.teacherApplication.notFound"] };

        if (!teacher.IsIndependentTutor && !teacher.RequestedSchoolId.HasValue)
            return Fail(_localizer["admin.teacherApplication.noPendingApplication"]);

        if (teacher.ApprovalStatus != TeacherApprovalStatus.Pending)
            return AlreadyDecided();

        return null;
    }

    /// <summary>
    /// issue #234: okul bağı kurulduktan sonra öğretmenin önbellekteki profili (SchoolId=null) düşürülür ki bir
    /// sonraki istekte SchoolId DB'den yeniden çözülsün (<see cref="UserProfileCacheService.RemoveAsync"/>, #194
    /// invalidation noktası); Keycloak "school_id" JWT ipucu da güncellenir. Best-effort: DB zaten tutarlı —
    /// hata onayı geri almaz, en kötü ihtimalle öğretmen önbellek süresi (1 saat) boyunca okulsuz görünür
    /// (güvenli yön). KeycloakId auth-api'den çözülür.
    /// </summary>
    private async Task SyncSchoolMembershipAsync(int userId, int? schoolId, CancellationToken ct)
    {
        if (_profileCache == null && _keycloakService == null)
            return;

        string? keycloakId;
        try
        {
            var users = await _authApiClient.GetUsersByIdsAsync(new[] { userId }, ct);
            keycloakId = users.FirstOrDefault(u => u.Id == userId)?.KeycloakId;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "Teacher school approval: KeycloakId could not be resolved for user {UserId}; profile cache not refreshed", userId);
            return;
        }

        if (string.IsNullOrEmpty(keycloakId))
        {
            _logger.LogWarning("Teacher school approval: no KeycloakId for user {UserId}; profile cache not refreshed", userId);
            return;
        }

        try
        {
            if (_profileCache != null)
                await _profileCache.RemoveAsync(keycloakId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Teacher school approval: profile cache invalidation failed for {KeycloakId}", keycloakId);
        }

        try
        {
            if (_keycloakService != null)
                await _keycloakService.SetSchoolIdAttributeAsync(keycloakId, schoolId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Teacher school approval: Keycloak school_id attribute update failed for {KeycloakId}", keycloakId);
        }
    }

    /// <summary>
    /// UserId'leri tek batch çağrıyla ad/e-postaya çevirir (TeacherService.ResolveStudentNamesAsync ile aynı desen).
    /// Auth-api erişilemezse boş sözlük döner — liste yine de dönmeli.
    /// </summary>
    private async Task<Dictionary<int, UserLookupResultDto>> ResolveUsersAsync(List<int> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0)
            return new Dictionary<int, UserLookupResultDto>();

        try
        {
            var users = await _authApiClient.GetUsersByIdsAsync(userIds, ct);
            return users
                .GroupBy(u => u.Id)
                .ToDictionary(g => g.Key, g => g.First());
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new Dictionary<int, UserLookupResultDto>();
        }
    }

    private static ResponseBaseDto Fail(string message) => new() { Success = false, Message = message };

    private static ResponseBaseDto Ok(string message, int id) =>
        new() { Success = true, Message = message, ObjectId = id };
}
