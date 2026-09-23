using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Services;

/// <summary>
/// Bir kullanıcının badge/aktivite/puan verisini sıfırlar (exam API StudentResetJob'unun BadgeService ayağı).
///
/// issue #225: kullanıcının puan aggregate'ı varsa, silmeyle AYNI SaveChanges'te outbox'a mutlak
/// <c>TotalPoints = 0</c> event'i yazılır. exam API StudentResetJob kendi StudentPoints satırını zaten
/// soft-delete ediyor; bu event, reset sırasında uçuşta olan eski bir StudentPointsChangedEvent'in satırı
/// eski puanla geri getirmesini engeller (daha yeni versiyon kazanır). Aggregate yoksa exam API'ye
/// taşınmış bir puan da yoktur → event yazılmaz.
/// </summary>
public class UserResetService
{
    private readonly BadgeDbContext _db;

    public UserResetService(BadgeDbContext db) => _db = db;

    /// <returns>Puan aggregate'ı vardı ve sıfırlama event'i yazıldıysa true.</returns>
    public async Task<bool> ResetAsync(int userId, CancellationToken cancellationToken = default)
    {
        if (userId <= 0) throw new ArgumentOutOfRangeException(nameof(userId));

        var daily = await _db.StudentDailyActivities.Where(x => x.UserId == userId).ToListAsync(cancellationToken);
        if (daily.Count > 0) _db.StudentDailyActivities.RemoveRange(daily);

        var progress = await _db.StudentBadgeProgresses.Where(x => x.UserId == userId).ToListAsync(cancellationToken);
        if (progress.Count > 0) _db.StudentBadgeProgresses.RemoveRange(progress);

        var earned = await _db.BadgeEarned.Where(x => x.UserId == userId).ToListAsync(cancellationToken);
        if (earned.Count > 0) _db.BadgeEarned.RemoveRange(earned);

        var questionAgg = await _db.StudentQuestionAggregates.Where(x => x.UserId == userId).ToListAsync(cancellationToken);
        if (questionAgg.Count > 0) _db.StudentQuestionAggregates.RemoveRange(questionAgg);

        var subjectAgg = await _db.StudentSubjectAggregates.Where(x => x.UserId == userId).ToListAsync(cancellationToken);
        if (subjectAgg.Count > 0) _db.StudentSubjectAggregates.RemoveRange(subjectAgg);

        var pointsReset = questionAgg.Count > 0;
        if (pointsReset)
        {
            StudentPointsOutbox.Enqueue(_db, userId, 0, DateTime.UtcNow);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return pointsReset;
    }
}
