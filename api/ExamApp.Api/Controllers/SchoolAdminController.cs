using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Services.TeacherApprovals;
using ExamApp.Api.Services.Teachers.Authorization;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>
/// issue #1277 (#1071 S2): okul yöneticisinin kendi okulunun öğretmen başvurularını listeleyip onaylaması / reddetmesi.
/// Gateway'de mevcut <c>/api/exam/{everything}</c> route'u (ocelot değişikliği yok). Yetki JWT'den değil DB'den
/// (<see cref="ISchoolAdminResolver"/>, her istekte): okul yöneticisi olmayan öğretmen → 403. Kapsam
/// (<see cref="SchoolApplicationScope"/>) ve kurallar <see cref="ITeacherApprovalService"/>'te — platform admin uçlarıyla
/// aynı servis; başka okulun başvurusu → 404. Karar audit'inde aktör okul yöneticisinin Keycloak sub'ıdır.
/// </summary>
[ApiController]
[Route("api/school-admin")]
[Authorize(Roles = "Teacher")]
[Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
public class SchoolAdminController : BaseController
{
    private readonly ITeacherApprovalService _approvals;
    private readonly ISchoolAdminResolver _schoolAdmins;
    private readonly IAdminDataAccessAuditService _dataAccessAudit;
    private readonly ISchoolAdminOverviewService _overview;
    private readonly ISchoolSectionService _sections;
    private readonly IStringLocalizer<Messages> _localizer;

    public SchoolAdminController(ITeacherApprovalService approvals, ISchoolAdminResolver schoolAdmins,
        IAdminDataAccessAuditService dataAccessAudit, ISchoolAdminOverviewService overview, ISchoolSectionService sections,
        IStringLocalizer<Messages>? localizer = null)
    {
        _sections = sections;
        _approvals = approvals;
        _schoolAdmins = schoolAdmins;
        _dataAccessAudit = dataAccessAudit;
        _overview = overview;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    /// <summary>
    /// GET api/school-admin/summary → okul adı, öğretmen / öğrenci sayısı, bekleyen öğretmen başvurusu ve öğrenci üyeliği
    /// (#1278 okul paneli; menü rozeti de buradan); okul yöneticisi değilse 403.
    /// </summary>
    [HttpGet("summary")]
    [EnableRateLimiting(StudentSchoolRequestListRateLimiting.Policy)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        if (actor is null)
            return Forbidden();

        var pendingTeachers = await _approvals.CountPendingAsync(actor.Scope, ct);
        return Ok(await _overview.GetSummaryAsync(actor.Scope.SchoolId, actor.Scope.ActorUserId, pendingTeachers, ct));
    }

    /// <summary>
    /// GET api/school-admin/teachers?page&amp;pageSize → okulun öğretmenleri (#1278; salt okunur, yöneticiler önce).
    /// Erişim admin veri erişimi audit'ine okul filtresiyle yazılır.
    /// </summary>
    [HttpGet("teachers")]
    [EnableRateLimiting(StudentSchoolRequestListRateLimiting.Policy)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] // kişisel veri
    public async Task<ActionResult<Paged<SchoolAdminTeacherDto>>> GetTeachers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = AdminListPaging.DefaultPageSize,
        CancellationToken ct = default)
    {
        var actor = await ResolveActorAsync(ct);
        if (actor is null)
            return Forbidden();

        var result = await _overview.ListTeachersAsync(actor.Scope.SchoolId, page, pageSize, ct);
        await _dataAccessAudit.RecordListAccessAsync(new AdminListAccessRecord(
            actor.KeycloakId, AdminDataAccessResource.TeacherList, actor.Scope.SchoolId, false,
            result.PageNumber, result.PageSize, result.Items?.Count ?? 0, result.TotalCount), ct);
        return Ok(result);
    }

    /// <summary>
    /// GET api/school-admin/students?gradeId&amp;page&amp;pageSize → okulun doğrulanmış öğrencileri (#1278; numara maskeli).
    /// Bekleyen üyelikler <c>api/student-school-requests</c> kuyruğundadır.
    /// </summary>
    [HttpGet("students")]
    [EnableRateLimiting(StudentSchoolRequestListRateLimiting.Policy)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] // kişisel veri
    public async Task<ActionResult<Paged<SchoolAdminStudentDto>>> GetStudents(
        [FromQuery] int? gradeId = null,
        [FromQuery] int? sectionId = null,
        [FromQuery] bool unassigned = false,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = AdminListPaging.DefaultPageSize,
        CancellationToken ct = default)
    {
        var actor = await ResolveActorAsync(ct);
        if (actor is null)
            return Forbidden();

        var result = await _overview.ListStudentsAsync(actor.Scope.SchoolId, gradeId, page, pageSize, ct, sectionId, unassigned);
        await _dataAccessAudit.RecordListAccessAsync(new AdminListAccessRecord(
            actor.KeycloakId, AdminDataAccessResource.StudentList, actor.Scope.SchoolId, false,
            result.PageNumber, result.PageSize, result.Items?.Count ?? 0, result.TotalCount), ct);
        return Ok(result);
    }

    /// <summary>
    /// GET api/school-admin/teacher-applications?status=pending|all&amp;page&amp;pageSize → okulun başvuruları (platform admin
    /// listesiyle aynı şekil; e-posta maskeli). Erişim admin veri erişimi audit'ine okul filtresiyle yazılır.
    /// </summary>
    [HttpGet("teacher-applications")]
    [EnableRateLimiting(StudentSchoolRequestListRateLimiting.Policy)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] // kişisel veri
    public async Task<ActionResult<Paged<TeacherApplicationListItemDto>>> GetTeacherApplications(
        [FromQuery] string? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = AdminListPaging.DefaultPageSize,
        CancellationToken ct = default)
    {
        var actor = await ResolveActorAsync(ct);
        if (actor is null)
            return Forbidden();
        if (!TeacherApplicationStatusQuery.TryParse(status, out var filter))
            return BadRequest(_localizer["admin.teacherApplication.invalidStatusFilter"].Value);

        var result = await _approvals.ListApplicationsAsync(filter, page, pageSize, ct, actor.Scope);
        await _dataAccessAudit.RecordListAccessAsync(new AdminListAccessRecord(
            actor.KeycloakId, AdminDataAccessResource.TeacherApplicationList, actor.Scope.SchoolId, false,
            result.PageNumber, result.PageSize, result.Items?.Count ?? 0, result.TotalCount, filter), ct);
        return Ok(result);
    }

    /// <summary>GET api/school-admin/teacher-applications/{id} → detay (tam e-posta yalnız bekleyende); başka okul → 404.</summary>
    [HttpGet("teacher-applications/{id:int}")]
    [EnableRateLimiting(StudentSchoolRequestListRateLimiting.Policy)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] // kişisel veri
    public async Task<ActionResult<TeacherApplicationDetailDto>> GetTeacherApplication(int id, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        if (actor is null)
            return Forbidden();

        var detail = await _approvals.GetApplicationAsync(id, ct, actor.Scope);
        await _dataAccessAudit.RecordDetailAccessAsync(new AdminDetailAccessRecord(
            actor.KeycloakId, AdminDataAccessResource.TeacherApplicationDetail, id,
            detail == null ? AdminDataAccessOutcome.NotFound : AdminDataAccessOutcome.Served,
            detail?.Status), ct);
        return detail == null ? NotFound() : Ok(detail);
    }

    /// <summary>POST api/school-admin/teacher-applications/{id}/approve → okula bağlar; başka okul / yok → 404, karar verilmiş → 409.</summary>
    [HttpPost("teacher-applications/{id:int}/approve")]
    [EnableRateLimiting(AdminStudentSchoolRateLimiting.Policy)]
    public async Task<IActionResult> Approve(int id, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        if (actor is null)
            return Forbidden();

        return Result(await _approvals.ApproveAsync(id, actor.Scope.ActorUserId, actor.KeycloakId, ct, actor.Scope));
    }

    /// <summary>POST api/school-admin/teacher-applications/{id}/reject <c>{ reason }</c> → ret; kurallar admin ucuyla aynı.</summary>
    [HttpPost("teacher-applications/{id:int}/reject")]
    [EnableRateLimiting(AdminStudentSchoolRateLimiting.Policy)]
    public async Task<IActionResult> Reject(int id, [FromBody] TeacherRejectRequestDto dto, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        if (actor is null)
            return Forbidden();

        return Result(await _approvals.RejectAsync(id, dto.Reason, actor.Scope.ActorUserId, actor.KeycloakId, ct, actor.Scope));
    }

    // ---- issue #1279 (#1071 S4): şube yönetimi (okuma uçları api/school-sections, okulun tüm öğretmenlerine açık) ----

    /// <summary>POST api/school-admin/sections <c>{ gradeId, name }</c> → 201 şube; ad boş / &gt;20 / sınıf yok → 400, aynı ad → 409.</summary>
    [HttpPost("sections")]
    [EnableRateLimiting(AdminStudentSchoolRateLimiting.Policy)]
    public async Task<IActionResult> CreateSection([FromBody] SchoolSectionCreateRequestDto dto, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        if (actor is null)
            return Forbidden();
        var result = await _sections.CreateAsync(actor.Scope.SchoolId, actor.Scope.ActorUserId, dto?.GradeId, dto?.Name, ct);
        return result.Status == SchoolSectionStatus.Success
            ? StatusCode(StatusCodes.Status201Created, result.Section)
            : SectionError(result);
    }

    /// <summary>PUT api/school-admin/sections/{id} <c>{ name }</c> → yeniden adlandır (sınıf değişmez).</summary>
    [HttpPut("sections/{id:int}")]
    [EnableRateLimiting(AdminStudentSchoolRateLimiting.Policy)]
    public async Task<IActionResult> RenameSection(int id, [FromBody] SchoolSectionRenameRequestDto dto, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        if (actor is null)
            return Forbidden();
        var result = await _sections.RenameAsync(actor.Scope.SchoolId, actor.Scope.ActorUserId, id, dto?.Name, ct);
        return result.Status == SchoolSectionStatus.Success ? Ok(result.Section) : SectionError(result);
    }

    /// <summary>DELETE api/school-admin/sections/{id} → 204; aktif üyelikler kapanır (geçmiş kalır). Başka okul → 404.</summary>
    [HttpDelete("sections/{id:int}")]
    [EnableRateLimiting(AdminStudentSchoolRateLimiting.Policy)]
    public async Task<IActionResult> DeleteSection(int id, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        if (actor is null)
            return Forbidden();
        var result = await _sections.DeleteAsync(actor.Scope.SchoolId, actor.Scope.ActorUserId, id, ct);
        return result.Status == SchoolSectionStatus.Success ? NoContent() : SectionError(result);
    }

    /// <summary>
    /// POST api/school-admin/sections/{id}/students <c>{ studentIds }</c> (1..100) → öğrencileri şubeye ekler; başka şubedekiler
    /// taşınır. Okulun doğrulanmış üyesi olmayan öğrenci → 404 (hiçbiri atanmaz); sınıfı farklı → 409 <c>{ studentIds }</c>.
    /// </summary>
    [HttpPost("sections/{id:int}/students")]
    [EnableRateLimiting(AdminStudentSchoolRateLimiting.Policy)]
    public async Task<IActionResult> AssignStudents(int id, [FromBody] SchoolSectionAssignRequestDto dto, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        if (actor is null)
            return Forbidden();
        var result = await _sections.AssignAsync(actor.Scope.SchoolId, actor.Scope.ActorUserId, id, dto?.StudentIds, ct);
        return result.Status == SchoolSectionStatus.Success ? Ok(result.Assignment) : SectionError(result);
    }

    /// <summary>DELETE api/school-admin/sections/{id}/students/{studentId} → 204; öğrenci bu şubede değilse 404.</summary>
    [HttpDelete("sections/{id:int}/students/{studentId:int}")]
    [EnableRateLimiting(AdminStudentSchoolRateLimiting.Policy)]
    public async Task<IActionResult> RemoveStudent(int id, int studentId, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        if (actor is null)
            return Forbidden();
        var result = await _sections.RemoveStudentAsync(actor.Scope.SchoolId, actor.Scope.ActorUserId, id, studentId, ct);
        return result.Status == SchoolSectionStatus.Success ? NoContent() : SectionError(result);
    }

    private IActionResult SectionError(SchoolSectionResult result) => result.Status switch
    {
        SchoolSectionStatus.NotFound => NotFound(new { message = _localizer["admin.schoolSection.notFound"].Value }),
        SchoolSectionStatus.Invalid => BadRequest(new { message = _localizer["admin.schoolSection.invalid"].Value }),
        SchoolSectionStatus.Duplicate => Conflict(new { message = _localizer["admin.schoolSection.duplicate"].Value }),
        SchoolSectionStatus.GradeMismatch => Conflict(new
        {
            message = _localizer["admin.schoolSection.gradeMismatch"].Value,
            studentIds = result.StudentIds
        }),
        _ => Conflict(new { message = _localizer["admin.schoolSection.conflict"].Value })
    };

    private sealed record SchoolAdminActor(SchoolApplicationScope Scope, string KeycloakId);

    /// <summary>Okul yöneticisi değilse (ya da kullanıcı çözülemezse) null → 403.</summary>
    private async Task<SchoolAdminActor?> ResolveActorAsync(CancellationToken ct)
    {
        var sub = KeyCloakId;
        if (string.IsNullOrWhiteSpace(sub))
            return null;
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return null;
        var schoolId = await _schoolAdmins.GetAdministeredSchoolIdAsync(user.Id, ct);
        return schoolId is int id ? new SchoolAdminActor(new SchoolApplicationScope(id, user.Id), sub) : null;
    }

    private ObjectResult Forbidden() =>
        StatusCode(StatusCodes.Status403Forbidden, new { message = _localizer["admin.schoolAdmin.notSchoolAdmin"].Value });

    private IActionResult Result(ResponseBaseDto dto)
    {
        if (dto.Success) return Ok(dto);
        if (dto.NotFound) return NotFound(dto);
        if (dto.Conflict) return Conflict(dto);
        return BadRequest(dto);
    }
}
