using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Parents;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Localization;

namespace ExamApp.Api.Controllers;

/// <summary>Issue #523: öğrencinin asistan erişimi.</summary>
public sealed class StudentAssistantAccessDto
{
    /// <summary>false: bir velisi asistanı kapattı.</summary>
    public bool Allowed { get; set; }

    /// <summary>
    /// Issue #1353: the student has a timed test running (started, not finished, time left). The assistant refuses chat then,
    /// whatever route the client reports (another tab cannot be used to ask about the test).
    /// </summary>
    public bool TestInProgress { get; set; }
}

/// <summary>
/// Issue #523: öğrenci asistanı kullanabilir mi (velisi kapatmadıysa). AssistantService her sohbet turunun başında öğrencinin token'ıyla
/// çağırır (iç ağ); UI da asistan düğmesini buna göre gizler. Gateway: <c>/api/exam/student/assistant-access</c>.
/// </summary>
[ApiController]
[Route("api/student/assistant-access")]
[Authorize(Roles = "Student")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public class StudentAssistantAccessController : BaseController
{
    private readonly AppDbContext _db;
    private readonly IStringLocalizer<Messages> _localizer;

    public StudentAssistantAccessController(AppDbContext db, IStringLocalizer<Messages>? localizer = null)
    {
        _db = db;
        _localizer = localizer ?? FallbackMessageLocalizer.Instance;
    }

    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        var user = await GetAuthenticatedUserAsync(ct);
        if (!IsResolvedUser(user))
            return UserNotResolved(new { message = _localizer["auth.userNotResolved"].Value });

        var studentId = await _db.Students.AsNoTracking().Where(s => s.UserId == user.Id).Select(s => (int?)s.Id).FirstOrDefaultAsync(ct);
        // Öğrenci profili yoksa velisi de yoktur: açık.
        var allowed = studentId is not { } id || await ParentAssistantControl.IsAllowedAsync(_db, id, ct);
        var testInProgress = false;
        if (studentId is { } sid)
        {
            var now = DateTime.UtcNow;
            testInProgress = await _db.TestInstances.AsNoTracking().AnyAsync(ti =>
                ti.StudentId == sid
                && ti.Status == WorksheetInstanceStatus.Started
                && ti.MaxDurationSeconds > 0
                && ti.StartTime.AddSeconds((double)ti.MaxDurationSeconds!.Value) > now, ct);
        }
        return Ok(new StudentAssistantAccessDto { Allowed = allowed, TestInProgress = testInProgress });
    }
}
