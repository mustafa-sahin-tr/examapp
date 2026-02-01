using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Controllers;

[ApiController]
[Route("api/reset")]
[Authorize]
public class ResetController : ControllerBase
{
    private readonly BadgeDbContext _db;

    public ResetController(BadgeDbContext db)
    {
        _db = db;
    }

    [HttpDelete("users/{userId:int}")]
    public async Task<IActionResult> ResetUserAsync(int userId, CancellationToken cancellationToken)
    {
        if (userId <= 0)
        {
            return BadRequest(new { message = "Invalid userId" });
        }

        var daily = await _db.StudentDailyActivities
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);
        if (daily.Count > 0) _db.StudentDailyActivities.RemoveRange(daily);

        var progress = await _db.StudentBadgeProgresses
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);
        if (progress.Count > 0) _db.StudentBadgeProgresses.RemoveRange(progress);

        var earned = await _db.BadgeEarned
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);
        if (earned.Count > 0) _db.BadgeEarned.RemoveRange(earned);

        var questionAgg = await _db.StudentQuestionAggregates
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);
        if (questionAgg.Count > 0) _db.StudentQuestionAggregates.RemoveRange(questionAgg);

        var subjectAgg = await _db.StudentSubjectAggregates
            .Where(x => x.UserId == userId)
            .ToListAsync(cancellationToken);
        if (subjectAgg.Count > 0) _db.StudentSubjectAggregates.RemoveRange(subjectAgg);

        await _db.SaveChangesAsync(cancellationToken);

        return Ok(new { message = "User badge/activity data reset." });
    }
}
