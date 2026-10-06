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
using ExamApp.Api.Models.Dtos.StudentSchoolMemberships;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Services.Interfaces;
using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Services.StudentSchoolMemberships;

/// <inheritdoc cref="IStudentSchoolMembershipService"/>
/// <remarks>
/// Yazımlar koşullu <c>ExecuteUpdate</c> (TeacherApprovalService/AdminStudentSchoolService deseni): okunduğu andaki başvuru
/// (aynı okul + hâlâ beklemede) değiştiyse 0 satır → Conflict. ExecuteUpdate SaveChanges audit'ini atlar; UpdateTime/
/// UpdateUserId açıkça yazılır. Onaydan sonra öğrencinin profil önbelleği düşürülür ve Keycloak <c>school_id</c> ipucu
/// güncellenir (<see cref="AdminSchoolMembershipSync"/>, best-effort) — okul kapsamı profil önbelleğinden gelir (#194); düşmezse
/// öğrenci en geç önbellek süresi boyunca okulsuz görünür (güvenli yön). Ret yetki durumunu değiştirmez (beklemedeki üyelik
/// zaten kapsam vermiyordu), önbellek dokunulmaz. Loglarda yalnız id'ler (PII yok).
/// </remarks>
public sealed class StudentSchoolMembershipService : IStudentSchoolMembershipService
{
    private readonly AppDbContext _context;
    private readonly IAuthApiClient _authApiClient;
    private readonly IAdminUserActionAuditService _audit;
    private readonly IKeycloakService? _keycloak;
    private readonly UserProfileCacheService? _profileCache;
    private readonly IBackgroundJobClient? _jobs;
    private readonly ILogger<StudentSchoolMembershipService> _logger;

    public StudentSchoolMembershipService(
        AppDbContext context,
        IAuthApiClient authApiClient,
        IAdminUserActionAuditService audit,
        IKeycloakService? keycloak = null,
        UserProfileCacheService? profileCache = null,
        IBackgroundJobClient? jobs = null,
        ILogger<StudentSchoolMembershipService>? logger = null)
    {
        _context = context;
        _authApiClient = authApiClient;
        _audit = audit;
        _keycloak = keycloak;
        _profileCache = profileCache;
        _jobs = jobs;
        _logger = logger ?? NullLogger<StudentSchoolMembershipService>.Instance;
    }

    public async Task<Paged<StudentSchoolRequestDto>> ListPendingAsync(
        StudentSchoolApprover approver, int? schoolIdFilter, int page, int pageSize, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(approver);
        (page, pageSize) = AdminListPaging.Normalize(page, pageSize);

        var scope = await ResolveApproverScopeAsync(approver, ct);
        if (scope is null)
            return AdminListPaging.EmptyPage<StudentSchoolRequestDto>(page, pageSize, 0);

        var query = PendingQuery(approver, scope);
        // Admin için isteğe bağlı daraltma; öğretmende filtre yok sayılır (okul zaten tek, istemci genişletemez).
        if (approver.IsAdmin && schoolIdFilter is int filter)
            query = query.Where(s => s.SchoolId == filter);

        var totalCount = await query.CountAsync(ct);
        if (!AdminListPaging.TryGetOffset(page, pageSize, totalCount, out var offset))
            return AdminListPaging.EmptyPage<StudentSchoolRequestDto>(page, pageSize, totalCount);

        var rows = await query
            .OrderBy(s => s.CreateTime)
            .ThenBy(s => s.Id)
            .Skip(offset)
            .Take(pageSize)
            .Select(s => new
            {
                s.Id,
                s.UserId,
                s.StudentNumber,
                s.GradeId,
                GradeName = s.Grade != null ? s.Grade.Name : null,
                SchoolId = s.SchoolId!.Value,
                SchoolName = s.School != null ? s.School.Name : string.Empty,
                s.CreateTime
            })
            .ToListAsync(ct);

        var names = await ResolveNamesAsync(rows.Select(r => r.UserId).Distinct().ToList(), ct);
        return new Paged<StudentSchoolRequestDto>
        {
            PageNumber = page,
            PageSize = pageSize,
            TotalCount = totalCount,
            Items = rows.Select(r => new StudentSchoolRequestDto
            {
                StudentId = r.Id,
                FullName = names.GetValueOrDefault(r.UserId) ?? string.Empty,
                // #262 ile tutarlı: liste görünümünde öğrenci numarası kısmi (son 4 karakter).
                StudentNumber = StudentNumberMask.Apply(r.StudentNumber),
                GradeId = r.GradeId,
                GradeName = r.GradeName,
                SchoolId = r.SchoolId,
                SchoolName = r.SchoolName,
                RegisteredAt = r.CreateTime
            }).ToList()
        };
    }

