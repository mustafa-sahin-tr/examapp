using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Services.StudentSchoolMemberships;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamApp.Api.Controllers;

/// <summary>
/// issue #861: admin "Bekleyenler" özeti. Gateway: <c>/api/exam/admin/pending-summary</c> (mevcut wildcard route).
/// </summary>
[ApiController]
[Route("api/admin/pending-summary")]
[Authorize(Roles = "Admin")]
public class AdminPendingController : BaseController
{
    private readonly IAdminPendingSummaryService _summary;

    public AdminPendingController(IAdminPendingSummaryService summary)
    {
        _summary = summary;
    }

    /// <summary>GET api/admin/pending-summary → kuyruk başına bekleyen sayısı ve toplam.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(AdminPendingSummaryDto), 200)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        var sub = KeyCloakId;
        if (user == null || user.Id <= 0 || string.IsNullOrWhiteSpace(sub))
            return Unauthorized();

        return Ok(await _summary.GetAsync(new StudentSchoolApprover(user.Id, IsAdmin: true, sub), ct));
    }
}
