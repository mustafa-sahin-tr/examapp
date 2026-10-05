using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Models.Dtos.DirectMessages;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Services.DirectMessages;
using ExamApp.Api.Services.Teachers.Authorization;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>
/// Öğrenci ↔ öğretmen doğrudan mesajlaşma (issue #106, dilim a — bildirimsiz). Gateway üzerinden
/// <c>/api/exam/direct-messages/...</c> (mevcut <c>/api/exam/{everything}</c> wildcard route'u). İnce controller: rol/onay
/// kapısı (ApprovedTeacher, #287) + aktör çözümü + HTTP eşleme; yetki kuralı (<c>CanMessage</c>) ve IDOR kontrolleri
/// <see cref="IDirectMessageService"/>/<see cref="IDirectMessagePolicy"/>'de.
/// </summary>
[ApiController]
[Route("api/direct-messages")]
public class DirectMessagesController : BaseController
{
    private readonly IDirectMessageService _messages;
    private readonly IAdminDataAccessAuditService _dataAccessAudit;
    private readonly IStringLocalizer<Messages> _localizer;

    public DirectMessagesController(IDirectMessageService messages, IAdminDataAccessAuditService dataAccessAudit,
        IStringLocalizer<Messages>? localizer = null)
    {
        _messages = messages;
        _dataAccessAudit = dataAccessAudit;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    // ---- Öğrenci ----------------------------------------------------------------------------------------------

    /// <summary>Öğrenci: mesajlaşabileceği öğretmenler (A ∪ B, tekrarsız, <c>relation</c>; <c>search</c>, <c>page</c>, <c>pageSize</c>).</summary>
    [HttpGet("teachers")]
    [Authorize(Roles = "Student")]
    [EnableRateLimiting(DirectMessageRateLimiting.TeacherListPolicy)] // search doluysa arama kovası (20/dk), değilse okuma
    public async Task<IActionResult> GetMessageableTeachers([FromQuery] MessageableTeacherQueryDto query, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        if (actor == null)
            return ActorNotResolved();

        var result = await _messages.GetMessageableTeachersAsync(actor, query, ct);
        return result.Success ? Ok(result.Page) : MapFailure(result);
    }

    /// <summary>Öğrenci: öğretmene mesaj (ilk mesaj konuşmayı açar). 201 + <see cref="SendDirectMessageResultDto"/>; CanMessage değilse nötr 403.</summary>
    [HttpPost("teachers/{teacherId:int}/messages")]
    [Authorize(Roles = "Student")]
    [EnableRateLimiting(DirectMessageRateLimiting.SendPolicy)]
    public async Task<IActionResult> SendToTeacher(int teacherId, [FromBody] SendDirectMessageDto dto, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        if (actor == null)
            return ActorNotResolved();

        var result = await _messages.SendToTeacherAsync(actor, teacherId, dto, ct);
        return result.Success ? StatusCode(StatusCodes.Status201Created, result) : MapFailure(result);
    }

    /// <summary>Öğrenci: kendi konuşmaları (son mesaj önce; <c>page</c>, <c>pageSize</c>).</summary>
    [HttpGet("conversations")]
    [Authorize(Roles = "Student")]
    [EnableRateLimiting(DirectMessageRateLimiting.ReadPolicy)]
    public async Task<IActionResult> GetStudentConversations([FromQuery] DirectMessagePageQueryDto query, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        if (actor == null)
            return ActorNotResolved();

        var result = await _messages.GetStudentConversationsAsync(actor, query, ct);
        return result.Success ? Ok(result.Page) : MapFailure(result);
    }

    // ---- Öğretmen ---------------------------------------------------------------------------------------------

    /// <summary>Öğretmen: gelen kutusu (<c>filter = all | unread | blocked</c>, <c>page</c>, <c>pageSize</c>).</summary>
    [HttpGet("inbox")]
    [Authorize(Roles = "Teacher")]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherOrStudentCapability)] // #287: onaysız/askıdaki öğretmen giremez
    [EnableRateLimiting(DirectMessageRateLimiting.ReadPolicy)]
    public async Task<IActionResult> GetInbox([FromQuery] TeacherInboxQueryDto query, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        if (actor == null)
            return ActorNotResolved();

        var result = await _messages.GetTeacherInboxAsync(actor, query, ct);
        return result.Success ? Ok(result.Page) : MapFailure(result);
    }

    /// <summary>Öğretmen: konuşmanın öğrencisini engelle (idempotent, audit'li).</summary>
    [HttpPost("conversations/{conversationId:int}/block")]
    [Authorize(Roles = "Teacher")]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherOrStudentCapability)]
    [EnableRateLimiting(DirectMessageRateLimiting.BlockPolicy)]
    public Task<IActionResult> Block(int conversationId, CancellationToken ct) => SetBlockedAsync(conversationId, true, ct);

    /// <summary>Öğretmen: engeli kaldır (idempotent, audit'li).</summary>
    [HttpPost("conversations/{conversationId:int}/unblock")]
    [Authorize(Roles = "Teacher")]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherOrStudentCapability)]
    [EnableRateLimiting(DirectMessageRateLimiting.BlockPolicy)]
    public Task<IActionResult> Unblock(int conversationId, CancellationToken ct) => SetBlockedAsync(conversationId, false, ct);

    private async Task<IActionResult> SetBlockedAsync(int conversationId, bool blocked, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        if (actor == null)
            return ActorNotResolved();

        var result = await _messages.SetBlockedAsync(actor, conversationId, blocked, ct);
        return result.Success ? Ok(result) : MapFailure(result);
    }

    // ---- İki taraf --------------------------------------------------------------------------------------------

    /// <summary>Konuşmanın mesajları (eskiden yeniye bir sayfa; <c>beforeId</c>, <c>take</c>). Yan etkisiz — okundu için POST .../read.</summary>
    [HttpGet("conversations/{conversationId:int}/messages")]
    [Authorize(Roles = "Student,Teacher")]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherOrStudentCapability)]
    [EnableRateLimiting(DirectMessageRateLimiting.ReadPolicy)]
    public async Task<IActionResult> GetMessages(int conversationId, [FromQuery] DirectMessageHistoryQueryDto query, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        if (actor == null)
            return ActorNotResolved();

        var result = await _messages.GetMessagesAsync(actor, conversationId, query, ct);
        return result.Success ? Ok(result.Conversation) : MapFailure(result);
    }

    /// <summary>
    /// Okundu işaretle: gövde <c>{ upToMessageId }</c>. Karşı taraftan gelen, Id &lt;= upToMessageId okunmamışlar okundu olur.
    /// 200 + <c>{ conversationId, markedCount }</c> (idempotent). Taraf değilse / yoksa 404.
    /// </summary>
    [HttpPost("conversations/{conversationId:int}/read")]
    [Authorize(Roles = "Student,Teacher")]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherOrStudentCapability)]
    [EnableRateLimiting(DirectMessageRateLimiting.ReadPolicy)]
    public async Task<IActionResult> MarkRead(int conversationId, [FromBody] MarkDirectMessagesReadDto dto, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        if (actor == null)
            return ActorNotResolved();

        var result = await _messages.MarkReadAsync(actor, conversationId, dto, ct);
        return result.Success ? Ok(result) : MapFailure(result);
    }

    /// <summary>Mevcut konuşmaya mesaj: öğrenci (CanMessage) ya da öğretmen cevabı (ilişki sürüyorsa; engel cevabı kapatmaz). 201.</summary>
    [HttpPost("conversations/{conversationId:int}/messages")]
    [Authorize(Roles = "Student,Teacher")]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherOrStudentCapability)]
    [EnableRateLimiting(DirectMessageRateLimiting.SendPolicy)]
    public async Task<IActionResult> SendToConversation(int conversationId, [FromBody] SendDirectMessageDto dto, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        if (actor == null)
            return ActorNotResolved();

        var result = await _messages.SendToConversationAsync(actor, conversationId, dto, ct);
        return result.Success ? StatusCode(StatusCodes.Status201Created, result) : MapFailure(result);
    }

    /// <summary>Şikayet: gövde <c>{ messageId?, reason, note? }</c>. 200 + <c>reportId</c>, <c>alreadyReported</c> (idempotent).</summary>
    [HttpPost("conversations/{conversationId:int}/report")]
    [Authorize(Roles = "Student,Teacher")]
    [Authorize(Policy = ApprovedTeacherPolicies.TeacherOrStudentCapability)]
    [EnableRateLimiting(DirectMessageRateLimiting.ReportPolicy)]
    public async Task<IActionResult> Report(int conversationId, [FromBody] ReportDirectMessageDto dto, CancellationToken ct)
    {
        var actor = await ResolveActorAsync(ct);
        if (actor == null)
            return ActorNotResolved();

        var result = await _messages.ReportAsync(actor, conversationId, dto, ct);
        return result.Success ? Ok(result) : MapFailure(result);
    }

    // ---- Admin ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Admin: Open şikayetler (salt okunur; <c>page</c>, <c>pageSize</c>). Gateway: /api/exam/admin/direct-messages/reports.
    /// Mesaj gövdesi + taraf adları taşır (kişisel veri): her başarılı çağrı <c>AdminDataAccessLogs</c>'a yazılır (Resource
    /// <c>DirectMessageReports</c>; veri dönmeden önce, fail-closed) ve admin liste uçlarıyla AYNI kullanıcı başına kova —
    /// reddedilen (429) istek de audit'lenir (#246/#262 deseni).
    /// </summary>
    [HttpGet("~/api/admin/direct-messages/reports")]
    [Authorize(Roles = "Admin")]
    [ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)] // kişisel veri
    [EnableRateLimiting(AdminUserListRateLimiting.Policy)]
    [AdminDataAccess(AdminDataAccessResource.DirectMessageReports)]
    public async Task<IActionResult> GetOpenReports([FromQuery] DirectMessagePageQueryDto query, CancellationToken ct)
    {
        if (IsServiceAccount)
            return Forbid();

        var result = await _messages.GetOpenReportsAsync(query, ct);
        if (!result.Success)
            return MapFailure(result);

        await _dataAccessAudit.RecordListAccessAsync(new AdminListAccessRecord(
            KeyCloakId ?? string.Empty, AdminDataAccessResource.DirectMessageReports, null, false,
            result.Page!.Page, result.Page.PageSize, result.Page.Items.Count, result.Page.TotalCount), ct);
        return Ok(result.Page);
    }

    // ---- Yardımcılar ------------------------------------------------------------------------------------------

    /// <summary>
    /// Aktör: JWT ile doğrulanmış etkin rol (<see cref="EffectiveRole"/>, #277) — Student ya da Teacher. Servis hesabı ve
    /// başka roller mesajlaşmaz. Profil çözülemezse (Id &lt;= 0) null.
    /// </summary>
    private async Task<DirectMessageActor?> ResolveActorAsync(CancellationToken ct)
    {
        if (IsServiceAccount)
            return null;

        var user = await GetAuthenticatedUserAsync(ct);
        if (user == null || user.Id <= 0)
            return null;

        DirectMessageActorKind? kind = user.Role == UserRole.Student.ToString() ? DirectMessageActorKind.Student
            : user.Role == UserRole.Teacher.ToString() ? DirectMessageActorKind.Teacher
            : null;
        return kind == null ? null : new DirectMessageActor(user.Id, user.KeycloakId ?? KeyCloakId ?? string.Empty, kind.Value);
    }

    private IActionResult ActorNotResolved() =>
        IsServiceAccount ? Forbid() : UserNotResolved(new { message = _localizer["exam.unauthenticated"].Value });

    private IActionResult MapFailure(DirectMessageResponseDto result)
    {
        if (result.RateLimited)
        {
            if (result.RetryAfterSeconds is { } seconds)
                Response.Headers.RetryAfter = seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
            return StatusCode(StatusCodes.Status429TooManyRequests, result);
        }
        if (result.ServiceUnavailable)
            return StatusCode(StatusCodes.Status503ServiceUnavailable, result);
        if (result.NotFound)
            return NotFound(result);
        if (result.Forbidden)
            return StatusCode(StatusCodes.Status403Forbidden, result);
        return BadRequest(result);
    }
}
