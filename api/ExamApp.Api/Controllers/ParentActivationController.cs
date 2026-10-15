using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.ParentInvitations;
using ExamApp.Api.Services.Parents;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Issue #440: velinin e-postasındaki aktivasyon bağlantısı (<c>/app/parent-activate?token=...</c>, auth-ui) için ANONİM uçlar. Gateway
/// üzerinden <c>POST /api/exam/parent-activation/{lookup|activate|resend}</c> (kimlik doğrulamasız ayrı route; giriş IP kovasından da
/// düşer). Token URL'de değil gövdede taşınır (erişim loglarına düşmesin); yalnız SHA-256 hash'iyle aranır. İstemci IP'si başına
/// 10 dakikada 20. Parola yanıtlarda/loglarda yer almaz; yanıtlar önbelleğe alınmaz.
/// </summary>
[ApiController]
[Route("api/parent-activation")]
[AllowAnonymous]
[EnableRateLimiting(ParentInvitationRateLimiting.ActivationPolicy)]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ParentActivationController : ControllerBase
{
    private readonly IParentInvitationService _service;

    public ParentActivationController(IParentInvitationService service) => _service = service;

    /// <summary>
    /// 200 <see cref="ParentActivationLookupDto"/> (kullanılmış / süresi dolmuş token da 200 — <c>used</c> / <c>expired</c> bayrakları);
    /// bilinmeyen token 404 <c>InvalidActivationToken</c>.
    /// </summary>
    [HttpPost("lookup")]
    [ProducesResponseType(typeof(ParentActivationLookupDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> Lookup([FromBody] ParentActivationTokenRequestDto? request, CancellationToken ct)
    {
        var result = await _service.LookupAsync(request?.Token, ct);
        return result.Success ? Ok(result.Lookup) : StudentParentController.MapFailure(this, result);
    }

    /// <summary>
    /// Parola belirle + rıza → hesap aktif, bağlantı Active. 200 <c>{ activated, passwordSet, message }</c>; 400 <c>ConsentRequired</c> /
    /// <c>ConsentVersionMismatch</c> / <c>PasswordRejected</c>; 404 <c>InvalidActivationToken</c>; 409 <c>ActivationTokenExpired</c> /
    /// <c>ActivationTokenUsed</c>; 502.
    /// </summary>
    [HttpPost("activate")]
    public async Task<IActionResult> Activate([FromBody] ParentActivationRequestDto? request, CancellationToken ct)
    {
        var result = await _service.ActivateAsync(request, ClientIpAddress.Resolve(HttpContext), ct);
        if (!result.Success)
            return StudentParentController.MapFailure(this, result);
        return Ok(new { activated = result.Activation!.Activated, passwordSet = result.Activation.PasswordSet, message = result.Message });
    }

    /// <summary>Süresi dolmuş bağlantıyı yeniden iste (yeni bağlantı veliye e-postayla). 200 <c>{ message }</c>; 404; 409 <c>ActivationTokenUsed</c>; 429.</summary>
    [HttpPost("resend")]
    public async Task<IActionResult> Resend([FromBody] ParentActivationTokenRequestDto? request, CancellationToken ct)
    {
        var result = await _service.ResendByTokenAsync(request?.Token, ct);
        return result.Success ? Ok(new { message = result.Message }) : StudentParentController.MapFailure(this, result);
    }
}
