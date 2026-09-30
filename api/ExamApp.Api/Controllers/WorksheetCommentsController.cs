using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Models.Dtos.WorksheetComments;
using ExamApp.Api.Services.Teachers.Authorization;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Worksheet / soru yorum-soru thread'leri (issue #105, dilim 1). Gateway üzerinden
/// <c>/api/exam/worksheet/{worksheetId}/comments</c> (mevcut <c>/api/exam/{everything}</c> wildcard route'u).
/// İnce controller: rol/onay kapısı (ApprovedTeacher, #287) + aktör çözümü + HTTP eşleme; yetki kuralları
/// <see cref="IWorksheetCommentService"/>'te. Yazma ucu kullanıcı başına rate limit'li
/// (<see cref="WorksheetCommentWriteRateLimiting"/>), okuma uçları ayrı bir kova ile
/// (<see cref="WorksheetCommentReadRateLimiting"/>).
/// </summary>
[ApiController]
[Route("api/worksheet/{worksheetId:int}/comments")]
public class WorksheetCommentsController : BaseController
{
    private readonly IWorksheetCommentService _comments;
    private readonly IStringLocalizer<Messages> _localizer;

    public WorksheetCommentsController(IWorksheetCommentService comments, IStringLocalizer<Messages>? localizer = null)
    {
        _comments = comments;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    /// <summary>Kök yorumlar (yeniden eskiye, cursor sayfalı) + her kökün reply'ları; thread seviyesinde canWrite/lockReason.</summary>
    [HttpGet]
    [Authorize(Roles = "Student,Teacher,Admin")] // issue #287 review H1: ApprovedTeacher policy tek başına rol kapısı değil
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherOrStudentCapability)] // issue #287: onaysız/askıdaki öğretmen giremez
    [EnableRateLimiting(WorksheetCommentReadRateLimiting.Policy)]
    public async Task<IActionResult> GetThread(int worksheetId, [FromQuery] WorksheetCommentQueryDto query, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        if (actor == null)
            return ActorNotResolved();

        var result = await _comments.GetThreadAsync(worksheetId, query, actor, ct);
        return result.Success ? Ok(result.Page) : MapFailure(result);
    }

    /// <summary>Bir kökün tüm reply'ları — eskiden yeniye, cursor'lı (take ≤ 50). Thread listesi kök başına yalnız son 5'i döner.</summary>
    [HttpGet("{rootId:int}/replies")]
    [Authorize(Roles = "Student,Teacher,Admin")]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherOrStudentCapability)] // issue #287
    [EnableRateLimiting(WorksheetCommentReadRateLimiting.Policy)]
    public async Task<IActionResult> GetReplies(int worksheetId, int rootId, [FromQuery] WorksheetCommentRepliesQueryDto query, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        if (actor == null)
            return ActorNotResolved();

        var result = await _comments.GetRepliesAsync(worksheetId, rootId, query, actor, ct);
        return result.Success ? Ok(result.Page) : MapFailure(result);
    }

    /// <summary>Yeni kök yorum veya (tek seviye) reply. 201 + oluşturulan yorum.</summary>
    [HttpPost]
    [Authorize(Roles = "Student,Teacher")]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherOrStudentCapability)] // issue #287
    [EnableRateLimiting(WorksheetCommentWriteRateLimiting.Policy)]
    public async Task<IActionResult> Create(int worksheetId, [FromBody] CreateWorksheetCommentDto dto, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        if (actor == null)
            return ActorNotResolved();

        var result = await _comments.CreateAsync(worksheetId, dto, actor, ct);
        if (!result.Success)
            return MapFailure(result);

        return StatusCode(StatusCodes.Status201Created, result.Comment);
    }

    /// <summary>
    /// Aktör: öğrenci/öğretmen dalı JWT ile doğrulanmış etkin rolden (<see cref="EffectiveRole"/>, #277). Servis hesabı
    /// yorum yazmaz/okumaz (CreateUserId=0 kayıt üretirdi, #222 D2). Profil çözülemezse (Id &lt;= 0) null.
    /// </summary>
    private async Task<WorksheetCommentActor?> ResolveActorAsync(CancellationToken ct)
    {
        if (IsServiceAccount)
            return null;

        var user = await GetAuthenticatedUserAsync(ct);
        if (user == null || user.Id <= 0)
            return null;

        var kind = user.Role == UserRole.Student.ToString() ? WorksheetCommentActorKind.Student
            : user.Role == UserRole.Teacher.ToString() ? WorksheetCommentActorKind.Teacher
            : WorksheetCommentActorKind.AdminReader;

        return new WorksheetCommentActor(user.Id, user.KeycloakId ?? KeyCloakId ?? string.Empty, user.FullName, kind, IsAdmin);
    }

    private IActionResult ActorNotResolved() =>
        IsServiceAccount ? Forbid() : UserNotResolved(new { message = _localizer["exam.unauthenticated"].Value });

    /// <summary>ResponseBaseDto bayraklarını HTTP koduna çevirir (404 / 403 / 400). Gövde errorCode + message taşır.</summary>
    private IActionResult MapFailure(WorksheetCommentResponseDto result)
    {
        if (result.NotFound)
            return NotFound(result);
        if (result.Forbidden)
            return StatusCode(StatusCodes.Status403Forbidden, result);
        return BadRequest(result);
    }
}
