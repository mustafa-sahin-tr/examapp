using ExamApp.Api.Services.Questions;
using ExamApp.Api.Services.Teachers.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamApp.Api.Controllers;

/// <summary>Issue #703: soruya etiketlenecek TYMM becerileri (KB2, KB3). Liste seed'den gelir, değişmez.</summary>
[ApiController]
[Route("api/skills")]
[Authorize(Roles = "Teacher,Admin")]
[Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
public class SkillsController(ISkillService skills) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct = default) => Ok(await skills.ListAsync(ct));
}