    public async Task<int> CountPendingAsync(StudentSchoolApprover approver, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(approver);
        var scope = await ResolveApproverScopeAsync(approver, ct);
        return scope is null ? 0 : await PendingQuery(approver, scope).CountAsync(ct);
    }

    public async Task<StudentSchoolDecisionResult> ApproveAsync(
        StudentSchoolApprover approver, int studentId, CancellationToken ct = default)
    {
        var target = await ResolveDecisionTargetAsync(approver, studentId, ct);
        if (target is null)
        {
            await AuditNotFoundAsync(approver, AdminUserAction.StudentSchoolApproved, studentId);
            return new(StudentSchoolDecisionStatus.NotFound);
        }

        var schoolId = target.SchoolId;
        // Fail-closed (#277 deseni): iz yazılamazsa istisna yukarı çıkar (500), üyelik değişmez.
        var auditId = await _audit.RecordAsync(Record(approver, AdminUserAction.StudentSchoolApproved, studentId, schoolId),
            AdminUserActionOutcome.Requested, ct);

        var now = DateTime.UtcNow;
        var affected = await StillPending(studentId, schoolId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.SchoolVerifiedAt, now)
                .SetProperty(s => s.SchoolVerifiedByUserId, approver.UserId)
                .SetProperty(s => s.UpdateTime, now)
                .SetProperty(s => s.UpdateUserId, approver.UserId), CancellationToken.None);

        if (affected == 0)
        {
            _logger.LogWarning("[StudentSchool] Onay çakışması: Student#{StudentId} okuma/yazma arasında değişti", studentId);
            await _audit.TryUpdateOutcomeAsync(auditId, AdminUserActionOutcome.Conflict);
            return new(StudentSchoolDecisionStatus.Conflict);
        }

        _logger.LogInformation("[StudentSchool] Üyelik onaylandı: Student#{StudentId} School#{SchoolId} approver={ApproverUserId} admin={IsAdmin}",
            studentId, schoolId, approver.UserId, approver.IsAdmin);

        // Bu noktadan sonra istemci iptali önbellek düşürmeyi yarıda bırakmasın (DB zaten değişti).
        var cacheInvalidated = false;
        var sub = await TryResolveKeycloakIdAsync(target.UserId, studentId, CancellationToken.None);
        if (!string.IsNullOrWhiteSpace(sub))
        {
            cacheInvalidated = await new AdminSchoolMembershipSync(_keycloak, _profileCache, _jobs, _logger)
                .ApplyAsync(sub, schoolId, AdminUserTargetType.Student, studentId);
        }

