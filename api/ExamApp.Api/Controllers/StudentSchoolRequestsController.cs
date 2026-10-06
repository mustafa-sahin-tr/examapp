using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.StudentSchoolMemberships;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Services.StudentSchoolMemberships;
using ExamApp.Api.Services.Teachers.Authorization;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>
/// issue #361: bekleyen öğrenci okul üyeliklerinin onayı. Gateway üzerinden <c>/api/exam/student-school-requests/...</c>
/// (mevcut <c>/api/exam/{everything}</c> wildcard route'u — ocelot değişikliği yok). Platform Admin tüm okulları, onaylı
/// öğretmen yalnız kendi okulunu görür/karar verir (ApprovedTeacher kapısı #287 + servis tarafı okul çözümü). İnce controller:
/// aktör çözümü + HTTP eşleme; kapsam/IDOR kuralı <see cref="IStudentSchoolMembershipService"/>'te. Kapsam dışı öğrenci → 404.
/// </summary>
[ApiController]
[Route("api/student-school-requests")]
[Authorize(Roles = "Admin,Teacher")]
[Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
public class StudentSchoolRequestsController : BaseController
{
    private readonly IStudentSchoolMembershipService _memberships;
    private readonly IStringLocalizer<Messages> _localizer;

    public StudentSchoolRequestsController(IStudentSchoolMembershipService memberships, IStringLocalizer<Messages>? localizer = null)
    {
        _memberships = memberships;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    /// <summary>
    /// GET api/student-school-requests?page=1&amp;pageSize=20[&amp;schoolId=5] → bekleyen başvurular (en eski önce).
    /// <c>schoolId</c> yalnız admin için daraltır; öğretmende yok sayılır. pageSize 1..100'e kırpılır.
    /// </summary>
    [HttpGet]
    [EnableRateLimiting(StudentSchoolRequestListRateLimiting.Policy)] // issue #361 review: liste kazıma yavaşlatılır
    [ProducesResponseType(typeof(Paged<StudentSchoolRequestDto>), 200)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] // kişisel veri (öğrenci adı/numarası)
    public async Task<IActionResult> GetPending(
        [FromQuery] int? schoolId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = AdminListPaging.DefaultPageSize,
        CancellationToken ct = default)
    {
        var approver = await ResolveApproverAsync(ct);
        if (approver == null)
            return UserNotResolved(Message("student.schoolMembership.unauthenticated"));

        return Ok(await _memberships.ListPendingAsync(approver, schoolId, page, pageSize, ct));
    }

    /// <summary>GET api/student-school-requests/count → <c>{ count }</c> (menü rozeti).</summary>
    [HttpGet("count")]
    [EnableRateLimiting(StudentSchoolRequestListRateLimiting.Policy)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetPendingCount(CancellationToken ct)
    {
        var approver = await ResolveApproverAsync(ct);
        if (approver == null)
            return UserNotResolved(Message("student.schoolMembership.unauthenticated"));

        return Ok(new StudentSchoolRequestCountDto { Count = await _memberships.CountPendingAsync(approver, ct) });
    }

    /// <summary>POST api/student-school-requests/{studentId}/approve → 200 <c>{ message }</c>; kapsam dışı/yok → 404; çakışma → 409.</summary>
    [HttpPost("{studentId:int}/approve")]
    [EnableRateLimiting(AdminStudentSchoolRateLimiting.Policy)]
    public async Task<IActionResult> Approve(int studentId, CancellationToken ct)
    {
        var approver = await ResolveApproverAsync(ct);
        if (approver == null)
            return UserNotResolved(Message("student.schoolMembership.unauthenticated"));

        return MapDecision(await _memberships.ApproveAsync(approver, studentId, ct), "student.schoolMembership.approved");
    }

    /// <summary>POST api/student-school-requests/{studentId}/reject → 200 <c>{ message }</c> (okul temizlenir); kapsam dışı/yok → 404; çakışma → 409.</summary>
    [HttpPost("{studentId:int}/reject")]
    [EnableRateLimiting(AdminStudentSchoolRateLimiting.Policy)]
    public async Task<IActionResult> Reject(int studentId, CancellationToken ct)
    {
        var approver = await ResolveApproverAsync(ct);
        if (approver == null)
            return UserNotResolved(Message("student.schoolMembership.unauthenticated"));

        return MapDecision(await _memberships.RejectAsync(approver, studentId, ct), "student.schoolMembership.rejected");
    }

    private async Task<StudentSchoolApprover?> ResolveApproverAsync(CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        var sub = KeyCloakId;
        if (user == null || user.Id <= 0 || string.IsNullOrWhiteSpace(sub))
            return null;
        return new StudentSchoolApprover(user.Id, IsAdmin, sub);
    }

    private IActionResult MapDecision(StudentSchoolDecisionResult result, string successKey) => result.Status switch
    {
        StudentSchoolDecisionStatus.Success => Ok(Message(successKey)),
        StudentSchoolDecisionStatus.Conflict => Conflict(Message("student.schoolMembership.alreadyDecided")),
        _ => NotFound(Message("student.schoolMembership.notFound"))
    };

    private object Message(string key) => new { message = _localizer[key].Value };
}
