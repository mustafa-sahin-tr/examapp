using System;
using System.Threading;
using System.Threading.Tasks;
using BadgeService.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BadgeService.Controllers;

[ApiController]
[Route("api/reset")]
[Authorize(Policy = "Service")]
public class ResetController : ControllerBase
{
    private readonly UserResetService _resetService;

    public ResetController(UserResetService resetService)
    {
        _resetService = resetService;
    }

    /// <param name="resetAtUtc">
    /// issue #396: sıfırlama çizgisi (exam API saati, ISO 8601 + ofset). Bundan önce gönderilmiş AnswerSubmittedEvent'ler
    /// yok sayılır. Verilmezse BadgeService saati kullanılır (eski çağıranlarla uyumlu).
    /// </param>
    [HttpDelete("users/{userId:int}")]
    public async Task<IActionResult> ResetUserAsync(
        int userId, [FromQuery] DateTimeOffset? resetAtUtc, CancellationToken cancellationToken)
    {
        if (userId <= 0)
        {
            return BadRequest(new { message = "Invalid userId" });
        }

        await _resetService.ResetAsync(userId, resetAtUtc?.UtcDateTime, cancellationToken);

        return Ok(new { message = "User badge/activity data reset." });
    }
}
