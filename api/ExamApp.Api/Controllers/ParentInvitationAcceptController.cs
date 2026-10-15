using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.ParentInvitations;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Services.Parents.Enforcement;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Issue #440 security C1: e-postaya ait bir hesap zaten varken (anonim aktivasyon 409 <c>LoginRequired</c>) velinin daveti kabulü —
/// OTURUM AÇMIŞ veli (Parent rolü) + davet token'ı. Gateway üzerinden <c>POST /api/exam/parent-activation/accept</c> (kimlik doğrulamalı ayrı
/// route; anonim aktivasyon route'undan önce eşlenir). Token e-posta sahipliğini, oturum hesap sahipliğini kanıtlar; e-posta eşleşmesi
/// auth-api'de hesabın sunucudaki e-postasıyla yapılır (JWT claim'iyle değil). Anonim aktivasyon uçlarıyla aynı IP kovası.
/// </summary>
[ApiController]
[Route("api/parent-activation")]
[Authorize(Roles = "Parent")]
[AllowWhenParentRequired] // çağıran veli; yine de öğrenci yaptırımı bu akışı kesmesin
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ParentInvitationAcceptController : BaseController
{
    private readonly IParentInvitationService _service;
    private readonly IStringLocalizer<Messages> _localizer;

    public ParentInvitationAcceptController(IParentInvitationService service, IStringLocalizer<Messages>? localizer = null)
    {
        _service = service;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    /// <summary>
    /// 200 <c>{ activated: true, passwordSet: false, message }</c>; 400 <c>ConsentRequired</c> / <c>ConsentVersionMismatch</c>; 403
    /// <c>InvitationEmailMismatch</c> / <c>ParentAccountRequired</c>; 404 <c>InvalidActivationToken</c>; 409 <c>ActivationTokenExpired</c> /
    /// <c>ActivationTokenUsed</c> / tavanlar; 502.
    /// </summary>
    [HttpPost("accept")]
    [EnableRateLimiting(ParentInvitationRateLimiting.ActivationPolicy)]
    public async Task<IActionResult> Accept([FromBody] ParentInvitationAcceptRequestDto? request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = _localizer["auth.userNotResolved"].Value });

        var result = await _service.AcceptAsync(request, user.Id, ClientIpAddress.Resolve(HttpContext), ct);
        if (!result.Success)
            return StudentParentController.MapFailure(this, result);
        return Ok(new { activated = true, passwordSet = false, message = result.Message });
    }
}
