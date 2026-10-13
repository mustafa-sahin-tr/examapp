using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Helpers;
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
/// Issue #438 (epic #435): veli panelinden çocuk hesabı açma ve çocuk şifresini sıfırlama. Gateway üzerinden
/// <c>/api/exam/parent/children[...]</c> (mevcut <c>/api/exam/{everything}</c> wildcard route'u; açma isteği gateway'in giriş IP
/// kovasından da düşer). Yalnız Parent rolü (öğrenci/öğretmen 403); sahiplik servis katmanında (bağlı olmayan çocuk 404).
/// Parola hiçbir yanıtta dönmez; yanıtlar önbelleğe alınmaz.
/// </summary>
[ApiController]
[Route("api/parent/children")]
[Authorize(Roles = "Parent")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ParentChildAccountsController : BaseController
{
    private readonly IParentChildAccountService _service;
    private readonly IStringLocalizer<Messages> _localizer;

    public ParentChildAccountsController(IParentChildAccountService service, IStringLocalizer<Messages>? localizer = null)
    {
        _service = service;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    /// <summary>
    /// Çocuk hesabı açar: Keycloak kullanıcısı (kullanıcı adı + şifre, e-postasız) + öğrenci profili (ParentManaged) + birincil
    /// Active bağlantı + rıza kaydı. 201 gövdesi yalnız öğrenci/bağlantı id'si, kullanıcı adı ve ad — şifre YOK. Kullanıcı adı
    /// alınmışsa 409 <c>UsernameTaken</c>; tavan 409; doğrulama 400 (alan bazlı <c>errorCode</c>); kimlik deposu erişilemezse 502.
    /// Veli (sub) başına saatte 5.
    /// </summary>
    [HttpPost]
    [EnableRateLimiting(ParentLinkRateLimiting.ChildAccountCreatePolicy)]
    [ProducesResponseType(typeof(CreatedChildAccountDto), StatusCodes.Status201Created)]
    public async Task<IActionResult> CreateChildAccount([FromBody] CreateChildAccountRequestDto request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = _localizer["auth.userNotResolved"].Value });

        var result = await _service.CreateAsync(user.Id, request, ClientIpAddress.Resolve(HttpContext), ct);
        if (!result.Success)
            return MapFailure(result);

        return CreatedAtAction(
            nameof(ParentDashboardController.GetChildSummary),
            "ParentDashboard",
            new { studentId = result.Child!.StudentId },
            result.Child);
    }

    /// <summary>
    /// Velinin belirlediği yeni şifre (yalnız veli tarafından açılmış hesap; aksi 403 <c>NotParentManaged</c>). Bağlı olmayan çocuk
    /// 404. Şifre yanıtta dönmez/loglanmaz; başarı veli erişim kaydına yazılır. 204. Veli (sub) başına saatte 10.
    /// </summary>
    [HttpPost("{studentId:int}/reset-password")]
    [EnableRateLimiting(ParentLinkRateLimiting.ChildPasswordResetPolicy)]
    public async Task<IActionResult> ResetChildPassword(int studentId, [FromBody] ResetChildPasswordRequestDto request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = _localizer["auth.userNotResolved"].Value });

        var result = await _service.ResetPasswordAsync(user.Id, studentId, request.NewPassword, ct);
        return result.Success ? NoContent() : MapFailure(result);
    }

    /// <summary>Hata gövdesi: <c>{ message, errorCode }</c>; 404 / 403 / 409 / 502 / 400.</summary>
    private IActionResult MapFailure(ParentLinkResponseDto result)
    {
        var body = new { message = result.Message, errorCode = result.ErrorCode };
        if (result.NotFound)
            return NotFound(body);
        if (result.Forbidden)
            return StatusCode(StatusCodes.Status403Forbidden, body);
        if (result.Conflict)
            return Conflict(body);
        if (result is ParentChildAccountResultDto { Upstream: true })
            return StatusCode(StatusCodes.Status502BadGateway, body);
        return BadRequest(body);
    }
}
