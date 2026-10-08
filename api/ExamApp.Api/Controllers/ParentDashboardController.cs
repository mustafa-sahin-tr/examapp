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
// issue #424: çocuğa ait veri — HER yanıt (200/400/404) önbelleğe alınmaz (Cache-Control: no-store, Pragma: no-cache). Sınıf
// seviyesinde tanımlı; böylece eklenen yeni uç da otomatik kapsanır (ParentEndpointCatalogTests her veli GET ucunda doğrular).
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ParentDashboardController : BaseController
{
    private readonly IParentDashboardService _service;
    private readonly IParentAssignmentService _assignments;
    private readonly IParentProgressService _progress;
    private readonly IParentScheduleService _schedule;
    private readonly IStringLocalizer<Messages> _localizer;

    public ParentDashboardController(
        IParentDashboardService service,
        IParentAssignmentService assignments,
        IParentProgressService progress,
        IParentScheduleService schedule,
        IStringLocalizer<Messages>? localizer = null)
    {
        _service = service;
        _assignments = assignments;
        _progress = progress;
        _schedule = schedule;
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

        return Ok(lookup.Result);
    }

    /// <summary>
    /// Issue #422 (V4): çocuğun puanı, seviyesi, bu hafta kazandığı puan, kazanılmış rozetleri (ad, ikon, GÜN) ve KENDİ sırası
    /// (platform geneli + doğrulanmış okulu varsa okul içi). Başka öğrencinin adı/avatarı/puanı dönmez. Tüm veri exam DB'den
    /// (rozet ve günlük puan projeksiyonları BadgeService event'leriyle beslenir). Salt okunur.
    /// </summary>
    [HttpGet("{studentId:int}/progress")]
    [EnableRateLimiting(ParentLinkRateLimiting.ChildActivityPolicy)]
    public async Task<IActionResult> GetChildProgress(int studentId, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = _localizer["auth.userNotResolved"].Value });

        var progress = await _progress.GetProgressAsync(user.Id, studentId, ct);
        if (progress == null)
            return ChildNotFound();

        return Ok(progress);
    }

    /// <summary>
    /// Issue #422 (V4): çocuğun programı — "Planım" planları (worksheet adı + planlanan gün) ve bekleyen/onaylanan ders
    /// randevuları (öğretmen adı, gün, başlangıç/bitiş, durum). Görüşme bağlantısı, ücret, not dönmez. Salt okunur.
    /// </summary>
    /// <param name="from">İlk gün "yyyy-MM-dd" (Europe/Istanbul). <paramref name="to"/> ile birlikte verilir; ikisi de boşsa bu hafta.</param>
    /// <param name="to">Son gün (dahil) "yyyy-MM-dd"; aralık en fazla 31 gün, bugünden en fazla bir yıl geri/ileri.</param>
    [HttpGet("{studentId:int}/schedule")]
    [EnableRateLimiting(ParentLinkRateLimiting.ChildActivityPolicy)]
    public async Task<IActionResult> GetChildSchedule(
        int studentId, [FromQuery] string? from, [FromQuery] string? to, CancellationToken ct = default)
    {
        // Parametre doğrulaması veri okumaz (kapıdan önce yapılması bir şey sızdırmaz).
        if (!_schedule.TryResolveRange(from, to, out var range))
            return InvalidQuery("range", "parentLinks.errors.invalidRange");

        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = _localizer["auth.userNotResolved"].Value });

        var schedule = await _schedule.GetScheduleAsync(user.Id, studentId, range.From, range.To, ct);
        if (schedule == null)
            return ChildNotFound();

        return Ok(schedule);
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
