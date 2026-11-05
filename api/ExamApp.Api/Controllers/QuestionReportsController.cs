using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.QuestionReports;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Services.Teachers.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ExamApp.Api.Controllers;

/// <summary>
/// issue #781: öğrenci çözdüğü testteki soruyu bildirir; soru sahibi ve admin listeler. Öğrenci erişimi gerektiği için
/// öğretmen yeteneği isteyen <see cref="QuestionsController"/> dışındadır.
/// </summary>
[ApiController]
[Route("api/questions")]
public class QuestionReportsController : BaseController
{
    private readonly IQuestionReportService _reports;

    public QuestionReportsController(IQuestionReportService reports)
    {
        _reports = reports;
    }

    /// <summary>200 + <c>alreadyReported</c>. Soru o oturumda değilse ya da oturum başkasınınsa 404.</summary>
    [HttpPost("{questionId:int}/report")]
    [Authorize(Roles = "Student")]
    [EnableRateLimiting(WorksheetCommentWriteRateLimiting.Policy)]
    public async Task<IActionResult> Create(int questionId, [FromBody] CreateQuestionReportDto dto)
    {
        var user = await GetAuthenticatedUserAsync(HttpContext.RequestAborted);
        var result = await _reports.CreateAsync(questionId, dto, user.Id, HttpContext.RequestAborted);
        return result == null ? NotFound() : Ok(result);
    }

    [HttpGet("reports")]
    [Authorize(Roles = "Teacher,Admin")]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
    public async Task<IActionResult> List(
        [FromQuery] QuestionReportStatus status = QuestionReportStatus.Open,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var user = await GetAuthenticatedUserAsync(HttpContext.RequestAborted);
        return Ok(await _reports.ListAsync(user.Id, IsAdmin, status, page, pageSize, HttpContext.RequestAborted));
    }

    [HttpPut("reports/{id:int}/reviewed")]
    [Authorize(Roles = "Teacher,Admin")]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
    public async Task<IActionResult> MarkReviewed(int id)
    {
        var user = await GetAuthenticatedUserAsync(HttpContext.RequestAborted);
        return await _reports.MarkReviewedAsync(id, user.Id, IsAdmin, HttpContext.RequestAborted) ? NoContent() : NotFound();
    }
}
