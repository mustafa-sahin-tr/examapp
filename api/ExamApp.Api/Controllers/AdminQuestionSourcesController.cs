using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Questions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamApp.Api.Controllers;

/// <summary>Issue #690: sorunun kaynağı ve lisansı; hak sahibine göre toplu yayından kaldırma.</summary>
[ApiController]
[Route("api/admin/question-sources")]
[Authorize(Roles = "Admin")]
public class AdminQuestionSourcesController : ControllerBase
{
    private readonly IQuestionSourceService _sources;

    public AdminQuestionSourcesController(IQuestionSourceService sources) => _sources = sources;

    [HttpGet]
    public async Task<IActionResult> List(
        [FromQuery] string? rightsHolder,
        [FromQuery] string? sourceType,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        CancellationToken ct = default)
    {
        QuestionSourceType? type = null;
        if (!string.IsNullOrWhiteSpace(sourceType))
        {
            if (!TryParseType(sourceType, out var parsed))
                return BadRequest();
            type = parsed;
        }

        return Ok(await _sources.ListAsync(rightsHolder, type, page, pageSize, ct));
    }

    [HttpPut]
    public async Task<IActionResult> Assign([FromBody] QuestionSourceAssignDto request, CancellationToken ct = default)
    {
        if (!TryParseType(request.SourceType, out var type))
            return BadRequest();

        return Ok(new QuestionSourceActionResultDto { AffectedCount = await _sources.AssignAsync(request, type, ct) });
    }

    /// <summary>Hak sahibinin sorularını yeni test ve pratiklerden çeker ya da geri alır; işlem kayda geçer.</summary>
    [HttpPost("publish-state")]
    public async Task<IActionResult> SetPublished([FromBody] QuestionSourcePublishDto request, CancellationToken ct = default) =>
        Ok(new QuestionSourceActionResultDto { AffectedCount = await _sources.SetPublishedAsync(request, ct) });

    private static bool TryParseType(string value, out QuestionSourceType type) =>
        Enum.TryParse(value, ignoreCase: false, out type) && Enum.IsDefined(type);
}
