using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.LoginEvents;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;

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

    // Client'a dönen tüm metinler mesaj sözlüğünden gelir (issue #184).
    // DI her zaman gerçek localizer'ı verir; parametre yalnızca DI'siz kurulan (birim test)
    // senaryolarda varsayılan dile düşebilmek için opsiyonel.
    private readonly IStringLocalizer<Messages> _localizer;

    public LoginEventsController(ILoginEventService loginEventService, IStringLocalizer<Messages>? localizer = null)
        : base()
    {
        _loginEventService = loginEventService;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    [HttpPost]
    [ProducesResponseType(typeof(LoginEventCreatedDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Create([FromBody] LoginEventCreateDto request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Role))
        {
            return BadRequest(new { message = _localizer["loginEvents.roleRequired"].Value });
        }

        if (request.OccurredAtUtc == default)
        {
            return BadRequest(new { message = _localizer["loginEvents.occurredAtUtcRequired"].Value });
        }

        // KeycloakUserId kuralı (Success=true ise zorunlu, issue #100) serviste.
        var result = await _loginEventService.RecordAsync(request, ct);
        if (result.ErrorKey is not null)
        {
            return BadRequest(new { message = _localizer[result.ErrorKey].Value });
        }

        // Henüz GET ucu yok (issue #6); CreatedAtAction yerine Location'ı elle veriyoruz.
        return Created($"/api/login-events/{result.Created!.Id}", result.Created);
    }
}
