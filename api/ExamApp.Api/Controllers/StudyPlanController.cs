using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.ParentLinks;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Services.StudyPlans;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

public class StudyPlanTaskDoneDto
{
    public bool Done { get; set; }
}

/// <summary>Issue #1113: öğrencinin bu haftaki çalışma planı (Pro, <c>weekly-plan</c>). Gateway: <c>/api/exam/study-plan</c>.</summary>
[ApiController]
[Route("api/study-plan")]
[Authorize(Roles = "Student")]
[RequiresEntitlement(EntitlementFeatures.WeeklyPlan, InPlanOnly = true)]
public class StudyPlanController(IStudyPlanService service, IStringLocalizer<Messages> localizer) : BaseController
{
    [HttpGet("current")]
    [ProducesResponseType(typeof(StudyPlanDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Current(CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = localizer["auth.userNotResolved"].Value });
        var plan = await service.GetCurrentForStudentAsync(user.Id, ct);
        return plan is null ? NotFound(new { message = localizer["student.profileNotFound"].Value }) : Ok(plan);
    }

    /// <summary>Görevi yapıldı / yapılmadı işaretler; yalnız bu haftanın planında. Dönüş güncel plan.</summary>
    [HttpPut("tasks/{taskId:int}")]
    [ProducesResponseType(typeof(StudyPlanDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> SetDone(int taskId, [FromBody] StudyPlanTaskDoneDto request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = localizer["auth.userNotResolved"].Value });
        var plan = await service.SetTaskDoneAsync(user.Id, taskId, request.Done, ct);
        return plan is null ? NotFound() : Ok(plan);
    }
}

/// <summary>
/// Issue #1113: çocuğun bu haftaki çalışma planı ve ilerlemesi (salt okunur). Çocuğun planında <c>weekly-plan</c> yoksa 403; başka
/// velinin ya da Active olmayan bağlantının çocuğu 404.
/// </summary>
[ApiController]
[Route("api/parent/children")]
[Authorize(Roles = "Parent")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ParentStudyPlanController(IStudyPlanService service, IStringLocalizer<Messages> localizer) : BaseController
{
    [HttpGet("{studentId:int}/study-plan")]
    [EnableRateLimiting(ParentLinkRateLimiting.ChildActivityPolicy)]
    public async Task<IActionResult> Get(int studentId, [FromServices] IParentChildAccess access, [FromServices] IEntitlementService entitlements,
        CancellationToken ct = default)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = localizer["auth.userNotResolved"].Value });
        if (await ChildEntitlements.DenyUnlessChildHasAsync(access, entitlements, user.Id, studentId, EntitlementFeatures.WeeklyPlan, ct) is { } denied)
            return denied;

        var plan = await service.GetCurrentForParentAsync(user.Id, studentId, ct);
        return plan is null
            ? NotFound(new { message = localizer["parentLinks.errors.childNotFound"].Value, errorCode = ParentLinkErrorCodes.NotFound })
            : Ok(plan);
    }
}
