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
    private const int MaxDays = 90;
    private const int MaxMinQuestions = 1000;

    private static readonly Dictionary<string, TopicPerformanceGroupBy> GroupByValues = new(StringComparer.OrdinalIgnoreCase)
    {
        ["topic"] = TopicPerformanceGroupBy.Topic,
        ["subtopic"] = TopicPerformanceGroupBy.SubTopic,
        ["difficulty"] = TopicPerformanceGroupBy.Difficulty,
        ["interactionType"] = TopicPerformanceGroupBy.InteractionType,
    };

    [HttpGet]
    [EnableRateLimiting(TopicPerformanceRateLimiting.Policy)]
    public async Task<IActionResult> Get(
        [FromQuery] int days = 30,
        [FromQuery] int? subjectId = null,
        [FromQuery] string groupBy = "topic",
        [FromQuery] int minQuestions = 5,
        CancellationToken ct = default)
    {
        if (days is < 1 or > MaxDays)
            return Invalid("InvalidDays", "student.topicPerformance.invalidDays");
        if (!GroupByValues.TryGetValue(groupBy, out var grouping))
            return Invalid("InvalidGroupBy", "student.topicPerformance.invalidGroupBy");
        if (minQuestions is < 1 or > MaxMinQuestions)
            return Invalid("InvalidMinQuestions", "student.topicPerformance.invalidMinQuestions");

        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return Unauthorized();
        var student = await students.GetStudentProfile(user.Id);
        if (student is null)
            return NotFound(new { message = localizer["student.profileNotFound"].Value });

        var to = DateTime.UtcNow;
        return Ok(await service.GetAsync(student.Id, to.AddDays(-days), to, subjectId, grouping, minQuestions, ct));
    }

    private BadRequestObjectResult Invalid(string errorCode, string messageKey) =>
        BadRequest(new { message = localizer[messageKey].Value, errorCode });
}
