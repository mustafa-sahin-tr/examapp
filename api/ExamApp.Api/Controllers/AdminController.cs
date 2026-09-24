using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Services.Classifier;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.Locations;
using ExamApp.Api.Services.Schools;
using ExamApp.Api.Services.Taxonomy;
using ExamApp.Api.Services.TeacherApprovals;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Platform administration — taxonomy management and the question-classifier
/// cache. Gated on the Keycloak realm role "Admin" (see KeycloakRoleTransformer),
/// which is distinct from the "exam-admin" service client.
/// </summary>
[ApiController]
[Route("api/admin")]
[Authorize(Roles = "Admin")]
public class AdminController : BaseController
{
    private readonly ITaxonomyService _taxonomy;
    private readonly IClassifierCacheService _classifierCache;
    private readonly ISchoolService _schools;
    private readonly IDashboardService _dashboard;
    private readonly ILocationService _locations;
    private readonly ITeacherApprovalService _teacherApprovals;
    private readonly IAdminTeacherService _adminTeachers;
    private readonly IAdminStudentService _adminStudents;
    private readonly IAdminDataAccessAuditService _dataAccessAudit;
    private readonly IAdminPasswordResetService _passwordReset;
    private readonly IAdminAccountStatusService _accountStatus;

    // Client'a donen tum metinler mesaj sozlugunden gelir (issue #184).
    private readonly IStringLocalizer<Messages> _localizer;

    public AdminController(ITaxonomyService taxonomy, IClassifierCacheService classifierCache, ISchoolService schools, IDashboardService dashboard, ILocationService locations, ITeacherApprovalService teacherApprovals, IAdminTeacherService adminTeachers, IAdminStudentService adminStudents, IAdminDataAccessAuditService dataAccessAudit, IAdminPasswordResetService passwordReset, IAdminAccountStatusService accountStatus, IStringLocalizer<Messages>? localizer = null)
    {
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
        _taxonomy = taxonomy;
        _classifierCache = classifierCache;
        _schools = schools;
        _dashboard = dashboard;
        _locations = locations;
        _teacherApprovals = teacherApprovals;
        _adminTeachers = adminTeachers;
        _adminStudents = adminStudents;
        _dataAccessAudit = dataAccessAudit;
        _passwordReset = passwordReset;
        _accountStatus = accountStatus;
    }

    private async Task<int> CurrentUserIdAsync()
    {
        var profile = await GetAuthenticatedUserAsync();
        return profile?.Id ?? 0;
    }

    // ---- Taxonomy ----

    /// <summary>
    /// GET api/admin/taxonomy            → all subjects (unfiltered)
    /// GET api/admin/taxonomy?gradeId=3  → only subjects linked to grade 3
    /// GET api/admin/taxonomy?unassigned=true → only subjects with no grade link
    /// gradeId and unassigned are mutually exclusive.
    /// </summary>
    [HttpGet("taxonomy")]
    public async Task<ActionResult<TaxonomyTreeDto>> GetTaxonomy(
        [FromQuery] int? gradeId,
        [FromQuery] bool unassigned = false,
        CancellationToken ct = default)
    {
        if (gradeId.HasValue && unassigned)
            return BadRequest(_localizer["admin.taxonomy.filterConflict"].Value);

        return Ok(await _taxonomy.GetTreeAsync(gradeId, unassigned, ct));
    }

    [HttpPost("subjects/{subjectId:int}/grades/{gradeId:int}")]
    public async Task<IActionResult> AddSubjectGrade(int subjectId, int gradeId, CancellationToken ct)
        => Result(await _taxonomy.AddSubjectGradeAsync(subjectId, gradeId, await CurrentUserIdAsync(), ct));

    [HttpDelete("subjects/{subjectId:int}/grades/{gradeId:int}")]
    public async Task<IActionResult> RemoveSubjectGrade(int subjectId, int gradeId, CancellationToken ct)
        => Result(await _taxonomy.RemoveSubjectGradeAsync(subjectId, gradeId, await CurrentUserIdAsync(), ct));

    [HttpPost("subjects")]
    public async Task<IActionResult> CreateSubject([FromBody] UpsertSubjectDto dto, CancellationToken ct)
        => Result(await _taxonomy.CreateSubjectAsync(dto, await CurrentUserIdAsync(), ct));

