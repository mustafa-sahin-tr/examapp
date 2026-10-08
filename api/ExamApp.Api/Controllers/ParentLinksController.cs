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
/// Veli–öğrenci bağlantısı (issue #419 V1 → issue #436 veli-öncelikli model). Gateway üzerinden <c>/api/exam/parent-links/...</c>
/// (mevcut <c>/api/exam/{everything}</c> wildcard route'u). Öğrenci yalnızca bağlı velilerini görür (salt okunur) ve geçiş
/// dönemindeki eski (#419) istekleri onaylar/reddeder; öğrenci kod üretmez, yeni istek onaylamaz, bağlantı koparmaz. Birincil
/// veli ikinci veli kodu üretir, bekleyen isteği onaylar/reddeder, bağlantıları koparır; ikinci veli kodu mevcut <c>redeem</c>
/// ucuyla girer (gateway IP kovası ve veli başına dakikada 5 aynen sayar). Admin her bağlantıyı koparabilir. Sınıf seviyesinde
/// rol attribute'u YOK — metot bazında (ASP.NET sınıf + metot rollerini AND'ler). Sahiplik servis katmanında: çocuğa bağlı
/// olmayana 404, bağlı ama birincil olmayana 403.
/// </summary>
[ApiController]
[Route("api/parent-links")]
public class ParentLinksController : BaseController
{
    private const string ParentRole = "Parent";
    private const string AdminRole = "Admin";

    private readonly IParentLinkService _service;

    // Client'a dönen tüm metinler mesaj sözlüğünden gelir (issue #184).
    private readonly IStringLocalizer<Messages> _localizer;

