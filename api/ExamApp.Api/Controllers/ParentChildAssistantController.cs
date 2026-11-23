using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.ParentLinks;
using ExamApp.Api.Services.Parents;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>Issue #523: <c>PUT</c> gövdesi.</summary>
public sealed class ParentChildAssistantSettingDto
{
    public bool Allowed { get; set; }
}

/// <summary>Issue #523: yanıt — bu velinin seçimi ve çocuk için geçerli durum (başka bir veli kapattıysa false).</summary>
public sealed class ParentChildAssistantStateDto
{
    public bool Allowed { get; set; }

    public bool EffectiveAllowed { get; set; }
}

/// <summary>
/// Issue #523: veli çocuğun AI asistanı kullanmasını açar / kapatır (varsayılan açık). Her Active veli kendi bağlantısında seçer; herhangi
/// biri kapatırsa kapalıdır (<see cref="ParentAssistantControl"/>). Başka velinin çocuğu ve Pending / Revoked bağlantı 404; her değişiklik
/// veli erişim kaydına düşer.
/// </summary>
[ApiController]
[Route("api/parent/children/{studentId:int}/assistant")]
[Authorize(Roles = "Parent")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class ParentChildAssistantController : BaseController
{
    private readonly AppDbContext _db;
    private readonly IParentChildAccess _access;
    private readonly IParentAccessAuditLog _audit;
    private readonly IStringLocalizer<Messages> _localizer;

    public ParentChildAssistantController(AppDbContext db, IParentChildAccess access, IParentAccessAuditLog audit,
        IStringLocalizer<Messages>? localizer = null)
    {
        _db = db;
        _access = access;
        _audit = audit;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    [HttpPut]
    [EnableRateLimiting(ParentLinkRateLimiting.ManagePolicy)]
    public async Task<IActionResult> Put(int studentId, [FromBody] ParentChildAssistantSettingDto request, CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = _localizer["auth.userNotResolved"].Value });

        var grant = await _access.EnsureActiveChildAsync(user.Id, studentId, ct);
        if (grant is null)
            return NotFound(new
            {
                message = _localizer["parentLinks.errors.childNotFound"].Value,
                errorCode = ParentLinkErrorCodes.NotFound
            });

        await _db.ParentStudentLinks
            .Where(l => l.Id == grant.LinkId && l.Status == ParentStudentLinkStatus.Active)
            .ExecuteUpdateAsync(set => set.SetProperty(l => l.AssistantBlocked, !request.Allowed), ct);
        await _audit.RecordAsync(grant.ParentId, grant.StudentId, ParentAccessEndpoints.ChildAssistantSetting,
            request.Allowed ? 1 : 0, ct);

        return Ok(new ParentChildAssistantStateDto
        {
            Allowed = request.Allowed,
            EffectiveAllowed = await ParentAssistantControl.IsAllowedAsync(_db, grant.StudentId, ct),
        });
    }
}