        // Önbellek düşürülemediyse (ya da sub çözülemediyse) karar yine başarılıdır; öğrenci en geç önbellek süresi boyunca
        // okulsuz görünür (güvenli yön) — iz bunu ayırt eder (#313 SucceededCacheStale).
        await _audit.TryUpdateOutcomeAsync(auditId,
            cacheInvalidated ? AdminUserActionOutcome.Succeeded : AdminUserActionOutcome.SucceededCacheStale);
        return new(StudentSchoolDecisionStatus.Success, schoolId);
    }

    public async Task<StudentSchoolDecisionResult> RejectAsync(
        StudentSchoolApprover approver, int studentId, CancellationToken ct = default)
    {
        var target = await ResolveDecisionTargetAsync(approver, studentId, ct);
        if (target is null)
        {
            await AuditNotFoundAsync(approver, AdminUserAction.StudentSchoolRejected, studentId);
            return new(StudentSchoolDecisionStatus.NotFound);
        }

        var rejectedSchoolId = target.SchoolId;
        var auditId = await _audit.RecordAsync(Record(approver, AdminUserAction.StudentSchoolRejected, studentId, rejectedSchoolId),
            AdminUserActionOutcome.Requested, ct);

        var now = DateTime.UtcNow;
        var affected = await StillPending(studentId, rejectedSchoolId)
            .ExecuteUpdateAsync(set => set
                .SetProperty(s => s.SchoolId, (int?)null)
                // issue #361 review: aynı okul StudentService.SchoolRejectCooldown boyunca yeniden istenemez.
                .SetProperty(s => s.LastRejectedSchoolId, rejectedSchoolId)
                .SetProperty(s => s.SchoolRejectedAt, now)
                .SetProperty(s => s.SchoolRejectedByUserId, approver.UserId)
                .SetProperty(s => s.UpdateTime, now)
                .SetProperty(s => s.UpdateUserId, approver.UserId), CancellationToken.None);

        if (affected == 0)
        {
            _logger.LogWarning("[StudentSchool] Ret çakışması: Student#{StudentId} okuma/yazma arasında değişti", studentId);
            await _audit.TryUpdateOutcomeAsync(auditId, AdminUserActionOutcome.Conflict);
            return new(StudentSchoolDecisionStatus.Conflict);
        }

        await _audit.TryUpdateOutcomeAsync(auditId, AdminUserActionOutcome.Succeeded);

        _logger.LogInformation("[StudentSchool] Üyelik reddedildi: Student#{StudentId} School#{SchoolId} approver={ApproverUserId} admin={IsAdmin}",
            studentId, target.SchoolId, approver.UserId, approver.IsAdmin);
        return new(StudentSchoolDecisionStatus.Success, target.SchoolId);
    }

    /// <summary>
    /// issue #361 review (security Medium): karar izi AdminUserActionLogs'a (#277 AdminStudentSchoolService deseni) — aktör
    /// admin ya da öğretmen olabilir (#305 yorum moderasyonu emsali); <c>ToSchoolId</c> = kararın verildiği okul.
    /// </summary>
    private static AdminUserActionRecord Record(StudentSchoolApprover approver, AdminUserAction action, int studentId, int? schoolId)
        => new(approver.KeycloakId, action, AdminUserTargetType.Student, studentId, ToSchoolId: schoolId);

    /// <summary>
    /// Kapsam dışı / olmayan / beklemede olmayan hedefe karar denemesi best-effort izlenir (id tarama denemeleri iz bıraksın;
    /// yanıt yine 404).
    /// </summary>
    private Task AuditNotFoundAsync(StudentSchoolApprover approver, AdminUserAction action, int studentId)
        => _audit.TryRecordAsync(Record(approver, action, studentId, null), AdminUserActionOutcome.NotFound);

    /// <summary>Onaylayıcının kapsamı: admin → tüm okullar; öğretmen → tek doğrulanmış okul; yetkisiz → null.</summary>
    private sealed record ApproverScope(bool AllSchools, int? SchoolId);

    private sealed record DecisionTarget(int UserId, int SchoolId);

    /// <summary>
    /// Öğretmen okulu: canlı, hesabı onaylı ve askıda olmayan, okula bağlı TEK Teachers satırı (#259 unique index; index'siz
    /// ortamda birden fazla satır → belirsiz → yetki yok, tahmin yok). Admin rolü öğretmen kaydından bağımsız tüm okulları kapsar.
    /// </summary>
    private async Task<ApproverScope?> ResolveApproverScopeAsync(StudentSchoolApprover approver, CancellationToken ct)
    {
        if (approver.IsAdmin)
            return new ApproverScope(true, null);
        if (approver.UserId <= 0)
            return null;

        var rows = await _context.Teachers.AsNoTracking()
            .Where(t => t.UserId == approver.UserId)
            .Select(t => new { t.SchoolId, t.AccountApprovedAt, t.AccountSuspendedAt })
            .Take(2)
            .ToListAsync(ct);
        if (rows.Count != 1)
            return null;

        var row = rows[0];
        if (row.SchoolId is not int schoolId || row.AccountApprovedAt == null || row.AccountSuspendedAt != null)
            return null;

        return new ApproverScope(false, schoolId);
    }

    /// <summary>Bekleyen (okul seçilmiş, doğrulanmamış) öğrenciler, onaylayıcı kapsamında; onaylayıcının kendi kaydı hariç.</summary>
    private IQueryable<Student> PendingQuery(StudentSchoolApprover approver, ApproverScope scope)
    {
        var approverUserId = approver.UserId;
        var query = _context.Students.AsNoTracking()
            .Where(s => s.SchoolId != null && s.SchoolVerifiedAt == null && s.UserId != approverUserId);
        if (!scope.AllSchools)
        {
            var schoolId = scope.SchoolId;
            query = query.Where(s => s.SchoolId == schoolId);
        }
        return query;
    }

    /// <summary>
    /// Karar hedefi: kapsam içi, beklemedeki öğrenci. Kapsam dışı / olmayan / silinmiş / beklemede olmayan → null (404, tek
    /// sorguda — var/yok oracle'ı kapalı).
    /// </summary>
    private async Task<DecisionTarget?> ResolveDecisionTargetAsync(
        StudentSchoolApprover approver, int studentId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(approver);
        var scope = await ResolveApproverScopeAsync(approver, ct);
        if (scope is null)
            return null;

        return await PendingQuery(approver, scope)
            .Where(s => s.Id == studentId)
            .Select(s => new DecisionTarget(s.UserId, s.SchoolId!.Value))
            .FirstOrDefaultAsync(ct);
    }

    /// <summary>Koşullu yazım hedefi: okunduğu andaki okul hâlâ aynı ve üyelik hâlâ beklemede.</summary>
    private IQueryable<Student> StillPending(int studentId, int schoolId) =>
        _context.Students.Where(s => s.Id == studentId && s.SchoolId == schoolId && s.SchoolVerifiedAt == null);

    /// <summary>Ad çözümü tek batch çağrı (TeacherApprovalService deseni); auth-api erişilemezse boş — liste yine döner.</summary>
    private async Task<Dictionary<int, string>> ResolveNamesAsync(List<int> userIds, CancellationToken ct)
    {
        if (userIds.Count == 0)
            return new Dictionary<int, string>();
        try
        {
            var users = await _authApiClient.GetUsersByIdsAsync(userIds, ct);
            return users.GroupBy(u => u.Id).ToDictionary(g => g.Key, g => g.First().FullName ?? string.Empty);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "[StudentSchool] Ad çözümü başarısız ({Count} kullanıcı); liste adsız döner", userIds.Count);
            return new Dictionary<int, string>();
        }
    }

    private async Task<string?> TryResolveKeycloakIdAsync(int userId, int studentId, CancellationToken ct)
    {
        try
        {
            var users = await _authApiClient.GetUsersByIdsAsync(new[] { userId }, ct);
            var keycloakId = users.FirstOrDefault(u => u.Id == userId)?.KeycloakId;
            if (string.IsNullOrWhiteSpace(keycloakId))
                _logger.LogWarning("[StudentSchool] Student#{StudentId} için KeycloakId yok; profil önbelleği düşürülmedi", studentId);
            return keycloakId;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning(ex, "[StudentSchool] Student#{StudentId} KeycloakId çözülemedi; profil önbelleği düşürülmedi", studentId);
            return null;
        }
    }
}
