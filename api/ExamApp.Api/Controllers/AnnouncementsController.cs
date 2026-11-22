using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Services.Teachers.Authorization;
using ExamApp.Api.Services.Tenancy;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>
/// issue #1281 (#1071 S6): okul duyuruları. Gönderen okulun onaylı, askısız, bağımsız olmayan öğretmenidir (okul DB'den); okul
/// geneli / sınıf hedefi yalnız okul yöneticisi (403), başka okulun şubesi / öğrencisi 404, günlük sınır 429. "Gönderilenler"
/// gönderenin kendi duyuruları (okul değişse de kalır). Gateway <c>/api/exam/{everything}</c>.
/// </summary>
[ApiController]
[Route("api/announcements")]
[Authorize(Roles = "Teacher")]
[Authorize(Policy = ApprovedTeacherPolicies.TeacherCapability)]
public class AnnouncementsController : BaseController
{
    private readonly ISchoolAnnouncementService _announcements;
    private readonly IStringLocalizer<Messages> _localizer;

    public AnnouncementsController(ISchoolAnnouncementService announcements, IStringLocalizer<Messages>? localizer = null)
    {
        _announcements = announcements;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    /// <summary>GET api/announcements/options → okul adı ve okul geneli / sınıf hedefinin açık olup olmadığı.</summary>
    [HttpGet("options")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<IActionResult> Options(CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        var options = IsResolvedUser(user) ? await _announcements.GetOptionsAsync(user.Id, ct) : null;
        return options is null ? Error(SchoolAnnouncementStatus.Forbidden) : Ok(options);
    }

    /// <summary>POST api/announcements → duyuruyu gönderir; 201 + alıcı sayıları.</summary>
    [HttpPost]
    public async Task<IActionResult> Send([FromBody] SchoolAnnouncementSendRequestDto? request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return Error(SchoolAnnouncementStatus.Forbidden);
        var result = await _announcements.SendAsync(user.Id, request ?? new SchoolAnnouncementSendRequestDto(), ct);
        return result.Status == SchoolAnnouncementStatus.Success
            ? StatusCode(StatusCodes.Status201Created, result.Announcement)
            : Error(result.Status);
    }

    /// <summary>GET api/announcements/sent?page&amp;pageSize → gönderenin duyuruları, yeniden eskiye.</summary>
    [HttpGet("sent")]
    [EnableRateLimiting(StudentSchoolRequestListRateLimiting.Policy)]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
    public async Task<ActionResult<Paged<SchoolAnnouncementDto>>> Sent(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = AdminListPaging.DefaultPageSize,
        CancellationToken ct = default)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return Error(SchoolAnnouncementStatus.Forbidden);
        return Ok(await _announcements.ListSentAsync(user.Id, page, pageSize, ct));
    }

    private ObjectResult Error(SchoolAnnouncementStatus status)
    {
        var (code, key) = status switch
        {
            SchoolAnnouncementStatus.NotFound => (StatusCodes.Status404NotFound, "admin.announcement.notFound"),
            SchoolAnnouncementStatus.NoRecipients => (StatusCodes.Status400BadRequest, "admin.announcement.noRecipients"),
            SchoolAnnouncementStatus.RateLimited => (StatusCodes.Status429TooManyRequests, "admin.announcement.rateLimited"),
            SchoolAnnouncementStatus.Invalid => (StatusCodes.Status400BadRequest, "admin.announcement.invalid"),
            _ => (StatusCodes.Status403Forbidden, "admin.announcement.forbidden")
        };
        return StatusCode(code, new { message = _localizer[key].Value });
    }
}
