using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.Locations;
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
    private readonly ILocationService _locations;

    public SchoolController(ISchoolService schools, ILocationService locations)
    {
        _schools = schools;
        _locations = locations;
    }

    /// <summary>
    /// GET api/school?q=&amp;provinceId=&amp;districtId=&amp;limit= → okul seçici araması. Kelimeler ad, ilçe ve ilde aranır;
    /// il / ilçe verilirse onunla daraltılır (#1106); q yoksa ilk okullar döner. Liste 54 binin üstünde okul taşıdığı için
    /// tamamı hiçbir zaman dönmez.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<SchoolOptionDto>>> GetSchools(
        [FromQuery] string? q = null,
        [FromQuery] int? provinceId = null,
        [FromQuery] int? districtId = null,
        [FromQuery] int limit = SchoolService.MaxSearchLimit,
        CancellationToken ct = default)
        => Ok(await _schools.SearchAsync(q, provinceId, districtId, limit, ct));

    /// <summary>GET api/school/provinces → okul seçicinin il filtresi (#1106); admin listesiyle aynı referans veri.</summary>
    [HttpGet("provinces")]
    public async Task<ActionResult<List<ProvinceDto>>> GetProvinces(CancellationToken ct)
        => Ok(await _locations.GetProvincesAsync(ct));

    /// <summary>GET api/school/districts?provinceId=6 → o ilin ilçeleri; bilinmeyen il → boş liste.</summary>
    [HttpGet("districts")]
    public async Task<ActionResult<List<DistrictDto>>> GetDistricts([FromQuery] int provinceId, CancellationToken ct)
        => Ok(await _locations.GetDistrictsAsync(provinceId, ct));

    /// <summary>GET api/school/{id} → kayıtlı seçimin adını göstermek için tek okul; yoksa 404.</summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<SchoolOptionDto>> GetSchool(int id, CancellationToken ct)
    {
        var school = await _schools.GetOptionAsync(id, ct);
        return school == null ? NotFound() : Ok(school);
    }
}
