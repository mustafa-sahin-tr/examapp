using ExamApp.Api.Helpers;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Api.Services.Practice;
using ExamApp.Api.Services.StudentQuestions;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Issue #510 (assistant Phase 2): explanation context for a question the student already answered, and their recent
/// mistakes. The student comes only from the token. Read by the assistant's <c>explain_question</c> and
/// <c>get_my_recent_mistakes</c> tools with the student's own token.
/// </summary>
[ApiController]
[Route("api/student/me")]
[Authorize(Roles = "Student")]
public class StudentQuestionExplainController(
    IStudentQuestionExplainService service,
    IStudentService students,
    IPracticeSessionService practice,
    IStringLocalizer<Messages> localizer) : BaseController
{
    public const string NotAnsweredCode = "QuestionNotAnswered";
    public const string InOpenTestCode = "QuestionInOpenTest";

    /// <summary>
    /// 404 <see cref="NotAnsweredCode"/> when the student has not answered it (practice) or seen it in a finished test;
    /// 403 <see cref="InOpenTestCode"/> when it is in a test they are solving or an assigned test they have not finished.
    /// </summary>
    [HttpGet("questions/{questionId:int}/explain-context")]
    [EnableRateLimiting(TopicPerformanceRateLimiting.Policy)]
    public async Task<IActionResult> GetExplainContext(int questionId, CancellationToken ct = default)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return Unauthorized();
        var student = await students.GetStudentProfile(user.Id);
        if (student is null)
            return NotFound(new { message = localizer["student.profileNotFound"].Value });

        var result = await service.GetExplainContextAsync(student.Id, student.GradeId, student.SchoolId, questionId, ct);
        return result.Status switch
        {
            ExplainContextStatus.Ok => Ok(result.Context),
            ExplainContextStatus.InOpenTest => StatusCode(StatusCodes.Status403Forbidden,
                new { message = localizer["student.explain.inOpenTest"].Value, errorCode = InOpenTestCode }),
            _ => NotFound(new { message = localizer["student.explain.notAnswered"].Value, errorCode = NotAnsweredCode }),
        };
    }

    /// <summary>Wrong answers in finished tests and practice, one row per question, newest first.</summary>
    [HttpGet("mistakes")]
    [EnableRateLimiting(TopicPerformanceRateLimiting.Policy)]
    public async Task<IActionResult> GetMistakes(
        [FromQuery] int? subjectId = null,
        [FromQuery] int? topicId = null,
        [FromQuery] int days = 30,
        [FromQuery] int take = StudentQuestionExplainService.MaxTake,
        CancellationToken ct = default)
    {
        if (days is < 1 or > StudentQuestionExplainService.MaxDays || take is < 1 or > StudentQuestionExplainService.MaxTake)
            return BadRequest(new { message = localizer["student.explain.invalidQuery"].Value, errorCode = "InvalidQuery" });

        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return Unauthorized();
        var student = await students.GetStudentProfile(user.Id);
        if (student is null)
            return NotFound(new { message = localizer["student.profileNotFound"].Value });

        return Ok(await service.GetMistakesAsync(student.Id, days, subjectId, topicId, take, ct));
    }

    /// <summary>
    /// Issue #526: the questions the student struggled with most (wrong or blank, most misses first), to solve again with
    /// <c>POST api/practice/sessions { questionIds }</c>. <c>mode=global</c>: questions most students got wrong (nightly stats,
    /// at least <c>HardQuestions:MinStudents</c> students; <c>days</c> does not apply).
    /// </summary>
    [HttpGet("hard-questions")]
    [EnableRateLimiting(TopicPerformanceRateLimiting.Policy)]
    public async Task<IActionResult> GetHardQuestions(
        [FromQuery] string? mode = null,
        [FromQuery] int? subjectId = null,
        [FromQuery] int? topicId = null,
        [FromQuery] int? subTopicId = null,
        [FromQuery] int days = HardQuestionQueries.DefaultDays,
        [FromQuery] int take = HardQuestionQueries.DefaultTake,
        CancellationToken ct = default)
    {
        var global = string.Equals(mode, "global", StringComparison.OrdinalIgnoreCase);
        if ((mode is not null && !global && !string.Equals(mode, "mine", StringComparison.OrdinalIgnoreCase))
            || days is < 1 or > HardQuestionQueries.MaxDays || take is < 1 or > HardQuestionQueries.MaxTake)
            return BadRequest(new { message = localizer["student.explain.invalidQuery"].Value, errorCode = "InvalidQuery" });

        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return Unauthorized();
        var student = await students.GetStudentProfile(user.Id);
        if (student is null)
            return NotFound(new { message = localizer["student.profileNotFound"].Value });

        var filter = new HardQuestionFilter(days, subjectId is { } s ? [s] : [], topicId, subTopicId);
        return Ok(await practice.GetHardQuestionsAsync(student, filter, take, global, ct));
    }

    /// <summary>
    /// Issue #528: subtopics where the student keeps picking the same popular distractor (<c>days</c> 1–90, <c>take</c> 1–10).
    /// Examples carry the student's own choice, never the correct one; explain one with <c>explain-context</c> (#510).
    /// </summary>
    [HttpGet("repeated-traps")]
    [EnableRateLimiting(TopicPerformanceRateLimiting.Policy)]
    public async Task<IActionResult> GetRepeatedTraps(
        [FromQuery] int? subjectId = null,
        [FromQuery] int? topicId = null,
        [FromQuery] int days = HardQuestionQueries.DefaultDays,
        [FromQuery] int take = 5,
        CancellationToken ct = default)
    {
        if (days is < 1 or > HardQuestionQueries.MaxDays || take is < 1 or > PracticeSessionService.RepeatedTrapsMaxTake)
            return BadRequest(new { message = localizer["student.explain.invalidQuery"].Value, errorCode = "InvalidQuery" });

        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return Unauthorized();
        var student = await students.GetStudentProfile(user.Id);
        if (student is null)
            return NotFound(new { message = localizer["student.profileNotFound"].Value });

        return Ok(await practice.GetRepeatedTrapsAsync(student, new HardQuestionFilter(days, subjectId is { } s ? [s] : [], topicId, null), take, ct));
    }
}
