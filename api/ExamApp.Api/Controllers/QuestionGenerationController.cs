using System.Globalization;
using System.Security.Claims;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Labeling;
using ExamApp.Api.Services.QuestionGeneration;
using ExamApp.Api.Services.Teachers.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Kazanımdan görselsiz soru üretimi. Claude çağrısı ücretli olduğu için işaretleme ekranıyla aynı kişilere açıktır ve aynı kotayı kullanır.
/// Üretilen taslak kaydedilmez; kayıt, incelenen sorularla ayrı istekte yapılır.
/// Issue #1087: öğretmen de girer; <c>question-generation</c> planına bağlıdır, üretilen her taslak sayılır.
/// </summary>
[ApiController]
[Route("api/question-generation")]
[Authorize(Roles = "Admin,QuestionTrainer,Teacher")]
[Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
[RequiresEntitlement(EntitlementFeatures.QuestionGeneration, InPlanOnly = true)]
public class QuestionGenerationController : BaseController
{
    private readonly IQuestionGenerationService _service;

    public QuestionGenerationController(IQuestionGenerationService service)
    {
        _service = service;
    }

    [HttpPost("drafts")]
    [RequestSizeLimit(64 * 1024)]
    [RequiresEntitlement(EntitlementFeatures.QuestionGeneration)]
    public async Task<IActionResult> GenerateDrafts(
        [FromBody] GenerateQuestionDraftsDto request, [FromServices] IEntitlementService entitlements, CancellationToken ct)
    {
        try
        {
            var response = await _service.GenerateAsync(request, CurrentActor(), ct);
            if (response is null)
                return NotFound(new { errorCode = "LearningOutcomeNotFound" });
            var user = await GetAuthenticatedUserAsync(ct);
            if (IsResolvedUser(user))
            {
                foreach (var _ in response.Drafts)
                    await entitlements.RecordUsageAsync(user.Id, EntitlementFeatures.QuestionGeneration, $"question-generation:{Guid.NewGuid():N}", ct);
            }
            return Ok(response);
        }
        catch (LabelingRateLimitException ex)
        {
            return RateLimited(ex);
        }
        catch (LabelingAiUnavailableException)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new { errorCode = "AiUnavailable" });
        }
        catch (QuestionDraftGenerationException)
        {
            return StatusCode(StatusCodes.Status502BadGateway, new { errorCode = "GenerationFailed" });
        }
    }

    [HttpPost("save")]
    [RequestSizeLimit(1024 * 1024)]
    public async Task<IActionResult> Save([FromBody] SaveQuestionDraftsDto request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return Unauthorized();
        try
        {
            var saved = await _service.SaveAsync(request, CurrentActor(), user.Id, ct);
            return saved is null ? NotFound(new { errorCode = "LearningOutcomeNotFound" }) : Ok(saved);
        }
        catch (QuestionDraftInvalidException ex)
        {
            return BadRequest(new { errorCode = "InvalidQuestion", message = ex.Message });
        }
        catch (LabelingRateLimitException ex)
        {
            return RateLimited(ex);
        }
    }

    private LabelingActor CurrentActor() => new(KeyCloakId, User.FindFirstValue("name"), IsAdmin);

    private ObjectResult RateLimited(LabelingRateLimitException ex)
    {
        if (ex.RetryAfter is { } retryAfter)
            Response.Headers.RetryAfter = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
        return StatusCode(StatusCodes.Status429TooManyRequests, new { errorCode = ex.Budget ? "BudgetExceeded" : "RateLimited" });
    }
}
