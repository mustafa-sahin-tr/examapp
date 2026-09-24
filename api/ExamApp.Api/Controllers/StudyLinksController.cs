using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.StudyLinks;
using ExamApp.Api.Services.StudyLinks;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Konu / alt konu harici çalışma linkleri (issue #61). Gateway üzerinden <c>/api/exam/study-links/...</c>
/// (mevcut <c>/api/exam/{everything}</c> wildcard route'u) ile erişilir.
/// Yönetim uçları Admin + Teacher rolüne açılır; servis ayrıca öğretmenin ONAYLI olmasını ve güncelleme/silmede
/// linkin sahibi olmasını şart koşar (Admin muaf). Yazma uçları kullanıcı başına rate limit'lidir
/// (<see cref="StudyLinkWriteRateLimiting"/>). Öğrenci ucu yalnızca kendi sınavı.
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
        var actor = await ResolveActorAsync(ct);
        if (actor == null)
            return UserNotResolved(new { message = _localizer["studyLinks.unauthorized"].Value });

        var result = await _service.ListAsync(query, actor, ct);
        return result.Success ? Ok(result) : MapFailure(result);
    }

    [HttpGet("{id:int}")]
    [Authorize(Roles = ManagerRoles)]
    public async Task<IActionResult> GetById(int id, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        if (actor == null)
            return UserNotResolved(new { message = _localizer["studyLinks.unauthorized"].Value });

        var result = await _service.GetByIdAsync(id, actor, ct);
        return result.Success ? Ok(result.Link) : MapFailure(result);
    }

    [HttpPost]
    [EnableRateLimiting(StudyLinkWriteRateLimiting.Policy)]
    [Authorize(Roles = ManagerRoles)]
    public async Task<IActionResult> Create([FromBody] CreateTopicStudyLinkDto request, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        if (actor == null)
            return UserNotResolved(new { message = _localizer["studyLinks.unauthorized"].Value });

        var result = await _service.CreateAsync(request, actor, ct);
        if (!result.Success)
            return MapFailure(result);

        return CreatedAtAction(nameof(GetById), new { id = result.ObjectId }, result.Link);
    }

    [HttpPut("{id:int}")]
    [EnableRateLimiting(StudyLinkWriteRateLimiting.Policy)]
    [Authorize(Roles = ManagerRoles)]
    public async Task<IActionResult> Update(int id, [FromBody] UpdateTopicStudyLinkDto request, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        if (actor == null)
            return UserNotResolved(new { message = _localizer["studyLinks.unauthorized"].Value });

        var result = await _service.UpdateAsync(id, request, actor, ct);
        return result.Success ? Ok(result.Link) : MapFailure(result);
    }

    [HttpDelete("{id:int}")]
    [EnableRateLimiting(StudyLinkWriteRateLimiting.Policy)]
    [Authorize(Roles = ManagerRoles)]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        if (actor == null)
            return UserNotResolved(new { message = _localizer["studyLinks.unauthorized"].Value });

        var result = await _service.DeleteAsync(id, actor, ct);
        return result.Success ? NoContent() : MapFailure(result);
    }

    /// <summary>Bir kapsamdaki linklerin gösterim sırasını toplu günceller; güncel listeyi döner.</summary>
    [HttpPut("reorder")]
    [EnableRateLimiting(StudyLinkWriteRateLimiting.Policy)]
    [Authorize(Roles = ManagerRoles)]
    public async Task<IActionResult> Reorder([FromBody] ReorderTopicStudyLinksDto request, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        if (actor == null)
            return UserNotResolved(new { message = _localizer["studyLinks.unauthorized"].Value });

        var result = await _service.ReorderAsync(request, actor, ct);
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

    /// <summary>
    /// Yönetim işlemini yapan kullanıcı. IsAdmin yalnızca token'daki Admin rolünden gelir. Rol adı audit/CreatedByRole
    /// için normalize edilir ("Admin" | "Teacher"). Profil çözülemezse null (→ 401/404).
    /// </summary>
    private async Task<StudyLinkActor?> ResolveActorAsync(CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (user == null)
            return null;

        var isAdmin = IsAdmin;
        return new StudyLinkActor(user.Id, user.FullName ?? string.Empty, isAdmin ? "Admin" : "Teacher", isAdmin);
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
