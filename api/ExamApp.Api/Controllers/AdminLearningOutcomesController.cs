using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Questions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamApp.Api.Controllers;

/// <summary>Issue #692: resmî MEB listesinin içe aktarımı; aynı listeyi yeniden göndermek kopya üretmez.</summary>
[ApiController]
[Route("api/admin/learning-outcomes")]
[Authorize(Roles = "Admin")]
public class AdminLearningOutcomesController : BaseController
{
    private readonly ILearningOutcomeService _outcomes;

    public AdminLearningOutcomesController(ILearningOutcomeService outcomes) => _outcomes = outcomes;

    [HttpPost("import")]
    public async Task<IActionResult> Import([FromBody] LearningOutcomeImportDto request, CancellationToken ct = default)
    {
        if (!CurriculumVersionParser.TryParse(request.CurriculumVersion, out var version))
            return BadRequest();

        var user = await GetAuthenticatedUserAsync(ct);
        var result = await _outcomes.ImportAsync(request, version, user.Id, ct);
        return result.Errors.Count > 0 ? BadRequest(result) : Ok(result);
    }
}
