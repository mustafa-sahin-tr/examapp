using System.Globalization;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Hints;
using ExamApp.Api.Services.Labeling;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Issue #1113: yanlış ya da boş bırakılan soruda adım adım ipucu (Pro, <c>step-hints</c>). Yalnız öğrencinin kendi yanlışları;
/// başka soru 404. Gateway: <c>/api/exam/hints</c>.
/// </summary>
[ApiController]
[Route("api/hints")]
[Authorize(Roles = "Student")]
public class HintsController : BaseController
{
    private readonly IHintService _service;
    private readonly IStringLocalizer<Messages> _localizer;

    public HintsController(IHintService service, IStringLocalizer<Messages>? localizer = null)
    {
        _service = service;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    /// <summary>Açılmış adımlar ve kalan hak; Claude çağrılmaz, hak düşmez.</summary>
    [HttpGet("questions/{questionId:int}")]
    [ProducesResponseType(typeof(HintStateDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Get(int questionId, CancellationToken ct)
    {
        var userId = await UserIdAsync(ct);
        if (userId is null)
            return NotResolved();
        return ToResult(await _service.GetAsync(EntitlementSubject.FromPrincipal(userId.Value, User), questionId, ct));
    }

    /// <summary>Sıradaki adım. 403 planda yok, 429 hak dolu ya da Claude kotası, 503 AI kapalı, 502 üretilemedi.</summary>
    [HttpPost("questions/{questionId:int}/next")]
    [ProducesResponseType(typeof(HintStateDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Next(int questionId, CancellationToken ct)
    {
        var userId = await UserIdAsync(ct);
        if (userId is null)
            return NotResolved();
        try
        {
            var actor = new LabelingActor(KeyCloakId, User.FindFirstValue("name"));
            return ToResult(await _service.RevealNextAsync(EntitlementSubject.FromPrincipal(userId.Value, User), actor, questionId, ct));
        }
        catch (LabelingRateLimitException ex)
        {
            if (ex.RetryAfter is { } retryAfter)
                Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            return StatusCode(StatusCodes.Status429TooManyRequests, Body(ex.Budget ? "budgetExceeded" : "rateLimited", "generationFailed"));
        }
        catch (LabelingAiUnavailableException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, Body("aiUnavailable"));
        }
        catch (HintGenerationException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, Body("generationFailed"));
        }
    }

    private IActionResult ToResult(HintResult result) => result.Error switch
    {
        HintError.None => Ok(result.State),
        HintError.NotInPlan => EntitlementLimitResponse.NotInPlan(EntitlementFeatures.StepHints, result.Quota),
        HintError.QuotaReached => EntitlementLimitResponse.Create(EntitlementFeatures.StepHints, result.Quota!, _localizer["hints.quotaReached"].Value),
        HintError.NotSupported => BadRequest(Body("notSupported")),
        _ => NotFound(Body("notEligible")),
    };

    private object Body(string code, string? messageKey = null) =>
        new { code = $"hints.{code}", message = _localizer[$"hints.{messageKey ?? code}"].Value };

    private async Task<int?> UserIdAsync(CancellationToken ct)
    {
        if (IsServiceAccount)
            return null;
        var user = await GetAuthenticatedUserAsync(ct);
        return IsResolvedUser(user) ? user.Id : null;
    }

    private IActionResult NotResolved() =>
        IsServiceAccount ? Forbid() : UserNotResolved(new { message = _localizer["hints.userNotResolved"].Value });
}
