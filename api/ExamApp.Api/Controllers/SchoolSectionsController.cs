using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.AdminUsers;
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
/// issue #1279 (#1071 S4): okulun şubeleri ve şube öğrencileri — okulun onaylı, askısız, bağımsız olmayan her öğretmeni okur
/// (okul yöneticisi dahil). Yönetim uçları <see cref="SchoolAdminController"/>'da. Okul DB'den çözülür
/// (<see cref="ISchoolSectionService.ResolveTeacherSchoolIdAsync"/>); okulsuz / bağımsız / onaysız öğretmen → 403, başka okulun
/// şubesi → 404. Gateway <c>/api/exam/{everything}</c>.
/// </summary>
[ApiController]
[Route("api/school-sections")]
[Authorize(Roles = "Teacher")]
[Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
public class SchoolSectionsController : BaseController
{
    private readonly ISchoolSectionService _sections;
    private readonly IStringLocalizer<Messages> _localizer;

    public SchoolSectionsController(ISchoolSectionService sections, IStringLocalizer<Messages>? localizer = null)
    {
        _sections = sections;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    /// <summary>GET api/school-sections[?gradeId=] → okulun şubeleri (sınıf, ad sırasıyla; aktif öğrenci sayısıyla).</summary>
    [HttpGet]
    [EnableRateLimiting(StudentSchoolRequestListRateLimiting.Policy)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> List([FromQuery] int? gradeId, CancellationToken ct)
    {
        var schoolId = await ResolveSchoolAsync(ct);
        return schoolId is int id ? Ok(await _sections.ListAsync(id, gradeId, ct)) : Forbidden();
    }

    /// <summary>GET api/school-sections/{id}/students?page&amp;pageSize → şubenin aktif öğrencileri (numara maskeli).</summary>
    [HttpGet("{id:int}/students")]
    [EnableRateLimiting(StudentSchoolRequestListRateLimiting.Policy)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] // kişisel veri
    public async Task<ActionResult<Paged<SchoolSectionStudentDto>>> Students(
        int id,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = AdminListPaging.DefaultPageSize,
        CancellationToken ct = default)
    {
        var schoolId = await ResolveSchoolAsync(ct);
        if (schoolId is not int school)
            return Forbidden();
        var result = await _sections.ListStudentsAsync(school, id, page, pageSize, ct);
        return result is null ? NotFound(new { message = _localizer["admin.schoolSection.notFound"].Value }) : Ok(result);
    }

    private async Task<int?> ResolveSchoolAsync(CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        return IsResolvedUser(user) ? await _sections.ResolveTeacherSchoolIdAsync(user.Id, ct) : null;
    }

    private ObjectResult Forbidden() =>
        StatusCode(StatusCodes.Status403Forbidden, new { message = _localizer["admin.schoolSection.notSchoolTeacher"].Value });
}
