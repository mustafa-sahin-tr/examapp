using System;
using System.Threading;
using System.Threading.Tasks;
using BadgeService.Models;
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

    public ReportsController(StudentReportService reportService)
    {
        _reportService = reportService;
    }

    [HttpGet("users/{userId:int}/badge-progress")]
    public async Task<ActionResult<BadgeProgressReportDto>> GetBadgeProgressAsync(
        int userId,
        CancellationToken cancellationToken)
    {
        var report = await _reportService.GetBadgeProgressAsync(userId, cancellationToken);

        if (report == null)
        {
            return NotFound();
        }

        return Ok(report);
    }

    [HttpGet("users/{userId:int}/activity")]
    public async Task<ActionResult<ActivityReportDto>> GetActivityAsync(
        int userId,
        [FromQuery(Name = "startUtc")] DateTime? startUtc,
        [FromQuery(Name = "endUtc")] DateTime? endUtc,
        CancellationToken cancellationToken)
    {
        var report = await _reportService.GetActivityReportAsync(userId, startUtc, endUtc, cancellationToken);
        return Ok(report);
    }
}