    [HttpPut("subjects/{id:int}")]
    public async Task<IActionResult> UpdateSubject(int id, [FromBody] UpsertSubjectDto dto, CancellationToken ct)
        => Result(await _taxonomy.UpdateSubjectAsync(id, dto, await CurrentUserIdAsync(), ct));

    [HttpDelete("subjects/{id:int}")]
    public async Task<IActionResult> DeleteSubject(int id, CancellationToken ct)
        => Result(await _taxonomy.DeleteSubjectAsync(id, await CurrentUserIdAsync(), ct));

    [HttpPost("topics")]
    public async Task<IActionResult> CreateTopic([FromBody] UpsertTopicDto dto, CancellationToken ct)
        => Result(await _taxonomy.CreateTopicAsync(dto, await CurrentUserIdAsync(), ct));

    [HttpPut("topics/{id:int}")]
    public async Task<IActionResult> UpdateTopic(int id, [FromBody] UpsertTopicDto dto, CancellationToken ct)
        => Result(await _taxonomy.UpdateTopicAsync(id, dto, await CurrentUserIdAsync(), ct));

    [HttpDelete("topics/{id:int}")]
    public async Task<IActionResult> DeleteTopic(int id, CancellationToken ct)
        => Result(await _taxonomy.DeleteTopicAsync(id, await CurrentUserIdAsync(), ct));

    [HttpPost("subtopics")]
    public async Task<IActionResult> CreateSubTopic([FromBody] UpsertSubTopicDto dto, CancellationToken ct)
        => Result(await _taxonomy.CreateSubTopicAsync(dto, await CurrentUserIdAsync(), ct));

    [HttpPut("subtopics/{id:int}")]
    public async Task<IActionResult> UpdateSubTopic(int id, [FromBody] UpsertSubTopicDto dto, CancellationToken ct)
        => Result(await _taxonomy.UpdateSubTopicAsync(id, dto, await CurrentUserIdAsync(), ct));

    [HttpDelete("subtopics/{id:int}")]
    public async Task<IActionResult> DeleteSubTopic(int id, CancellationToken ct)
        => Result(await _taxonomy.DeleteSubTopicAsync(id, await CurrentUserIdAsync(), ct));

    // ---- Schools ----

    [HttpGet("schools")]
    public async Task<ActionResult<List<SchoolDto>>> GetSchools(CancellationToken ct)
        => Ok(await _schools.GetAllAsync(ct));

    [HttpPost("schools")]
    public async Task<IActionResult> CreateSchool([FromBody] UpsertSchoolDto dto, CancellationToken ct)
        => Result(await _schools.CreateAsync(dto, await CurrentUserIdAsync(), ct));

    [HttpPut("schools/{id:int}")]
    public async Task<IActionResult> UpdateSchool(int id, [FromBody] UpsertSchoolDto dto, CancellationToken ct)
        => Result(await _schools.UpdateAsync(id, dto, await CurrentUserIdAsync(), ct));

    [HttpDelete("schools/{id:int}")]
    public async Task<IActionResult> DeleteSchool(int id, CancellationToken ct)
        => Result(await _schools.DeleteAsync(id, await CurrentUserIdAsync(), ct));

    // ---- İl / ilçe referans verisi (issue #91, okul adres formu cascading dropdown) ----

    /// <summary>GET api/admin/provinces → tüm iller, alfabetik.</summary>
    [HttpGet("provinces")]
    public async Task<ActionResult<List<ProvinceDto>>> GetProvinces(CancellationToken ct)
        => Ok(await _locations.GetProvincesAsync(ct));

    /// <summary>GET api/admin/districts?provinceId=6 → o ilin ilçeleri, alfabetik. Bilinmeyen il → boş liste.</summary>
    [HttpGet("districts")]
    public async Task<ActionResult<List<DistrictDto>>> GetDistricts([FromQuery] int provinceId, CancellationToken ct)
        => Ok(await _locations.GetDistrictsAsync(provinceId, ct));

    // ---- Öğretmen başvuruları: bağımsız öğretmen (issue #94) + okul bağlantısı talebi (issue #234) ----

