using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.Classifier;
using ExamApp.Api.Services.Dashboard;
using ExamApp.Api.Services.Locations;
using ExamApp.Api.Services.Schools;
using ExamApp.Api.Services.Taxonomy;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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

    public AdminController(ITaxonomyService taxonomy, IClassifierCacheService classifierCache, ISchoolService schools, IDashboardService dashboard, ILocationService locations)
    {
        _taxonomy = taxonomy;
        _classifierCache = classifierCache;
        _schools = schools;
        _dashboard = dashboard;
        _locations = locations;
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
            return BadRequest("gradeId ve unassigned birlikte kullanılamaz.");

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

    // ---- Dashboard ----

    [HttpGet("dashboard/summary")]
    public async Task<ActionResult<DashboardSummaryDto>> GetDashboardSummary(CancellationToken ct)
        => Ok(await _dashboard.GetSummaryAsync(ct));

    [HttpGet("dashboard/trends")]
    public async Task<ActionResult<DashboardTrendsDto>> GetDashboardTrends([FromQuery] int days = 30, CancellationToken ct = default)
    {
        if (days < 1 || days > 365)
            return BadRequest(new { message = "days 1 ile 365 arasında olmalı." });

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
        => dto.Success ? Ok(dto) : BadRequest(dto);
}
