using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.Classifier;
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

    public AdminController(ITaxonomyService taxonomy, IClassifierCacheService classifierCache)
    {
        _taxonomy = taxonomy;
        _classifierCache = classifierCache;
    }

    private async Task<int> CurrentUserIdAsync()
    {
        var profile = await GetAuthenticatedUserAsync();
        return profile?.Id ?? 0;
    }

    // ---- Taxonomy ----

    [HttpGet("taxonomy")]
    public async Task<ActionResult<TaxonomyTreeDto>> GetTaxonomy(CancellationToken ct)
        => Ok(await _taxonomy.GetTreeAsync(ct));

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
