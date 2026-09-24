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

    public async Task<Paged<TeacherApplicationListItemDto>> ListApplicationsAsync(
        TeacherApplicationStatusFilter status, int page, int pageSize, CancellationToken ct = default)
    {
        (page, pageSize) = AdminListPaging.Normalize(page, pageSize);

        var query = status switch
        {
            TeacherApplicationStatusFilter.Pending => PendingApplications(),
            TeacherApplicationStatusFilter.All => AllApplications(),
            _ => throw new ArgumentOutOfRangeException(nameof(status), status, "Unknown teacher application status filter.")
        };

        var totalCount = await query.CountAsync(ct);

        // Toplamı aşan sayfa boş döner (AdminTeacherService ile aynı: ikinci sorgu ve auth-api çağrısı yok).
        if (!AdminListPaging.TryGetOffset(page, pageSize, totalCount, out var offset))
            return AdminListPaging.EmptyPage<TeacherApplicationListItemDto>(page, pageSize, totalCount);

        var rows = await OrderForList(Project(query))
            .Skip(offset)
            .Take(pageSize)
            .ToListAsync(ct);

        var users = await ResolveUsersAsync(rows.Select(r => r.UserId).Distinct().ToList(), ct);
        return new Paged<TeacherApplicationListItemDto>
        {
            PageNumber = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            Items = rows.Select(r =>
            {
                users.TryGetValue(r.UserId, out var user);
                return new TeacherApplicationListItemDto
                {
                    TeacherId = r.TeacherId,
                    FullName = user?.FullName ?? string.Empty,
                    // issue #262: listede tam e-posta dönülmez (KVKK veri minimizasyonu); tam adres detay ucunda.
                    Email = user is null ? string.Empty : EmailMask.Apply(user.Email),
                    AppliedAt = r.AppliedAt,
                    IsIndependentTutor = r.IsIndependentTutor,
                    RequestedSchoolId = r.RequestedSchoolId,
                    RequestedSchoolName = r.RequestedSchoolName,
                    Status = r.Status.ToString(),
                    RejectionReason = r.RejectionReason,
                    DecidedAt = r.DecidedAt
                };
            }).ToList()
        };
    }

    public async Task<TeacherApplicationDetailDto?> GetApplicationAsync(int teacherId, CancellationToken ct = default)
    {
        // issue #187: her durumdaki başvuru (Pending/Approved/Rejected); başvuru olmayan öğretmen null → 404.
        var row = await Project(AllApplications().Where(t => t.Id == teacherId)).FirstOrDefaultAsync(ct);
        if (row == null)
            return null;

        var users = await ResolveUsersAsync([row.UserId], ct);
        users.TryGetValue(row.UserId, out var user);
        return new TeacherApplicationDetailDto
        {
            TeacherId = row.TeacherId,
            FullName = user?.FullName ?? string.Empty,
            Email = user?.Email ?? string.Empty, // tam adres — çağıran (controller) erişimi audit'ler
            AppliedAt = row.AppliedAt,
            IsIndependentTutor = row.IsIndependentTutor,
            RequestedSchoolId = row.RequestedSchoolId,
            RequestedSchoolName = row.RequestedSchoolName,
            Status = row.Status.ToString(),
            RejectionReason = row.RejectionReason,
            DecidedAt = row.DecidedAt
        };
    }

    /// <summary>
    /// Başvuru satırı + lookup için iç UserId (issue #262: UserId DTO'ya çıkmaz). Üye-atamalı sınıf (positional record
    /// değil): EF, projeksiyon SONRASI OrderBy'ı (<see cref="OrderForList"/>) yalnızca üye atamasında SQL'e çevirebilir.
    /// </summary>
    private sealed class ApplicationRow
    {
        public int TeacherId { get; init; }
        public int UserId { get; init; }
        public DateTime AppliedAt { get; init; }
        public bool IsIndependentTutor { get; init; }
        public int? RequestedSchoolId { get; init; }
        public string? RequestedSchoolName { get; init; }
        public TeacherApprovalStatus Status { get; init; }
        public string? RejectionReason { get; init; }
        public DateTime? DecidedAt { get; init; }
    }

    private IQueryable<ApplicationRow> Project(IQueryable<Teacher> query) => query
        .Select(t => new ApplicationRow
        {
            TeacherId = t.Id,
            UserId = t.UserId,
            AppliedAt = t.CreateTime,
            IsIndependentTutor = t.IsIndependentTutor,
            // issue #187: onay, okul talebini RequestedSchoolId → SchoolId'ye taşıyıp temizler; onaylı okul talebinde
            // talep edilen okul = öğretmenin okulu. Bağımsız başvuruda okul alanları her zaman null.
            RequestedSchoolId = t.IsIndependentTutor
                ? null
                : t.RequestedSchoolId ?? (t.ApprovalStatus == TeacherApprovalStatus.Approved ? t.SchoolId : null),
            RequestedSchoolName = t.IsIndependentTutor
                ? null
                : t.RequestedSchool != null
                    ? t.RequestedSchool.Name
                    : t.ApprovalStatus == TeacherApprovalStatus.Approved && t.School != null ? t.School.Name : null,
            Status = t.ApprovalStatus,
            RejectionReason = t.ApprovalStatus == TeacherApprovalStatus.Rejected ? t.RejectionReason : null,
            // issue #187: karar anı = mevcut durumla eşleşen EN SON başarılı admin kararı (#157 audit). Teacher.UpdateTime
            // güvenilir değil (sonraki her profil kaydında SaveChanges onu da günceller).
            DecidedAt = t.ApprovalStatus == TeacherApprovalStatus.Pending
                ? null
                : _context.AdminUserActionLogs
                    .Where(l => l.TargetType == AdminUserTargetType.Teacher
                                && l.TargetId == t.Id
                                && l.Outcome == AdminUserActionOutcome.Succeeded
                                && ((t.ApprovalStatus == TeacherApprovalStatus.Approved && l.Action == AdminUserAction.TeacherApproved)
                                    || (t.ApprovalStatus == TeacherApprovalStatus.Rejected && l.Action == AdminUserAction.TeacherRejected)))
                    .OrderByDescending(l => l.OccurredAtUtc)
                    .Select(l => (DateTime?)l.OccurredAtUtc)
                    .FirstOrDefault()
        });

    /// <summary>
    /// issue #187: önce bekleyenler (en eski başvuru önce — eski davranış), sonra karar verilmişler (en yeni karar önce;
    /// karar anı bilinmeyenler en sonda, başvuru tarihine göre yeniden eskiye). Son anahtar TeacherId: sayfalar kararlı.
    /// Null sıralaması sağlayıcıya göre değiştiği için (Postgres DESC → NULLS FIRST) null'lar açık anahtarla ayrılır.
    /// </summary>
    private static IQueryable<ApplicationRow> OrderForList(IQueryable<ApplicationRow> rows) => rows
        .OrderBy(r => r.Status == TeacherApprovalStatus.Pending ? 0 : 1)
        .ThenBy(r => r.Status == TeacherApprovalStatus.Pending ? (DateTime?)r.AppliedAt : null)
        .ThenBy(r => r.DecidedAt == null ? 1 : 0)
        .ThenByDescending(r => r.DecidedAt)
        .ThenByDescending(r => r.AppliedAt)
        .ThenBy(r => r.TeacherId);

    /// <summary>Onay bekleyen başvurular: bağımsız öğretmen (#94) ya da okul bağlantısı talebi (#234). Soft-deleted hariç (global filtre).</summary>
    private IQueryable<Teacher> PendingApplications() => _context.Teachers
        .AsNoTracking()
        .Where(t => t.ApprovalStatus == TeacherApprovalStatus.Pending
                    && (t.IsIndependentTutor || t.RequestedSchoolId != null));

    /// <summary>
    /// issue #187: her durumdaki başvurular. Bağımsız öğretmen (her durumda) ya da okul talebi olan öğretmen (Pending;
    /// Rejected'da RequestedSchoolId korunur). ONAYLANMIŞ okul talebinde RequestedSchoolId temizlendiği için satır yalnızca
    /// admin karar audit'inden (#157, <c>AdminUserActionLogs</c>) tanınır — #157 öncesi onaylanmış ya da audit'i yazılamamış
    /// okul talepleri sıradan okul öğretmeninden ayırt edilemez ve listede görünmez.
    /// </summary>
    private IQueryable<Teacher> AllApplications() => _context.Teachers
        .AsNoTracking()
        .Where(t => t.IsIndependentTutor
                    || t.RequestedSchoolId != null
                    || _context.AdminUserActionLogs.Any(l =>
                        l.TargetType == AdminUserTargetType.Teacher
                        && l.TargetId == t.Id
                        && l.Outcome == AdminUserActionOutcome.Succeeded
                        && (l.Action == AdminUserAction.TeacherApproved || l.Action == AdminUserAction.TeacherRejected)));

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
