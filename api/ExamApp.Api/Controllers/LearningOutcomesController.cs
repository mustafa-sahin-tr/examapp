using ExamApp.Api.Data;
using ExamApp.Api.Services.Questions;
using ExamApp.Api.Services.Teachers.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamApp.Api.Controllers;

/// <summary>Issue #692: öğretmenin soruya bağlayacağı kazanımların listesi.</summary>
[ApiController]
[Route("api/learning-outcomes")]
[Authorize(Roles = "Teacher,Admin")]
[Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
public class LearningOutcomesController : ControllerBase
{
    private readonly ILearningOutcomeService _outcomes;

    public LearningOutcomesController(ILearningOutcomeService outcomes) => _outcomes = outcomes;

    [HttpGet]
    public async Task<IActionResult> Search(
        [FromQuery] int gradeId,
        [FromQuery] int subjectId,
        [FromQuery] string? curriculumVersion,
        [FromQuery] string? search,
        CancellationToken ct = default)
    {
        CurriculumVersion? version = null;
        if (!string.IsNullOrWhiteSpace(curriculumVersion))
        {
            if (!CurriculumVersionParser.TryParse(curriculumVersion, out var parsed))
                return BadRequest();
            version = parsed;
        }

        return Ok(await _outcomes.SearchAsync(gradeId, subjectId, version, search, ct));
    }
}
