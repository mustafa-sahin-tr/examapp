using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.ParentLinks;
using ExamApp.Api.Services.Parents;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Veli–öğrenci bağlantısı (issue #419, epic #407 V1). Gateway üzerinden <c>/api/exam/parent-links/...</c> (mevcut
/// <c>/api/exam/{everything}</c> wildcard route'u). Öğrenci davet kodu üretir ve bağlı velilerini görür; veli kodu kullanır
/// ve çocuklarını görür; iki taraf da koparabilir. Sınıf seviyesinde rol attribute'u YOK — metot bazında (ASP.NET sınıf +
/// metot rollerini AND'ler). Sahiplik servis katmanında: başkasının bağlantısı 404.
/// </summary>
[ApiController]
[Route("api/parent-links")]
public class ParentLinksController : BaseController
{
    private readonly IParentLinkService _service;

    // Client'a dönen tüm metinler mesaj sözlüğünden gelir (issue #184).
    private readonly IStringLocalizer<Messages> _localizer;

    public ParentLinksController(IParentLinkService service, IStringLocalizer<Messages>? localizer = null)
    {
        _service = service;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    /// <summary>Öğrenci: yeni tek kullanımlık davet kodu (öncekini geçersizler). Düz kod yalnızca bu yanıtta döner.</summary>
    [HttpPost("invite-code")]
    [Authorize(Roles = "Student")]
    [EnableRateLimiting(ParentLinkRateLimiting.InvitePolicy)]
    public async Task<IActionResult> CreateInviteCode(CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(Unresolved());

        var result = await _service.CreateInviteCodeAsync(user.Id, ct);
        if (!result.Success)
            return MapFailure(result);

        // Kod bir kez gösterilir; ara katmanlar/tarayıcı önbelleğe almasın.
        Response.Headers.CacheControl = "no-store";
        return Ok(new ParentInviteCodeDto(result.Code!, result.ExpiresAt!.Value));
    }

    /// <summary>Öğrenci: bağlı velileri (yalnızca ad) + geçerli kodun bitişi.</summary>
    [HttpGet("my-parents")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> GetMyParents(CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(Unresolved());

        var result = await _service.GetStudentParentsAsync(user.Id, ct);
        return result == null ? ProfileNotFound() : Ok(result);
    }

    /// <summary>
    /// Veli: davet kodunu kullanır → bağlantı ONAY BEKLER (öğrenci onaylayınca Active). Veli (sub) başına dakikada 5 deneme;
    /// ayrıca hesap başına günlük başarısız deneme tavanı ve platform devre kesicisi (servis, 429). Gateway IP kovası da sayar.
    /// </summary>
    [HttpPost("redeem")]
    [Authorize(Roles = "Parent")]
    [EnableRateLimiting(ParentLinkRateLimiting.RedeemPolicy)]
    public async Task<IActionResult> Redeem(
        [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] RedeemParentInviteCodeRequestDto? request,
        CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(Unresolved());

        // Boş gövde / boş ya da bozuk kod da servisin genel InvalidCode yanıtına düşer (başarısız deneme sayılır).
        var result = await _service.RedeemAsync(user.Id, request?.Code, ct);
        return result.Success ? Ok(result.Child) : MapFailure(result);
    }

    /// <summary>Veli: çocukları — Active (ad, sınıf, okul adı) ve onay bekleyenler (öğrenci verisi yok).</summary>
    [HttpGet("my-children")]
    [Authorize(Roles = "Parent")]
    public async Task<IActionResult> GetMyChildren(CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(Unresolved());

        var result = await _service.GetParentChildrenAsync(user.Id, ct);
        if (result == null)
            return ProfileNotFound();

        // issue #420 review: çocuk adı/okulu/öğrenci id'si — paylaşılan önbelleklerde tutulmasın.
        Response.Headers.CacheControl = "no-store";
        return Ok(result);
    }

    /// <summary>Öğrenci: bekleyen veli isteğini onaylar → Active. Başkasınınki / süresi dolmuş 404.</summary>
    [HttpPost("{linkId:int}/approve")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Approve(int linkId, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(Unresolved());

        var result = await _service.ApproveAsync(linkId, user.Id, ct);
        return result.Success ? NoContent() : MapFailure(result);
    }

    /// <summary>Öğrenci: bekleyen veli isteğini reddeder → Revoked. Başkasınınki 404.</summary>
    [HttpPost("{linkId:int}/reject")]
    [Authorize(Roles = "Student")]
    public async Task<IActionResult> Reject(int linkId, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(Unresolved());

        var result = await _service.RejectAsync(linkId, user.Id, ct);
        return result.Success ? NoContent() : MapFailure(result);
    }

    /// <summary>Öğrenci ya da veli: kendi bağlantısını (aktif ya da bekleyen) koparır (soft). Başkasınınki 404.</summary>
    [HttpPost("{linkId:int}/revoke")]
    [Authorize(Roles = "Student,Parent")]
    public async Task<IActionResult> Revoke(int linkId, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(Unresolved());

        var result = await _service.RevokeAsync(linkId, user.Id, ct);
        return result.Success ? NoContent() : MapFailure(result);
    }

    private object Unresolved() => new { message = _localizer["auth.userNotResolved"].Value };

    private IActionResult ProfileNotFound() => NotFound(new
    {
        message = _localizer["parentLinks.errors.profileNotFound"].Value,
        errorCode = ParentLinkErrorCodes.ProfileNotFound
    });

    /// <summary>Hata gövdesi: <c>{ message, errorCode }</c>; 429 (Retry-After) / 404 / 409 / 400.</summary>
    private IActionResult MapFailure(ParentLinkResponseDto result)
    {
        var body = new { message = result.Message, errorCode = result.ErrorCode };
        if (result.RateLimited)
        {
            Response.Headers.RetryAfter = (result.RetryAfterSeconds ?? 60).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return StatusCode(StatusCodes.Status429TooManyRequests, body);
        }
        if (result.NotFound)
            return NotFound(body);
        if (result.Conflict)
            return Conflict(body);
        return BadRequest(body);
    }
}
