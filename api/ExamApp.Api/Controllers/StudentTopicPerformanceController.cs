using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
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
public class StudentTopicPerformanceController(
    ITopicPerformanceService service,
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
}
