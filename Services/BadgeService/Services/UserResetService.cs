using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BadgeService.Entities;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BadgeService.Services;

/// <summary>
/// Bir kullanıcının badge/aktivite/puan verisini sıfırlar (exam API StudentResetJob'unun BadgeService ayağı).
///
/// issue #225: kullanıcının puan aggregate'ı varsa, silmeyle AYNI SaveChanges'te outbox'a mutlak
/// <c>TotalPoints = 0</c> event'i yazılır. exam API StudentResetJob kendi StudentPoints satırını zaten
/// soft-delete ediyor; bu event, reset sırasında uçuşta olan eski bir StudentPointsChangedEvent'in satırı
/// eski puanla geri getirmesini engeller (daha yeni versiyon kazanır). Aggregate yoksa exam API'ye
/// taşınmış bir puan da yoktur → event yazılmaz.
///
/// issue #396: aynı SaveChanges'te kullanıcının <see cref="UserResetMarker"/>'ı (sıfırlama zamanı) yazılır; sıfırlamadan
/// önce gönderilmiş, yolda olan AnswerSubmittedEvent'ler bu çizgiye göre yok sayılır. exam API StudentResetJob artık
/// bu çağrıyı exam verisini silmeden ÖNCE yapar (başarısızsa exam verisi yerinde kalır, iş yeniden dener) ve çizgiyi
/// kendi saatinden gönderir (<c>SubmittedAt</c> ile aynı saat).
/// </summary>
public class UserResetService
{
    /// <summary>issue #396: çağıranın gönderdiği çizgi bundan daha ileri tarihliyse şimdiki zamana kırpılır.</summary>
    internal static readonly TimeSpan MaxResetAtSkew = TimeSpan.FromMinutes(5);

    private readonly BadgeDbContext _db;
    private readonly ILogger<UserResetService> _logger;

    public UserResetService(BadgeDbContext db, ILogger<UserResetService>? logger = null)
    {
        _db = db;
        _logger = logger ?? NullLogger<UserResetService>.Instance;
    }

    /// <param name="resetAtUtc">
    /// issue #396: sıfırlama çizgisi — exam API'nin saatiyle (AnswerSubmittedEvent.SubmittedAt ile aynı saat). null ise
    /// (eski çağıran) BadgeService saati. Gelecekte (&gt; 5 dk) bir değer şimdiki zamana kırpılır: aksi halde o ana kadar
    /// gönderilen meşru cevaplar da yok sayılırdı.
    /// </param>
    /// <returns>Puan aggregate'ı vardı ve sıfırlama event'i yazıldıysa true.</returns>
    public async Task<bool> ResetAsync(int userId, DateTime? resetAtUtc = null, CancellationToken cancellationToken = default)
    {
        if (userId <= 0) throw new ArgumentOutOfRangeException(nameof(userId));

        var daily = await _db.StudentDailyActivities.Where(x => x.UserId == userId).ToListAsync(cancellationToken);
        if (daily.Count > 0) _db.StudentDailyActivities.RemoveRange(daily);

        var progress = await _db.StudentBadgeProgresses.Where(x => x.UserId == userId).ToListAsync(cancellationToken);
        if (progress.Count > 0) _db.StudentBadgeProgresses.RemoveRange(progress);

        var earned = await _db.BadgeEarned.Where(x => x.UserId == userId).ToListAsync(cancellationToken);
        if (earned.Count > 0) _db.BadgeEarned.RemoveRange(earned);

        // issue #146: rozetler sıfırlanırken "rozet kazandın" bildirimleri de silinir; aksi halde yeniden
        // kazanımda (UserId, SourceBadgeDefinitionId) unique index'i yeni bildirimi engellerdi.
        var badgeNotifications = await _db.Notifications
            .Where(x => x.UserId == userId && x.Type == BadgeEvaluator.NotificationType).ToListAsync(cancellationToken);
        if (badgeNotifications.Count > 0) _db.Notifications.RemoveRange(badgeNotifications);

        var questionAgg = await _db.StudentQuestionAggregates.Where(x => x.UserId == userId).ToListAsync(cancellationToken);
        if (questionAgg.Count > 0) _db.StudentQuestionAggregates.RemoveRange(questionAgg);

        var subjectAgg = await _db.StudentSubjectAggregates.Where(x => x.UserId == userId).ToListAsync(cancellationToken);
        if (subjectAgg.Count > 0) _db.StudentSubjectAggregates.RemoveRange(subjectAgg);

        // issue #279 (item 3 + item 4): reset/KVKK silmede kullanıcının idempotency ledger'ı
        // (ProcessedAnswerSubmission) ve puan-tekilleştirme durumu (AnswerPointAward) da silinir —
        // aksi halde reset sonrası aynı (TestInstanceId, QuestionId) için gelecek bir mesaj eski
        // "PointsAwarded"ı baz alıp yanlış delta hesaplardı.
        var processedSubmissions = await _db.ProcessedAnswerSubmissions.Where(x => x.UserId == userId).ToListAsync(cancellationToken);
        if (processedSubmissions.Count > 0) _db.ProcessedAnswerSubmissions.RemoveRange(processedSubmissions);

        var pointAwards = await _db.AnswerPointAwards.Where(x => x.UserId == userId).ToListAsync(cancellationToken);
        if (pointAwards.Count > 0) _db.AnswerPointAwards.RemoveRange(pointAwards);

        // issue #396: sıfırlama çizgisi — bundan önce gönderilmiş (SubmittedAt < ResetAtUtc) ama henüz tüketilmemiş
        // AnswerSubmittedEvent'ler AnswerSubmissionAggregationService'te yok sayılır. Silmelerle aynı SaveChanges:
        // ya ikisi birden ya hiçbiri. Çizgi yalnız ileri gider (sırasız/tekrar çağrı geri almaz).
        var now = EventVersion.Normalize(DateTime.UtcNow);
        var resetAt = ResolveResetAt(userId, resetAtUtc, now);
        var marker = await _db.UserResetMarkers.FirstOrDefaultAsync(x => x.UserId == userId, cancellationToken);
        if (marker == null)
        {
            _db.UserResetMarkers.Add(new UserResetMarker { UserId = userId, ResetAtUtc = resetAt });
        }
        else if (resetAt > marker.ResetAtUtc)
        {
            marker.ResetAtUtc = resetAt;
        }

        var pointsReset = questionAgg.Count > 0;
        if (pointsReset)
        {
            StudentPointsOutbox.Enqueue(_db, userId, 0, now);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return pointsReset;
    }

    private DateTime ResolveResetAt(int userId, DateTime? requested, DateTime now)
    {
        if (requested is null)
            return now;

        var resetAt = EventVersion.Normalize(requested.Value);
        if (resetAt > now + MaxResetAtSkew)
        {
            _logger.LogWarning(
                "[UserReset] İleri tarihli sıfırlama çizgisi şimdiki zamana kırpıldı (UserId={UserId}, Requested={Requested:o}, Now={Now:o}).",
                userId, resetAt, now);
            return now;
        }

        return resetAt;
    }
}
