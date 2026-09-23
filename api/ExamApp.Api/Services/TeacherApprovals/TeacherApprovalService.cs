using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Localization;
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

    public TeacherApprovalService(AppDbContext context, IAuthApiClient authApiClient,
        IStringLocalizer<Messages>? localizer = null,
        UserProfileCacheService? profileCache = null,
        IKeycloakService? keycloakService = null,
        ILogger<TeacherApprovalService>? logger = null)
    {
        _context = context;
        _authApiClient = authApiClient;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
        _profileCache = profileCache;
        _keycloakService = keycloakService;
        _logger = logger ?? NullLogger<TeacherApprovalService>.Instance;
    }

    public async Task<List<PendingTeacherApplicationDto>> GetPendingApplicationsAsync(CancellationToken ct = default)
    {
        var pending = await _context.Teachers
            .AsNoTracking()
            .Where(t => t.ApprovalStatus == TeacherApprovalStatus.Pending
                        && (t.IsIndependentTutor || t.RequestedSchoolId != null))
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
                item.Email = user.Email;
            }
        }

        return pending;
    }

    public async Task<ResponseBaseDto> ApproveAsync(int teacherId, int adminUserId, CancellationToken ct = default)
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

        // Koşullu güncelleme (code review #234): okuma ile yazma arasında öğretmen talebini değiştirdiyse
        // (bağımsızlığa geçiş, yeni talep) ya da başka bir admin karar verdiyse 0 satır etkilenir → alreadyDecided.
        // ExecuteUpdate SaveChanges audit'ini atlar; UpdateTime/UpdateUserId burada açıkça yazılır.
        var now = DateTime.UtcNow;
        var affected = await PendingApplicationQuery(teacherId, isIndependent, expectedRequestedSchoolId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.ApprovalStatus, TeacherApprovalStatus.Approved)
                .SetProperty(t => t.RejectionReason, (string?)null)
                .SetProperty(t => t.SchoolId, newSchoolId)
                .SetProperty(t => t.RequestedSchoolId, newRequestedSchoolId)
                .SetProperty(t => t.UpdateTime, now)
                .SetProperty(t => t.UpdateUserId, adminUserId), ct);

        if (affected == 0)
            return AlreadyDecided();

        // Önbellek/Keycloak yalnızca güncelleme gerçekten uygulandıysa.
        if (linksSchool)
            await SyncSchoolMembershipAsync(teacher.UserId, newSchoolId, ct);

        return Ok(_localizer["admin.teacherApplication.approved"], teacher.Id);
    }

    public async Task<ResponseBaseDto> RejectAsync(int teacherId, string reason, int adminUserId, CancellationToken ct = default)
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

        // issue #234: redde okul bağı kurulmaz. RequestedSchoolId hangi okul talebinin reddedildiğini göstermek
        // için korunur; SchoolId null kalır, Rejected durumu talebin artık beklemediğini belirtir.
        // Koşullu güncelleme: ApproveAsync ile aynı yarış koruması.
        var now = DateTime.UtcNow;
        var affected = await PendingApplicationQuery(teacherId, teacher!.IsIndependentTutor, teacher.RequestedSchoolId)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(t => t.ApprovalStatus, TeacherApprovalStatus.Rejected)
                .SetProperty(t => t.RejectionReason, trimmedReason)
                .SetProperty(t => t.UpdateTime, now)
                .SetProperty(t => t.UpdateUserId, adminUserId), ct);

        if (affected == 0)
            return AlreadyDecided();

        return Ok(_localizer["admin.teacherApplication.rejected"], teacher.Id);
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