    /// <summary>
    /// GET api/admin/teacher-applications?status=pending&amp;page=1&amp;pageSize=20 → sayfalı (<c>Paged&lt;T&gt;</c>) başvuru listesi:
    /// bağımsız öğretmen başvuruları ve okul bağlantısı talepleri (<c>isIndependentTutor=false</c>,
    /// <c>requestedSchoolId</c>/<c>requestedSchoolName</c> dolu).
    /// issue #187: <c>status</c> = <c>pending</c> (varsayılan; yalnızca bekleyenler, en eski önce) | <c>all</c> (her durum: önce
    /// bekleyenler en eski önce, sonra karar verilmişler en yeni karar önce). Başka değer → 400. pageSize 1..100'e kırpılır.
    /// issue #262: e-posta maskeli (<c>a***@x.com</c>); her başarılı çağrı audit'lenir (durum filtresi dahil); admin liste
    /// uçlarıyla AYNI kullanıcı başına rate limit kovası (429, reddedilen istek de audit'lenir).
    /// </summary>
    [HttpGet("teacher-applications")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] // kişisel veri
    [EnableRateLimiting(AdminUserListRateLimiting.Policy)]
    [AdminDataAccess(AdminDataAccessResource.TeacherApplicationList)]
    public async Task<ActionResult<Paged<TeacherApplicationListItemDto>>> GetTeacherApplications(
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = AdminListPaging.DefaultPageSize,
        CancellationToken ct = default)
    {
        if (!TeacherApplicationStatusQuery.TryParse(status, out var filter))
            return BadRequest(_localizer["admin.teacherApplication.invalidStatusFilter"].Value);

        var result = await _teacherApprovals.ListApplicationsAsync(filter, page, pageSize, ct);
        await _dataAccessAudit.RecordListAccessAsync(new AdminListAccessRecord(
            KeyCloakId ?? string.Empty, AdminDataAccessResource.TeacherApplicationList, null, false,
            result.PageNumber, result.PageSize, result.Items?.Count ?? 0, result.TotalCount, filter), ct);
        return Ok(result);
    }

    /// <summary>
    /// GET api/admin/teacher-applications/{id} → tek başvurunun detayı, TAM e-posta ile (issue #262). issue #187: her durumdaki
    /// (Pending/Approved/Rejected) başvuru; başvuru olmayan öğretmen / bilinmeyen id → 404. Her çağrı audit'lenir
    /// (TargetId = teacherId; veri dönmeden önce, fail-closed; 404 → Outcome=NotFound);
    /// liste uçlarıyla aynı rate limit kovası.
    /// </summary>
    [HttpGet("teacher-applications/{id:int}")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] // kişisel veri
    [EnableRateLimiting(AdminUserListRateLimiting.Policy)]
    [AdminDataAccess(AdminDataAccessResource.TeacherApplicationDetail)]
    public async Task<ActionResult<TeacherApplicationDetailDto>> GetTeacherApplication(int id, CancellationToken ct)
    {
        var detail = await _teacherApprovals.GetApplicationAsync(id, ct);

        // 404 da audit'lenir (Outcome=NotFound): id tarama denemeleri iz bıraksın.
        await _dataAccessAudit.RecordDetailAccessAsync(new AdminDetailAccessRecord(
            KeyCloakId ?? string.Empty, AdminDataAccessResource.TeacherApplicationDetail, id,
            detail == null ? AdminDataAccessOutcome.NotFound : AdminDataAccessOutcome.Served), ct);

        return detail == null ? NotFound() : Ok(detail);
    }

    /// <summary>
    /// POST api/admin/teacher-applications/{id}/approve → ApprovalStatus=Approved. Okul talebinde okul bağı burada kurulur
    /// (SchoolId = RequestedSchoolId) ve öğretmenin profil önbelleği tazelenir. Pending başvuru yoksa 400; arada karar
    /// verilmiş/talep değişmişse 409.
    /// </summary>
    [HttpPost("teacher-applications/{id:int}/approve")]
    public async Task<IActionResult> ApproveTeacherApplication(int id, CancellationToken ct)
    {
        var actor = KeyCloakId;
        if (string.IsNullOrWhiteSpace(actor))
            return Forbid();

        return Result(await _teacherApprovals.ApproveAsync(id, await CurrentUserIdAsync(), actor, ct));
    }

    /// <summary>
    /// POST api/admin/teacher-applications/{id}/reject → ApprovalStatus=Rejected + RejectionReason (zorunlu). Okul talebinde
    /// okul bağı kurulmaz. Pending başvuru yoksa 400; arada karar verilmiş/talep değişmişse 409.
    /// </summary>
    [HttpPost("teacher-applications/{id:int}/reject")]
    public async Task<IActionResult> RejectTeacherApplication(int id, [FromBody] TeacherRejectRequestDto dto, CancellationToken ct)
    {
        var actor = KeyCloakId;
        if (string.IsNullOrWhiteSpace(actor))
            return Forbid();

        return Result(await _teacherApprovals.RejectAsync(id, dto.Reason, await CurrentUserIdAsync(), actor, ct));
    }