    public ParentLinksController(IParentLinkService service, IStringLocalizer<Messages>? localizer = null)
    {
        _service = service;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    /// <summary>Öğrenci: bağlı velileri (salt okunur; yalnızca ad + birincil işareti) + geçiş dönemindeki eski istekler.</summary>
    [HttpGet("my-parents")]
    [Authorize(Roles = "Student")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] // issue #424: veli adları/maskeli e-posta; koparma anında yansısın
    public async Task<IActionResult> GetMyParents(CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(Unresolved());

        var result = await _service.GetStudentParentsAsync(user.Id, ct);
        return result == null ? ProfileNotFound() : Ok(result);
    }

    /// <summary>
    /// Birincil veli: <paramref name="linkId"/> (kendi Active bağlantısı) çocuğu için "ikinci veli davet kodu" (7 gün, tek
    /// kullanımlık; öncekini geçersizler). Düz kod yalnızca bu yanıtta döner. Kod üretimi veli (sub) başına saatte 10.
    /// </summary>
    [HttpPost("{linkId:int}/second-parent-code")]
    [Authorize(Roles = ParentRole)]
    [EnableRateLimiting(ParentLinkRateLimiting.InvitePolicy)]
    public async Task<IActionResult> CreateSecondParentCode(int linkId, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(Unresolved());

        var result = await _service.CreateSecondParentCodeAsync(linkId, user.Id, ct);
        if (!result.Success)
            return MapFailure(result);

        // Kod bir kez gösterilir; ara katmanlar/tarayıcı önbelleğe almasın.
        Response.Headers.CacheControl = "no-store";
        return Ok(new ParentInviteCodeDto(result.Code!, result.ExpiresAt!.Value));
    }

    /// <summary>
    /// Veli: davet kodunu kullanır → bağlantı BİRİNCİL VELİNİN onayını bekler. Veli (sub) başına dakikada 5 deneme; ayrıca hesap
    /// başına günlük başarısız deneme tavanı ve platform devre kesicisi (servis, 429). Gateway IP kovası da sayar.
    /// </summary>
    [HttpPost("redeem")]
    [Authorize(Roles = ParentRole)]
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

    /// <summary>Veli: çocukları — Active (ad, sınıf, okul; birincilse diğer veliler + bekleyen istekler) ve kendi bekleyen isteği.</summary>
    [HttpGet("my-children")]
    [Authorize(Roles = ParentRole)]
    // issue #420 review / #424: çocuk adı/okulu/öğrenci id'si — HER yanıt önbelleğe alınmaz (koparma bir sonraki istekte görünür).
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> GetMyChildren(CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(Unresolved());

        var result = await _service.GetParentChildrenAsync(user.Id, ct);
        if (result == null)
            return ProfileNotFound();

        return Ok(result);
    }

    /// <summary>
    /// Bekleyen isteği onaylar → Active. Veli: birincil veli, ikinci veli isteğini (bağlı ama birincil değilse 403). Öğrenci:
    /// yalnız geçiş dönemindeki eski (#419) isteği — yeni istekler öğrenciye 404. Başkasınınki / süresi dolmuş 404.
    /// </summary>
    [HttpPost("{linkId:int}/approve")]
    [Authorize(Roles = "Student,Parent")]
    [EnableRateLimiting(ParentLinkRateLimiting.ManagePolicy)]
    public async Task<IActionResult> Approve(int linkId, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(Unresolved());

        var result = User.IsInRole(ParentRole)
            ? await _service.ApproveSecondParentAsync(linkId, user.Id, ct)
            : await _service.ApproveLegacyAsync(linkId, user.Id, ct);
        return result.Success ? NoContent() : MapFailure(result);
    }

    /// <summary>Bekleyen isteği reddeder → Revoked. Yetki kuralları onayla aynı.</summary>
    [HttpPost("{linkId:int}/reject")]
    [Authorize(Roles = "Student,Parent")]
    [EnableRateLimiting(ParentLinkRateLimiting.ManagePolicy)]
    public async Task<IActionResult> Reject(int linkId, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(Unresolved());

        var result = User.IsInRole(ParentRole)
            ? await _service.RejectSecondParentAsync(linkId, user.Id, ct)
            : await _service.RejectLegacyAsync(linkId, user.Id, ct);
        return result.Success ? NoContent() : MapFailure(result);
    }

    /// <summary>
    /// Bağlantıyı koparır (soft). Veli: birincil veli çocuğun her bağlantısını, her veli kendi bağlantısını / bekleyen isteğini
    /// (ayrılma; tek Active veli ayrılamaz → 409 LastParentCannotLeave); birincil olmayan veli başkasınınkini koparamaz (403).
    /// Admin: her bağlantıyı (denetim izine yazılır). Öğrenci koparamaz (403). Başkasınınki 404. Kullanıcı başına dakikada 30.
    /// </summary>
    [HttpPost("{linkId:int}/revoke")]
    [Authorize(Roles = "Parent,Admin")]
    [EnableRateLimiting(ParentLinkRateLimiting.ManagePolicy)]
    public async Task<IActionResult> Revoke(int linkId, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(Unresolved());

        ParentLinkResponseDto result;
        if (User.IsInRole(AdminRole))
        {
            if (string.IsNullOrWhiteSpace(KeyCloakId))
                return UserNotResolved(Unresolved());
            result = await _service.AdminRevokeAsync(linkId, user.Id, KeyCloakId, ct);
        }
        else
        {
            result = await _service.RevokeAsync(linkId, user.Id, ct);
        }
        return result.Success ? NoContent() : MapFailure(result);
    }

    private object Unresolved() => new { message = _localizer["auth.userNotResolved"].Value };

    private IActionResult ProfileNotFound() => NotFound(new
    {
        message = _localizer["parentLinks.errors.profileNotFound"].Value,
        errorCode = ParentLinkErrorCodes.ProfileNotFound
    });

    /// <summary>Hata gövdesi: <c>{ message, errorCode }</c>; 429 (Retry-After) / 404 / 403 / 409 / 400.</summary>
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
        if (result.Forbidden)
            return StatusCode(StatusCodes.Status403Forbidden, body);
        if (result.Conflict)
            return Conflict(body);
        return BadRequest(body);
    }
}
