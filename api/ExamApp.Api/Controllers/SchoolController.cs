using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.Schools;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Public, read-only school search — used by the register flow before the user
/// is authenticated. Write access lives under AdminController (api/admin/schools).
/// </summary>
[ApiController]
[Route("api/school")]
[AllowAnonymous]
public class SchoolController : ControllerBase
{
    private readonly ISchoolService _schools;

    public SchoolController(ISchoolService schools)
    {
        _schools = schools;
    }

    /// <summary>
    /// GET api/school?q=&amp;limit= → okul seçici araması. Kelimeler ad, ilçe ve ilde aranır; q yoksa ilk okullar döner.
    /// Liste 54 binin üstünde okul taşıdığı için tamamı hiçbir zaman dönmez.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<SchoolOptionDto>>> GetSchools(
        [FromQuery] string? q = null,
        [FromQuery] int limit = SchoolService.MaxSearchLimit,
        CancellationToken ct = default)
        => Ok(await _schools.SearchAsync(q, limit, ct));

    /// <summary>GET api/school/{id} → kayıtlı seçimin adını göstermek için tek okul; yoksa 404.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<SchoolOptionDto>> GetSchool(int id, CancellationToken ct)
    {
        var school = await _schools.GetOptionAsync(id, ct);
        return school == null ? NotFound() : Ok(school);
    }
}