    // ---- Öğretmen listesi (issue #152) ----

    /// <summary>
    /// GET api/admin/teachers?page=1&amp;pageSize=20           → tüm öğretmenler, Id'ye göre artan
    /// GET api/admin/teachers?schoolId=5                      → yalnızca 5 numaralı okulun öğretmenleri
    /// GET api/admin/teachers?unassigned=true                 → okul bağlantısı olmayanlar (SchoolId null)
    /// schoolId ve unassigned birlikte kullanılamaz (taxonomy gradeId/unassigned ile aynı konvansiyon).
    /// pageSize 1..100 aralığına kırpılır. Ad/e-posta/hesap durumu auth-api'den sayfa başına tek çağrıyla gelir;
    /// erişilemezse liste yine döner (ad/e-posta boş, isEnabled null).
    /// issue #246: e-posta maskeli (<c>a***@x.com</c>); her başarılı çağrı audit'lenir; kullanıcı başına rate limit (429).
    /// </summary>
    [HttpGet("teachers")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] // kişisel veri; ara katmanda/tarayıcıda saklanmasın
    [EnableRateLimiting(AdminUserListRateLimiting.Policy)]
    [AdminDataAccess(AdminDataAccessResource.TeacherList)]
    public async Task<ActionResult<Paged<AdminTeacherListItemDto>>> GetTeachers(
        [FromQuery, Range(1, int.MaxValue)] int? schoolId,
        [FromQuery] bool unassigned = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = AdminListPaging.DefaultPageSize,
        CancellationToken ct = default)
    {
        if (schoolId.HasValue && unassigned)
            return BadRequest(_localizer["admin.teachers.filterConflict"].Value);

        var result = await _adminTeachers.ListAsync(page, pageSize, schoolId, unassigned, ct);
        await AuditListAccessAsync(AdminDataAccessResource.TeacherList, schoolId, unassigned, result, ct);
        return Ok(result);
    }

    // ---- Öğrenci listesi (issue #153) ----

    /// <summary>
    /// GET api/admin/students?page=1&amp;pageSize=20           → tüm öğrenciler, Id'ye göre artan
    /// GET api/admin/students?schoolId=5                      → yalnızca 5 numaralı okulun öğrencileri
    /// GET api/admin/students?unassigned=true                 → okul bağlantısı olmayanlar (SchoolId null)
    /// schoolId ve unassigned birlikte kullanılamaz. pageSize 1..100 aralığına kırpılır. Ad/e-posta/hesap durumu
    /// auth-api'den sayfa başına tek çağrıyla gelir; erişilemezse liste yine döner (ad/e-posta boş, isEnabled null).
    /// issue #246: e-posta maskeli (<c>a***@x.com</c>); her başarılı çağrı audit'lenir; kullanıcı başına rate limit (429).
    /// issue #262: öğrenci numarası kısmi (<c>****1234</c>).
    /// </summary>
    [HttpGet("students")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] // kişisel veri; ara katmanda/tarayıcıda saklanmasın
    [EnableRateLimiting(AdminUserListRateLimiting.Policy)]
    [AdminDataAccess(AdminDataAccessResource.StudentList)]
    public async Task<ActionResult<Paged<AdminStudentListItemDto>>> GetStudents(
        [FromQuery, Range(1, int.MaxValue)] int? schoolId,
        [FromQuery] bool unassigned = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = AdminListPaging.DefaultPageSize,
        CancellationToken ct = default)
    {
        if (schoolId.HasValue && unassigned)
            return BadRequest(_localizer["admin.students.filterConflict"].Value);

        var result = await _adminStudents.ListAsync(page, pageSize, schoolId, unassigned, ct);
        await AuditListAccessAsync(AdminDataAccessResource.StudentList, schoolId, unassigned, result, ct);
        return Ok(result);
    }

