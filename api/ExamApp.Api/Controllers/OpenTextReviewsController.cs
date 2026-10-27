using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.OpenText;
using ExamApp.Api.Services.Teachers.Authorization;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>
/// issue #583: açık uçlu (<c>openText</c>) etkinlik cevaplarının öğretmen değerlendirme kuyruğu — gateway:
/// <c>/api/exam/open-text-reviews</c> (mevcut <c>/api/exam/{everything}</c> route'u). Yalnız Teacher (onaylı, #287) ve Admin;
/// kapsam (#573 öğretmen takibi kuralı) serviste. Öğrenci kendi değerlendirmesini oturum yanıtında görür
/// (<c>test-canvas-instance(-result)</c>, kontrol ucu) — bu controller'a erişimi yoktur.
/// </summary>
[ApiController]
[Route("api/open-text-reviews")]
public class OpenTextReviewsController : BaseController
{
    public const string ReviewerRoles = "Teacher,Admin";

    private readonly IOpenTextReviewService _service;
    private readonly IStringLocalizer<Messages> _localizer;

    public OpenTextReviewsController(IOpenTextReviewService service, IStringLocalizer<Messages>? localizer = null)
    {
        _service = service;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    /// <summary>
    /// Kuyruk: <c>?status=pending|reviewed|all&amp;worksheetId=&amp;page=1&amp;pageSize=20</c> (page 1..10000, pageSize 1..50). 200
    /// <c>{ pageNumber, pageSize, totalCount, items: OpenTextReviewItemDto[] }</c> · 400 OpenTextInvalidQuery · 429 RateLimited.
    /// </summary>
    [HttpGet]
    [Authorize(Roles = ReviewerRoles)]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)] // issue #287
    [EnableRateLimiting(OpenTextReviewReadRateLimiting.Policy)] // review D1: kullanıcı başına (RateLimiting:OpenTextReviewRead), 429
    public async Task<IActionResult> GetQueue([FromQuery] OpenTextReviewQueueQuery query, CancellationToken ct)
    {
        var status = query.Status?.Trim();
        if ((status is not null && status != OpenTextReviewStatuses.Pending && status != OpenTextReviewStatuses.Reviewed && status != "all")
            || query.Page < 1 || query.Page > OpenTextReviewQueueQuery.MaxPage || query.PageSize < 1 || query.PageSize > OpenTextReviewQueueQuery.MaxPageSize
            || query.WorksheetId is <= 0)
        {
            return BadRequest(new OpenTextReviewResultDto
            {
                Success = false,
                InvalidInput = true,
                ErrorCode = OpenTextReviewErrorCodes.InvalidQuery,
                Message = _localizer["worksheets.openTextReview.invalidQuery"]
            });
        }

        var scope = await GetSchoolScopeAsync(ct);
        return Ok(await _service.GetQueueAsync(scope, query, ct));
    }

    /// <summary>
    /// Değerlendir / düzelt: gövde <c>{ evaluation: "sufficient"|"partial"|"insufficient", feedback?, answerHash }</c>. 200
    /// <c>{ success, message, item }</c> · 400 OpenTextInvalidReview · 403 OpenTextReviewForbidden (başkasının değerlendirmesi) ·
    /// 404 OpenTextReviewNotFound · 409 OpenTextNotSubmitted /
    /// OpenTextAnswerChanged · 429 RateLimited.
    /// </summary>
    [HttpPut("{instanceQuestionId:int}")]
    [Authorize(Roles = ReviewerRoles)]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)] // issue #287
    [EnableRateLimiting(OpenTextReviewRateLimiting.Policy)] // kullanıcı başına (RateLimiting:OpenTextReview), 429
    public async Task<IActionResult> Review(int instanceQuestionId, [FromBody] OpenTextReviewRequestDto request, CancellationToken ct)
    {
        var scope = await GetSchoolScopeAsync(ct);
        var result = await _service.ReviewAsync(scope, instanceQuestionId, request, ct);
        if (result.InvalidInput)
            return BadRequest(result);
        if (result.Forbidden)
            return StatusCode(StatusCodes.Status403Forbidden, result);
        if (result.NotFound)
            return NotFound(result);
        if (result.Conflict)
            return Conflict(result);
        return Ok(result);
    }
}
