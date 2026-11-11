using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.ParentLinks;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Issue #1112: velinin kendi ayarları — şimdilik haftalık raporun e-postası (uygulama içi bildirim kapanmaz). Yalnız çağıranın
/// veli profili; çocuk verisi yok. Veli profili yoksa 404 <c>ProfileNotFound</c>.
/// </summary>
[ApiController]
[Route("api/parent/settings")]
[Authorize(Roles = "Parent")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ParentSettingsController : BaseController
{
    private readonly AppDbContext _db;
    private readonly IStringLocalizer<Messages> _localizer;

    public ParentSettingsController(AppDbContext db, IStringLocalizer<Messages>? localizer = null)
    {
        _db = db;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = _localizer["auth.userNotResolved"].Value });

        var optOut = await _db.Parents.AsNoTracking()
            .Where(p => p.UserId == user.Id && !p.IsDeleted)
            .Select(p => (bool?)p.WeeklyReportEmailOptOut)
            .FirstOrDefaultAsync(ct);
        return optOut is null ? ProfileNotFound() : Ok(new ParentSettingsDto { WeeklyReportEmail = !optOut.Value });
    }

    [HttpPut]
    [EnableRateLimiting(ParentLinkRateLimiting.ManagePolicy)]
    public async Task<IActionResult> Update([FromBody] ParentSettingsDto request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = _localizer["auth.userNotResolved"].Value });

        var updated = await _db.Parents
            .Where(p => p.UserId == user.Id && !p.IsDeleted)
            .ExecuteUpdateAsync(u => u.SetProperty(p => p.WeeklyReportEmailOptOut, !request.WeeklyReportEmail), ct);
        return updated == 0 ? ProfileNotFound() : Ok(request);
    }

    private IActionResult ProfileNotFound() => NotFound(new
    {
        message = _localizer["parentLinks.errors.profileNotFound"].Value,
        errorCode = ParentLinkErrorCodes.ProfileNotFound
    });
}
