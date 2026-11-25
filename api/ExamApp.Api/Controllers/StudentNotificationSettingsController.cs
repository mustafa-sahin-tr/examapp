using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>Issue #519: the student's own notification settings. <c>StudyNudge</c>: the "you have not studied for a few days" nudge.</summary>
public sealed class StudentNotificationSettingsDto
{
    public bool StudyNudge { get; set; }
}

/// <summary>Issue #519: GET / PUT the calling student's notification settings; 404 without a student profile.</summary>
[ApiController]
[Route("api/student/me/notification-settings")]
[Authorize(Roles = "Student")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class StudentNotificationSettingsController(AppDbContext db, IStringLocalizer<Messages>? localizer = null) : BaseController
{
    private readonly IStringLocalizer<Messages> _localizer = localizer ?? FallbackMessageLocalizer.Instance;

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = _localizer["auth.userNotResolved"].Value });

        var optOut = await db.Students.AsNoTracking()
            .Where(s => s.UserId == user.Id)
            .Select(s => (bool?)s.StudyNudgeOptOut)
            .FirstOrDefaultAsync(ct);
        return optOut is null ? ProfileNotFound() : Ok(new StudentNotificationSettingsDto { StudyNudge = !optOut.Value });
    }

    [HttpPut]
    [EnableRateLimiting(ParentLinkRateLimiting.ManagePolicy)]
    public async Task<IActionResult> Update([FromBody] StudentNotificationSettingsDto request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = _localizer["auth.userNotResolved"].Value });

        var updated = await db.Students
            .Where(s => s.UserId == user.Id)
            .ExecuteUpdateAsync(u => u.SetProperty(s => s.StudyNudgeOptOut, !request.StudyNudge), ct);
        return updated == 0 ? ProfileNotFound() : Ok(request);
    }

    private IActionResult ProfileNotFound() => NotFound(new { message = _localizer["student.profileNotFound"].Value });
}
