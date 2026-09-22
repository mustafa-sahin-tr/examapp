using ExamApp.Api.Helpers;
using ExamApp.Api.Services;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Contracts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Dev-only toplu kullanıcı oluşturma ucu (issue #217). Koruma katmanları: (1) servis yalnızca
/// Development/Staging'de DI'a kayıtlı — yoksa 404; (2) controller ortamı ayrıca denetler — 404;
/// (3) yalnızca servis token'ı (<c>Service</c> policy — exam-admin client_credentials, BadgeService
/// ResetController ile aynı desen); (4) servis kendisi de ortamı denetler (<see cref="DevSeedEnvironmentException"/> → 404);
/// (5) gateway <c>/api/auth/dev/*</c> yolunu engeller — CLI <c>AuthApiBaseUrl</c> ile doğrudan gelir.
/// </summary>
[Route("api/auth/dev")]
[ApiController]
[Authorize(Policy = "Service")]
public class DevSeedController : ControllerBase
{
    private readonly IDevUserSeedService? _seedService;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<DevSeedController> _logger;

    // Servis Production'da kayıtlı değildir; DI varsayılan değerli parametreyi null bırakır.
    public DevSeedController(IHostEnvironment environment, ILogger<DevSeedController> logger, IDevUserSeedService? seedService = null)
    {
        _seedService = seedService;
        _environment = environment;
        _logger = logger;
    }

    [HttpPost("seed-users")]
    [ProducesResponseType(typeof(DevSeedUsersResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SeedUsers([FromBody] DevSeedUsersRequest request, CancellationToken ct)
    {
        if (_seedService is null || !DevUserSeedService.IsAllowedEnvironment(_environment))
            return NotFound();

        try
        {
            var result = await _seedService.SeedAsync(request, ct);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (DevSeedEnvironmentException ex)
        {
            _logger.LogWarning(ex, "dev seed-users reddedildi");
            return NotFound();
        }
    }

    /// <summary>
    /// Seed hesaplarını Keycloak + identity'den kaldırır (issue #218). Kapsam sabittir (seed alanı +
    /// <c>IsSeedData=true</c>); istek yalnızca <c>ExcludeUserIds</c> ile daraltır. <c>DryRun=true</c> varsayılan.
    /// Aynı koruma katmanları: DI guard, ortam guard'ı, <c>Service</c> policy, gateway engeli.
    /// </summary>
    [HttpPost("seed-users/cleanup")]
    [ProducesResponseType(typeof(DevSeedCleanupResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> CleanupSeedUsers([FromBody] DevSeedCleanupRequest request, CancellationToken ct)
    {
        if (_seedService is null || !DevUserSeedService.IsAllowedEnvironment(_environment))
            return NotFound();

        try
        {
            var result = await _seedService.CleanupAsync(request, ct);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { message = ex.Message });
        }
        catch (DevSeedEnvironmentException ex)
        {
            _logger.LogWarning(ex, "dev seed-cleanup reddedildi");
            return NotFound();
        }
    }
}