    /// <summary>
    /// issue #246: veri dönmeden ÖNCE yazılır; yazılamazsa istisna yukarı çıkar ve liste dönmez (fail-closed).
    /// Sayfa/boyut servisin normalize ettiği değerlerdir (istemcinin gönderdiği ham değer değil).
    /// </summary>
    private Task AuditListAccessAsync<T>(AdminDataAccessResource resource, int? schoolId, bool unassigned, Paged<T> result, CancellationToken ct)
        => _dataAccessAudit.RecordListAccessAsync(new AdminListAccessRecord(
            KeyCloakId ?? string.Empty, resource, schoolId, unassigned,
            result.PageNumber, result.PageSize, result.Items?.Count ?? 0, result.TotalCount), ct);

    // ---- Şifre sıfırlama (issue #156) ----

    /// <summary>
    /// POST api/admin/teachers/{id}/reset-password → öğretmenin (Teacher.Id) Keycloak şifresini geçici bir şifreyle
    /// değiştirir (ilk girişte değiştirme zorunlu), tüm oturumlarını kapatır ve geçici şifreyi YALNIZCA bu yanıtta döner:
    /// <c>200 { "temporaryPassword": "..." }</c>. Yanıt önbelleğe alınmaz (no-store). Hedef admin/servis hesabı ya da
    /// çağıranın kendisi → 403; kayıt/hesap yok → 404; auth-api/Keycloak hatası → 502 (şifre değişip oturumlar kapatılamadıysa
    /// ayrı mesajla 502). Admin başına rate limit (429).
    /// </summary>
    [HttpPost("teachers/{id:int}/reset-password")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] // Cache-Control: no-store + Pragma: no-cache
    [EnableRateLimiting(AdminPasswordResetRateLimiting.Policy)]
    public Task<IActionResult> ResetTeacherPassword(int id, CancellationToken ct)
        => ResetPasswordAsync(AdminUserTargetType.Teacher, id, ct);

