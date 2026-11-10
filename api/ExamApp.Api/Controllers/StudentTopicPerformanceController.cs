using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Entitlements;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.TopicPerformance;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>Issue #497: öğrencinin kendi konu performansı. Öğrenci kimliği yalnız token'dan gelir.</summary>
[ApiController]
[Route("api/student/me/topic-performance")]
[Authorize(Roles = "Student")]
[RequiresEntitlement(EntitlementFeatures.Analytics, InPlanOnly = true)] // #1089: konu performansı ve haftalık gelişim Pro
public class StudentTopicPerformanceController(
    ITopicPerformanceService service,
    ITopicProgressTimelineService timeline,
    IStudentService students,
    IStringLocalizer<Messages> localizer) : BaseController
{
    [HttpGet]
    [EnableRateLimiting(TopicPerformanceRateLimiting.Policy)]
    public async Task<IActionResult> Get(
        [FromQuery] int days = TopicPerformanceQuery.DefaultDays,
        [FromQuery] int? subjectId = null,
        [FromQuery] string groupBy = TopicPerformanceQuery.DefaultGroupBy,
        [FromQuery] int minQuestions = TopicPerformanceQuery.DefaultMinQuestions,
        CancellationToken ct = default)
    {
        if (!TopicPerformanceQuery.TryParse(days, groupBy, minQuestions, out var grouping, out var problem))
            return BadRequest(new { message = localizer[problem!.MessageKey].Value, errorCode = problem.ErrorCode });

        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return Unauthorized();
        var student = await students.GetStudentProfile(user.Id);
        if (student is null)
            return NotFound(new { message = localizer["student.profileNotFound"].Value });

        var to = DateTime.UtcNow;
        return Ok(await service.GetAsync(student.Id, to.AddDays(-days), to, subjectId, grouping, minQuestions, ct));
    }

    /// <summary>Issue #531: bir konu ya da alt konunun haftalık doğruluğu.</summary>
    [HttpGet("timeline")]
    [EnableRateLimiting(TopicPerformanceRateLimiting.Policy)]
    public async Task<IActionResult> GetTimeline(
        [FromQuery] int? topicId = null,
        [FromQuery] int? subTopicId = null,
        [FromQuery] int weeks = TopicPerformanceQuery.DefaultTimelineWeeks,
        CancellationToken ct = default)
    {
        if (!TopicPerformanceQuery.TryParseTimeline(topicId, subTopicId, weeks, out var problem))
            return BadRequest(new { message = localizer[problem!.MessageKey].Value, errorCode = problem.ErrorCode });

        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return Unauthorized();
        var student = await students.GetStudentProfile(user.Id);
        if (student is null)
            return NotFound(new { message = localizer["student.profileNotFound"].Value });

        return Ok(await timeline.GetAsync(student.Id, topicId, subTopicId, weeks, ct));
    }
}
