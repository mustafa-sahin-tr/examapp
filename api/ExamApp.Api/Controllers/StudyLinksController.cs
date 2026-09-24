using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.StudyLinks;
using ExamApp.Api.Services.StudyLinks;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Konu / alt konu harici çalışma linkleri (issue #61). Gateway üzerinden <c>/api/exam/study-links/...</c>
/// (mevcut <c>/api/exam/{everything}</c> wildcard route'u) ile erişilir.
/// Yönetim uçları Admin + Teacher (tüm konu/alt konular, sahiplik yok); öğrenci ucu yalnızca kendi sınavı.
/// Sınıf seviyesinde rol attribute'u YOK — metot bazında tanımlı, çünkü ASP.NET sınıf + metot rollerini
/// AND'ler ve öğrenci ucu yönetim rolleriyle kesişmez.
/// </summary>
[ApiController]
[Route("api/study-links")]
public class StudyLinksController : BaseController
{
    public const string ManagerRoles = "Admin,Teacher";

    private readonly ITopicStudyLinkService _service;

    // Client'a dönen tüm metinler mesaj sözlüğünden gelir (issue #184).
    private readonly IStringLocalizer<Messages> _localizer;

    public StudyLinksController(ITopicStudyLinkService service, IStringLocalizer<Messages>? localizer = null)
    {
        _service = service;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    /// <summary>Konu (topicId) veya alt konu (subTopicId) linkleri — yönetim ekranı, pasifler dahil.</summary>
    [HttpGet]
    [Authorize(Roles = ManagerRoles)]
    public async Task<IActionResult> List([FromQuery] TopicStudyLinkQueryDto query, CancellationToken ct)
    {
        var result = await _service.ListAsync(query, ct);
        return result.Success ? Ok(result) : MapFailure(result);
    }

    [HttpGet("{id:int}")]
    [Authorize(Roles = ManagerRoles)]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var result = await _service.GetByIdAsync(id, ct);
        return result.Success ? Ok(result.Link) : MapFailure(result);
    }

    [HttpPost]
    [Authorize(Roles = ManagerRoles)]
    public async Task<IActionResult> Create([FromBody] CreateTopicStudyLinkDto request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (user == null)
            return UserNotResolved(new { message = _localizer["studyLinks.unauthorized"].Value });

        var result = await _service.CreateAsync(request, user, ct);
        if (!result.Success)
            return MapFailure(result);

        return CreatedAtAction(nameof(GetById), new { id = result.ObjectId }, result.Link);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = ManagerRoles)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateTopicStudyLinkDto request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (user == null)
            return UserNotResolved(new { message = _localizer["studyLinks.unauthorized"].Value });

        var result = await _service.UpdateAsync(id, request, user.Id, ct);
        return result.Success ? Ok(result.Link) : MapFailure(result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = ManagerRoles)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (user == null)
            return UserNotResolved(new { message = _localizer["studyLinks.unauthorized"].Value });

        var result = await _service.DeleteAsync(id, user.Id, ct);
        return result.Success ? NoContent() : MapFailure(result);
    }

    /// <summary>Bir kapsamdaki linklerin gösterim sırasını toplu günceller; güncel listeyi döner.</summary>
    [HttpPut("reorder")]
    [Authorize(Roles = ManagerRoles)]
    public async Task<IActionResult> Reorder([FromBody] ReorderTopicStudyLinksDto request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (user == null)
            return UserNotResolved(new { message = _localizer["studyLinks.unauthorized"].Value });

        var result = await _service.ReorderAsync(request, user.Id, ct);
        return result.Success ? Ok(result) : MapFailure(result);
    }

    /// <summary>
    /// Öğrencinin tamamlanmış sınavında yanlış cevapladığı sorular için alt konu bazlı aktif çalışma linkleri.
    /// Sonuç ekranı bunu ayrı ve asenkron çağırır (ana sonucu bloklamaz). Sınav başkasınınsa 404.
    /// </summary>
    [HttpGet("for-result/{testInstanceId:int}")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> GetForResult(int testInstanceId, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (user == null)
            return UserNotResolved(new { message = _localizer["studyLinks.unauthorized"].Value });

        var result = await _service.GetSuggestionsForResultAsync(testInstanceId, user.Id, ct);
        return result.Success ? Ok(result.Items) : MapFailure(result);
    }

    /// <summary>ResponseBaseDto bayraklarını HTTP koduna çevirir (404 / 403 / 409 / 400).</summary>
    private IActionResult MapFailure(ResponseBaseDto result)
    {
        if (result.NotFound)
            return NotFound(result);
        if (result.Forbidden)
            return StatusCode(StatusCodes.Status403Forbidden, result);
        if (result.Conflict)
            return Conflict(result);
        return BadRequest(result);
    }
}
