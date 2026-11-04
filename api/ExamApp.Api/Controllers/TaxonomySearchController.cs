using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services.TaxonomySearch;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>Issue #499: ad ile ders, konu ve alt konu araması. Taksonomi herkese açık veridir; giriş yapmak yeterli.</summary>
[ApiController]
[Route("api/subject/search")]
[Authorize]
public class TaxonomySearchController(
    ITaxonomySearchService service,
    IStringLocalizer<Messages> localizer) : BaseController
{
    private const int DefaultTake = 10;

    /// <param name="gradeId">Boşsa öğrencide kendi sınıfı, diğer rollerde bütün sınıflar.</param>
    [HttpGet]
    [EnableRateLimiting(TaxonomySearchRateLimiting.Policy)]
    public async Task<IActionResult> Search([FromQuery] string? q, [FromQuery] int? gradeId = null, [FromQuery] int take = DefaultTake, CancellationToken ct = default)
    {
        if (!TaxonomySearchService.IsValidQuery(q))
            return BadRequest(new { message = localizer["taxonomy.search.invalidQuery"].Value, errorCode = "InvalidQuery" });
        if (take is < 1 or > TaxonomySearchService.MaxTake)
            return BadRequest(new { message = localizer["taxonomy.search.invalidTake"].Value, errorCode = "InvalidTake" });

        if (gradeId is null && User.IsInRole(nameof(UserRole.Student)))
        {
            var user = await GetAuthenticatedUserAsync(ct);
            if (IsResolvedUser(user))
                gradeId = await service.GetStudentGradeIdAsync(user.Id, ct);
        }

        return Ok(await service.SearchAsync(q!.Trim(), gradeId, take, ct));
    }
}