    /// <summary>POST api/admin/students/{id}/reset-password → <see cref="ResetTeacherPassword"/> ile aynı sözleşme (Student.Id).</summary>
    [HttpPost("students/{id:int}/reset-password")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [EnableRateLimiting(AdminPasswordResetRateLimiting.Policy)]
    public Task<IActionResult> ResetStudentPassword(int id, CancellationToken ct)
        => ResetPasswordAsync(AdminUserTargetType.Student, id, ct);

    private async Task<IActionResult> ResetPasswordAsync(AdminUserTargetType targetType, int id, CancellationToken ct)
    {
        var actor = KeyCloakId;
        if (string.IsNullOrWhiteSpace(actor))
            return Forbid();

        var result = await _passwordReset.ResetAsync(targetType, id, actor, ct);
        return result.Status switch
        {
            AdminPasswordResetStatus.Success =>
                Ok(new AdminPasswordResetResponseDto { TemporaryPassword = result.TemporaryPassword! }),
            AdminPasswordResetStatus.TargetNotFound => NotFound(Message(targetType == AdminUserTargetType.Teacher
                ? "admin.passwordReset.teacherNotFound"
                : "admin.passwordReset.studentNotFound")),
            AdminPasswordResetStatus.AccountNotFound => NotFound(Message("admin.passwordReset.accountNotFound")),
            AdminPasswordResetStatus.ForbiddenSelf =>
                StatusCode(StatusCodes.Status403Forbidden, Message("admin.passwordReset.self")),
            AdminPasswordResetStatus.ForbiddenProtectedRole =>
                StatusCode(StatusCodes.Status403Forbidden, Message("admin.passwordReset.protectedRole")),
            AdminPasswordResetStatus.SessionRevokeFailed =>
                StatusCode(StatusCodes.Status502BadGateway, Message("admin.passwordReset.sessionRevokeFailed")),
            _ => StatusCode(StatusCodes.Status502BadGateway, Message("admin.passwordReset.upstreamFailed"))
        };
    }

    // ---- Hesap durumu: devre dışı bırak / etkinleştir (issue #155) ----

    /// <summary>
    /// PATCH api/admin/teachers/{id}/account-status, gövde <c>{ "enabled": bool }</c> → öğretmenin (Teacher.Id) Keycloak
    /// hesabını etkinleştirir / devre dışı bırakır; devre dışı bırakmada tüm oturumlarını da kapatır. Yanıt
    /// <c>200 { "enabled": bool }</c>; zaten istenen durumdaysa da 200 (idempotent). Hedef admin/servis hesabı ya da
    /// çağıranın kendisi → 403; kayıt/hesap yok → 404; auth-api/Keycloak hatası → 502 (hesap kapanıp oturumlar
    /// kapatılamadıysa ayrı mesajla 502). Admin başına rate limit (429).
    /// </summary>
    [HttpPatch("teachers/{id:int}/account-status")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [EnableRateLimiting(AdminAccountStatusRateLimiting.Policy)]
    public Task<IActionResult> SetTeacherAccountStatus(int id, [FromBody] AdminAccountStatusRequestDto request, CancellationToken ct)
        => SetAccountStatusAsync(AdminUserTargetType.Teacher, id, request, ct);

    /// <summary>PATCH api/admin/students/{id}/account-status → <see cref="SetTeacherAccountStatus"/> ile aynı sözleşme (Student.Id).</summary>
    [HttpPatch("students/{id:int}/account-status")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    [EnableRateLimiting(AdminAccountStatusRateLimiting.Policy)]
    public Task<IActionResult> SetStudentAccountStatus(int id, [FromBody] AdminAccountStatusRequestDto request, CancellationToken ct)
        => SetAccountStatusAsync(AdminUserTargetType.Student, id, request, ct);

    private async Task<IActionResult> SetAccountStatusAsync(
        AdminUserTargetType targetType, int id, AdminAccountStatusRequestDto request, CancellationToken ct)
    {
        var actor = KeyCloakId;
        if (string.IsNullOrWhiteSpace(actor))
            return Forbid();
        if (request?.Enabled is not bool enabled)
            return BadRequest(Message("admin.accountStatus.enabledRequired"));

        var result = await _accountStatus.SetEnabledAsync(targetType, id, enabled, actor, ct);
        return result.Status switch
        {
            AdminAccountStatusChangeStatus.Success =>
                Ok(new AdminAccountStatusResponseDto { Enabled = result.Enabled ?? enabled }),
            AdminAccountStatusChangeStatus.TargetNotFound => NotFound(Message(targetType == AdminUserTargetType.Teacher
                ? "admin.accountStatus.teacherNotFound"
                : "admin.accountStatus.studentNotFound")),
            AdminAccountStatusChangeStatus.AccountNotFound => NotFound(Message("admin.accountStatus.accountNotFound")),
            AdminAccountStatusChangeStatus.ForbiddenSelf =>
                StatusCode(StatusCodes.Status403Forbidden, Message("admin.accountStatus.self")),
            AdminAccountStatusChangeStatus.ForbiddenProtectedRole =>
                StatusCode(StatusCodes.Status403Forbidden, Message("admin.accountStatus.protectedRole")),
            AdminAccountStatusChangeStatus.SessionRevokeFailed =>
                StatusCode(StatusCodes.Status502BadGateway, Message("admin.accountStatus.sessionRevokeFailed")),
            _ => StatusCode(StatusCodes.Status502BadGateway, Message("admin.accountStatus.upstreamFailed"))
        };
    }

    private object Message(string key) => new { message = _localizer[key].Value };

    // ---- Dashboard ----

    [HttpGet("dashboard/summary")]
    public async Task<ActionResult<DashboardSummaryDto>> GetDashboardSummary(CancellationToken ct)
        => Ok(await _dashboard.GetSummaryAsync(ct));

    [HttpGet("dashboard/trends")]
    public async Task<ActionResult<DashboardTrendsDto>> GetDashboardTrends([FromQuery] int days = 30, CancellationToken ct = default)
    {
        if (days < 1 || days > 365)
            return BadRequest(new { message = _localizer["admin.dashboard.invalidDays"].Value });

        return Ok(await _dashboard.GetTrendsAsync(days, ct));
    }

    // ---- Classifier cache ----

    [HttpGet("classifier-cache")]
    public async Task<ActionResult<ClassifierCacheStatusDto>> GetClassifierCache(CancellationToken ct)
        => Ok(await _classifierCache.GetStatusAsync(ct));

    [HttpPost("classifier-cache/refresh")]
    public async Task<ActionResult<ClassifierCacheRefreshResultDto>> RefreshClassifierCache(CancellationToken ct)
    {
        var result = await _classifierCache.RefreshAsync(await CurrentUserIdAsync(), ct);
        return result.Success ? Ok(result) : BadRequest(result);
    }

    private IActionResult Result(Models.Dtos.ResponseBaseDto dto)
    {
        if (dto.Success) return Ok(dto);
        if (dto.NotFound) return NotFound(dto);
        if (dto.Conflict) return Conflict(dto);
        return BadRequest(dto);
    }
}
