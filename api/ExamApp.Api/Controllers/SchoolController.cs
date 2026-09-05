using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.Schools;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Public, read-only school list — used by the register flow before the user
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

    [HttpGet]
    public async Task<ActionResult<List<SchoolDto>>> GetSchools(CancellationToken ct)
        => Ok(await _schools.GetAllAsync(ct));
}
