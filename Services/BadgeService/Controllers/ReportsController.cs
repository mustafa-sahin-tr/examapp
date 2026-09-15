using System;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using BadgeService.Models;
using BadgeService.Security;
using BadgeService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BadgeService.Controllers;

[ApiController]
[Route("api/reports")]
[Authorize]
public class ReportsController : ControllerBase
{
    private readonly StudentReportService _reportService;
    private readonly ICallerIdentityResolver _callerIdentityResolver;
    private readonly ILogger<ReportsController> _logger;

    public ReportsController(
        StudentReportService reportService,
        ICallerIdentityResolver callerIdentityResolver,
        ILogger<ReportsController> logger)
    {
        _reportService = reportService;
        _callerIdentityResolver = callerIdentityResolver;
        _logger = logger;
    }

    [HttpGet("users/{userId:int}/badge-progress")]
    public async Task<ActionResult<BadgeProgressReportDto>> GetBadgeProgressAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        if (!await CanViewReportAsync(userId, cancellationToken))
        {
            return Forbid();
        }

        var report = await _reportService.GetBadgeProgressAsync(userId, cancellationToken);
        return Ok(report);
    }

    [HttpGet("users/{userId:int}/activity")]
    public async Task<ActionResult<ActivityReportDto>> GetActivityAsync(
        int userId,
        [FromQuery(Name = "startUtc")] DateTime? startUtc,
        [FromQuery(Name = "endUtc")] DateTime? endUtc,
        CancellationToken cancellationToken)
    {
        if (!await CanViewReportAsync(userId, cancellationToken))
        {
            return Forbid();
        }

        var report = await _reportService.GetActivityReportAsync(userId, startUtc, endUtc, cancellationToken);
        return Ok(report);
    }

    /// <summary>
    /// IDOR koruması (issue #165): route'taki <paramref name="requestedUserId"/> ile token'daki
    /// kimliğin aynı kişi olduğunu doğrular. Admin rolü açık istisnadır. Rapor servisi ancak bu
    /// kontrol geçtikten sonra çağrılır; aksi halde 403 dönülür (200 + başkasının verisi değil).
    /// </summary>
    private async Task<bool> CanViewReportAsync(int requestedUserId, CancellationToken cancellationToken)
    {
        // KeycloakRoleTransformer realm_access.roles'u ClaimTypes.Role'e projekte ediyor.
        var isAdmin = User.IsInRole("Admin");

        // Admin zaten her raporu görebilir; bu durumda auth-api'ye gereksiz çağrı yapmıyoruz.
        var callerUserId = isAdmin
            ? null
            : await _callerIdentityResolver.ResolveUserIdAsync(HttpContext, cancellationToken);

        // Tek karar noktası: kural ReportAccess.CanView içinde.
        if (ReportAccess.CanView(callerUserId, requestedUserId, isAdmin))
        {
            return true;
        }

        _logger.LogWarning(
            "[Reports] Yetkisiz rapor erişimi reddedildi. sub={Sub}, callerUserId={CallerUserId}, requestedUserId={RequestedUserId}",
            User.FindFirstValue(ClaimTypes.NameIdentifier),
            callerUserId,
            requestedUserId);

        return false;
    }
}
