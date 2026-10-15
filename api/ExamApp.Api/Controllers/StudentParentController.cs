using System.Globalization;
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
/// Issue #440 / #441: öğrencinin veli durumu ve "veli bilgisi ekle / e-postayı düzelt / yeniden gönder" akışı. Gateway üzerinden
/// <c>/api/exam/student-parent/...</c> (mevcut <c>/api/exam/{everything}</c> route'u). Yalnız Student rolü; hoşgörü süresi dolmuş öğrenci
/// de çağırabilir (<see cref="AllowWhenParentRequiredAttribute"/> — süre bitince açık kalan tek akış). Yanıtlar önbelleğe alınmaz.
/// </summary>
[ApiController]
[Route("api/student-parent")]
[Authorize(Roles = "Student")]
[AllowWhenParentRequired]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class StudentParentController : BaseController
{
    private readonly IParentInvitationService _service;
    private readonly IStringLocalizer<Messages> _localizer;

    public StudentParentController(IParentInvitationService service, IStringLocalizer<Messages>? localizer = null)
    {
        _service = service;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    /// <summary>Öğrencinin veli durumu (<see cref="ParentStatusDto"/>). İlk çağrıda hoşgörü sayacı başlar (profil tazelemesiyle aynı).</summary>
    [HttpGet("status")]
    [ProducesResponseType(typeof(ParentStatusDto), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = _localizer["auth.userNotResolved"].Value });

        return Ok(await _service.GetStatusAsync(user.Id, ParentRequirement.RequiresParent(User), stamp: true, ct));
    }

    /// <summary>
    /// Veli bilgisi ekle / e-postayı düzelt (eski bağlantı geçersiz olur). Aynı e-posta tekrar gönderilirse yeniden gönderim sayılır.
    /// 200 <c>{ message, status }</c>; 400 alan hatası; 409 <c>ParentEmailNotUsable</c> / <c>ParentAlreadyLinked</c>; 429
    /// <c>ParentEmailChangeLocked</c> / <c>ParentInvitationResendLimited</c> (+ <c>retryAt</c>, Retry-After); 502. Öğrenci (sub) başına saatte 10.
    /// </summary>
    [HttpPost("invitation")]
    [EnableRateLimiting(ParentInvitationRateLimiting.WritePolicy)]
    public async Task<IActionResult> StartInvitation([FromBody] ParentContactRequestDto? request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = _localizer["auth.userNotResolved"].Value });

        var result = await _service.StartAsync(new ParentInvitationStudent(user.Id, KeyCloakId ?? string.Empty, user.FullName), request,
            ParentRequirement.RequiresParent(User), ct);
        return result.Success ? Ok(new { message = result.Message, status = result.Status }) : MapFailure(result);
    }

    /// <summary>Açık daveti yeniden gönder (aynı e-postaya 24 saatte 1, toplam 5). 200 <c>{ message, status }</c>; 404 <c>NoOpenInvitation</c>; 429.</summary>
    [HttpPost("invitation/resend")]
    [EnableRateLimiting(ParentInvitationRateLimiting.WritePolicy)]
    public async Task<IActionResult> ResendInvitation(CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = _localizer["auth.userNotResolved"].Value });

        var result = await _service.ResendAsync(new ParentInvitationStudent(user.Id, KeyCloakId ?? string.Empty, user.FullName),
            ParentRequirement.RequiresParent(User), ct);
        return result.Success ? Ok(new { message = result.Message, status = result.Status }) : MapFailure(result);
    }

    /// <summary>Hata gövdesi: <c>{ message, errorCode, retryAt? }</c>; 429 (Retry-After) / 404 / 409 / 502 / 400.</summary>
    internal static IActionResult MapFailure(ControllerBase controller, ParentInvitationResultDto result)
    {
        if (result.RateLimited)
        {
            controller.Response.Headers.RetryAfter = (result.RetryAfterSeconds ?? 60).ToString(CultureInfo.InvariantCulture);
            return controller.StatusCode(StatusCodes.Status429TooManyRequests,
                new { message = result.Message, errorCode = result.ErrorCode, retryAt = result.RetryAt });
        }
        var body = new { message = result.Message, errorCode = result.ErrorCode };
        if (result.Upstream)
            return controller.StatusCode(StatusCodes.Status502BadGateway, body);
        if (result.NotFound)
            return controller.NotFound(body);
        if (result.Forbidden)
            return controller.StatusCode(StatusCodes.Status403Forbidden, body);
        if (result.Conflict)
            return controller.Conflict(body);
        return controller.BadRequest(body);
    }

    private IActionResult MapFailure(ParentInvitationResultDto result) => MapFailure(this, result);
}
