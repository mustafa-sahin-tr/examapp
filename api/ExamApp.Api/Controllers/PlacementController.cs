using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.ParentLinks;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Placement;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Issue #1114: ücretsiz seviye tespiti. Plan kilidi (#1090) altındaki öğrenci de çözer: plan seçmeden önce rapor ve öneri görür.
/// Gateway: <c>/api/exam/placement</c>.
/// </summary>
[ApiController]
[Route("api/placement")]
[Authorize(Roles = "Student")]
[AllowWithoutPlan]
public class PlacementController(IPlacementService service, IStringLocalizer<Messages> localizer) : BaseController
{
    [HttpGet("status")]
    [ProducesResponseType(typeof(PlacementStatusDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Status(CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = localizer["auth.userNotResolved"].Value });
        return Ok(await service.StatusAsync(user.Id, ct));
    }

    [HttpGet("questions")]
    [ProducesResponseType(typeof(List<PlacementQuestionDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Questions(CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = localizer["auth.userNotResolved"].Value });
        var result = await service.QuestionsAsync(user.Id, ct);
        return result.Error == PlacementError.None ? Ok(result.Value) : Error(result.Error);
    }

    /// <summary>Cevapları tek seferde puanlar; boş bırakılan soru <c>answerId=null</c> ya da hiç gönderilmez.</summary>
    [HttpPost("submit")]
    [ProducesResponseType(typeof(PlacementReportDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Submit([FromBody] PlacementSubmitDto request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = localizer["auth.userNotResolved"].Value });
        var result = await service.SubmitAsync(user.Id, request, ct);
        return result.Error == PlacementError.None ? Ok(result.Value) : Error(result.Error);
    }

    [HttpGet("report")]
    [ProducesResponseType(typeof(PlacementReportDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Report(CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = localizer["auth.userNotResolved"].Value });
        var report = await service.ReportAsync(user.Id, ct);
        return report is null ? NotFound(new { message = localizer["placement.notTaken"].Value }) : Ok(report);
    }

    private IActionResult Error(PlacementError error) => error switch
    {
        PlacementError.AlreadyTaken => Conflict(new { message = localizer["placement.alreadyTaken"].Value, errorCode = "alreadyTaken" }),
        PlacementError.InvalidAnswers => BadRequest(new { message = localizer["placement.invalidAnswers"].Value }),
        _ => NotFound(new { message = localizer["placement.notAvailable"].Value }),
    };
}

/// <summary>Issue #1114: sınıf başına seviye tespit testini üretir ve yayımlar.</summary>
[ApiController]
[Route("api/admin/placement-tests")]
[Authorize(Roles = "Admin,SuperAdmin")]
public class AdminPlacementTestsController(IPlacementService service, IStringLocalizer<Messages> localizer) : BaseController
{
    [HttpGet]
    [ProducesResponseType(typeof(List<AdminPlacementTestDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await service.ListAsync(ct));

    /// <summary>Testi bankadan (yeniden) üretir; yayım durumu korunur, çözülmüş raporlar etkilenmez.</summary>
    [HttpPost("{gradeId:int}/generate")]
    [ProducesResponseType(typeof(AdminPlacementTestDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Generate(int gradeId, CancellationToken ct)
    {
        var result = await service.GenerateAsync(gradeId, ct);
        return result.Error switch
        {
            PlacementError.None => Ok(result.Value),
            PlacementError.NotEnoughQuestions => Conflict(new { message = localizer["placement.notEnoughQuestions"].Value, errorCode = "notEnoughQuestions" }),
            _ => NotFound(),
        };
    }

    [HttpPut("{gradeId:int}/published")]
    [ProducesResponseType(typeof(AdminPlacementTestDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetPublished(int gradeId, [FromBody] AdminPlacementPublishDto request, CancellationToken ct)
    {
        var result = await service.SetPublishedAsync(gradeId, request.Published, ct);
        return result.Error == PlacementError.None ? Ok(result.Value) : NotFound();
    }
}

/// <summary>Issue #1114: çocuğun seviye tespiti raporu (ücretsiz; plan gerekmez). Başka velinin çocuğu 404, rapor yoksa 204.</summary>
[ApiController]
[Route("api/parent/children")]
[Authorize(Roles = "Parent")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ParentPlacementController(IPlacementService service, IStringLocalizer<Messages> localizer) : BaseController
{
    [HttpGet("{studentId:int}/placement")]
    [EnableRateLimiting(ParentLinkRateLimiting.ChildActivityPolicy)]
    public async Task<IActionResult> Get(int studentId, CancellationToken ct = default)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = localizer["auth.userNotResolved"].Value });
        var (found, report) = await service.ReportForParentAsync(user.Id, studentId, ct);
        if (!found)
            return NotFound(new { message = localizer["parentLinks.errors.childNotFound"].Value, errorCode = ParentLinkErrorCodes.NotFound });
        return report is null ? NoContent() : Ok(report);
    }
}
