using System.Collections.Generic;
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
    private readonly IParentAssignmentService _assignments;
    private readonly IStringLocalizer<Messages> _localizer;

    public ParentDashboardController(
        IParentDashboardService service,
        IParentAssignmentService assignments,
        IStringLocalizer<Messages>? localizer = null)
    {
        _service = service;
        _assignments = assignments;
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

    /// <summary>
    /// Issue #421 (V3): çocuğun ödev/test listesi — öğrencinin kendi ekranıyla aynı görünürlük, V2 özetiyle aynı kapsam ve
    /// kovalar (<c>completed</c> / <c>overdue</c> / <c>pending</c>), en yeni teslim tarihi önce, sayfa başına 20. Tamamlanan
    /// işlerde puan, doğru/yanlış/boş ve süre. Soru içeriği, cevap anahtarı ya da seçilen şık dönmez. Salt okunur.
    /// </summary>
    /// <param name="status">Boş = hepsi; aksi halde <c>completed</c>, <c>overdue</c> ya da <c>pending</c>.</param>
    /// <param name="page">1 tabanlı sayfa (varsayılan 1).</param>
    [HttpGet("{studentId:int}/assignments")]
    [EnableRateLimiting(ParentLinkRateLimiting.ChildActivityPolicy)]
    public async Task<IActionResult> GetChildAssignments(
        int studentId, [FromQuery] string? status, [FromQuery] int page = 1, CancellationToken ct = default)
    {
        // Parametre doğrulaması veri okumaz (kapıdan önce yapılması bir şey sızdırmaz).
        if (!ParentAssignmentService.TryParseStatus(status, out var bucket))
            return InvalidQuery(nameof(status), "parentLinks.errors.invalidStatus");
        if (page < 1 || page > MaxPage)
            return InvalidQuery(nameof(page), "parentLinks.errors.invalidPage");

        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = _localizer["auth.userNotResolved"].Value });

        var list = await _assignments.GetAssignmentsAsync(user.Id, studentId, bucket, page, ct);
        if (list == null)
            return ChildNotFound();

        Response.Headers.CacheControl = "no-store";
        return Ok(list);
    }

    /// <summary>
    /// Issue #421 (V3): çocuğun bitmiş bir test oturumunun özeti — puan, doğru/yanlış/boş, süre, başlangıç/bitiş ve konu bazında
    /// sayılar. Soru metni/görseli, cevap anahtarı, doğru şık ya da çocuğun seçtiği şık dönmez. Oturum bu çocuğa ait değilse,
    /// yoksa ya da henüz bitmemişse 404.
    /// </summary>
    [HttpGet("{studentId:int}/test-results/{testInstanceId:int}")]
    [EnableRateLimiting(ParentLinkRateLimiting.ChildActivityPolicy)]
    public async Task<IActionResult> GetChildTestResult(int studentId, int testInstanceId, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = _localizer["auth.userNotResolved"].Value });

        var lookup = await _assignments.GetTestResultAsync(user.Id, studentId, testInstanceId, ct);
        if (!lookup.ChildAccessible)
            return ChildNotFound();
        if (lookup.Result == null)
        {
            return NotFound(new
            {
                message = _localizer["parentLinks.errors.testResultNotFound"].Value,
                errorCode = ParentLinkErrorCodes.NotFound
            });
        }

        Response.Headers.CacheControl = "no-store";
        return Ok(lookup.Result);
    }

    /// <summary>Sayfa üst sınırı: satır tavanı / sayfa boyutu (500 / 20 = 25); daha büyük sayfa her zaman boş olurdu.</summary>
    internal const int MaxPage = ParentAssignmentScope.MaxRows / ParentAssignmentService.PageSize;

    private NotFoundObjectResult ChildNotFound() => NotFound(new
    {
        message = _localizer["parentLinks.errors.childNotFound"].Value,
        errorCode = ParentLinkErrorCodes.NotFound
    });

    private BadRequestObjectResult InvalidQuery(string field, string messageKey)
    {
        var message = _localizer[messageKey].Value;
        return BadRequest(new { message, errors = new Dictionary<string, string[]> { [field] = new[] { message } } });
    }
}
