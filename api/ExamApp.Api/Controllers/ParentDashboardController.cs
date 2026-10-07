using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.ParentLinks;
using ExamApp.Api.Services.Parents;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Veli paneli (issue #420, epic #407 V2). Gateway üzerinden <c>/api/exam/parent/children/...</c> (mevcut
/// <c>/api/exam/{everything}</c> wildcard route'u). Yetki iki katman: rol (<c>Parent</c>) + servis içinde
/// <see cref="IParentChildAccess"/> (yalnızca Active bağlantı; diğer her durum 404 — hangi koşulun tutmadığı sızdırılmaz).
/// </summary>
[ApiController]
[Route("api/parent/children")]
[Authorize(Roles = "Parent")]
public class ParentDashboardController : BaseController
{
    private readonly IParentDashboardService _service;
    private readonly IStringLocalizer<Messages> _localizer;

    public ParentDashboardController(IParentDashboardService service, IStringLocalizer<Messages>? localizer = null)
    {
        _service = service;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    /// <summary>
    /// Çocuğun özeti: bu hafta çözülen soru, atama durum sayıları, toplam puan, son aktivite. Yalnızca toplamlar
    /// (içerik/cevap anahtarı/mesaj/iletişim bilgisi yok). Başarılı erişim ParentAccessAudit'e yazılır (10 dk kovada tekil).
    /// </summary>
    [HttpGet("{studentId:int}/summary")]
    [EnableRateLimiting(ParentLinkRateLimiting.ChildSummaryPolicy)] // review: veli (sub) başına dakikada 30, 429 + Retry-After
    public async Task<IActionResult> GetChildSummary(int studentId, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = _localizer["auth.userNotResolved"].Value });

        var summary = await _service.GetChildSummaryAsync(user.Id, studentId, ct);
        if (summary == null)
        {
            return NotFound(new
            {
                message = _localizer["parentLinks.errors.childNotFound"].Value,
                errorCode = ParentLinkErrorCodes.NotFound
            });
        }

        // Çocuğa ait veri: paylaşılan önbelleklerde tutulmasın.
        Response.Headers.CacheControl = "no-store";
        return Ok(summary);
    }
}
