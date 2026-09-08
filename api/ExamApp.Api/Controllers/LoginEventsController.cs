using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.LoginEvents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Login event yazma ucu (issue #84). Sadece servis hesabına açıktır ("ServiceToService" policy,
/// bkz. Program.cs + <see cref="ExamApp.Foundation.Security.ServicePrincipal"/>); gateway'e route edilmez,
/// BadgeService <c>ExamApi:BaseUrl</c> üzerinden doğrudan çağırır.
/// Okuma/aggregate uçları issue #6 kapsamında ayrı eklenecek.
/// </summary>
[ApiController]
[Route("api/login-events")]
[Authorize(Policy = "ServiceToService")]
public class LoginEventsController : BaseController
{
    private readonly ILoginEventService _loginEventService;

    public LoginEventsController(ILoginEventService loginEventService)
        : base()
    {
        _loginEventService = loginEventService;
    }

    [HttpPost]
    [ProducesResponseType(typeof(LoginEventCreatedDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] LoginEventCreateDto request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.KeycloakUserId))
        {
            return BadRequest(new { message = "keycloakUserId zorunludur." });
        }

        if (string.IsNullOrWhiteSpace(request.Role))
        {
            return BadRequest(new { message = "role zorunludur." });
        }

        if (request.OccurredAtUtc == default)
        {
            return BadRequest(new { message = "occurredAtUtc zorunludur." });
        }

        var result = await _loginEventService.RecordAsync(request, ct);

        // Henüz GET ucu yok (issue #6); CreatedAtAction yerine Location'ı elle veriyoruz.
        return Created($"/api/login-events/{result.Id}", result);
    }
}
