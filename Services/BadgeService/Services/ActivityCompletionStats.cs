using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BadgeService.Entities;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Services;

/// <summary>
/// Issue #572: etkinlik rozet istatistikleri (sayı / süre / seri). Yalnız <see cref="StudentActivityCompletion"/>
/// satırlarından hesaplanır; soru aggregate'lerine ve <see cref="StudentDailyActivity"/>'ye bakmaz.
/// </summary>
public static class ActivityCompletionStats
{
    /// <summary>Etkinlik başına süre üst sınırı (sn). Producer zaten kırpar; burada savunmacı ikinci kırpma.</summary>
    public const int MaxActiveSecondsPerActivity = 1800;

    private static readonly Lazy<TimeZoneInfo> Istanbul = new(() =>
    {
        try { return TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul"); }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            // tzdata'sı olmayan minimal imaj: Türkiye 2016'dan beri sabit UTC+3 (DST yok).
            return TimeZoneInfo.CreateCustomTimeZone("Europe/Istanbul", TimeSpan.FromHours(3), "Europe/Istanbul", "Europe/Istanbul");
        }
    });

    /// <summary>UTC anının Europe/Istanbul yerel günü (yalnız tarih, Kind=Unspecified).</summary>
    public static DateTime ToIstanbulDate(DateTime utc)
    {
        var asUtc = utc.Kind switch
        {
            DateTimeKind.Utc => utc,
            DateTimeKind.Local => utc.ToUniversalTime(),
            _ => DateTime.SpecifyKind(utc, DateTimeKind.Utc),
        };
        return TimeZoneInfo.ConvertTimeFromUtc(asUtc, Istanbul.Value).Date;
    }

    /// <summary>Sayı/seri için nitelik: cevaplanan madde sayısı >= max(1, ceil(ItemCount/2)).</summary>
    public static bool QualifiesForBadges(int itemCount, int answeredItemCount) =>
        answeredItemCount >= Math.Max(1, (Math.Max(0, itemCount) + 1) / 2);

    public static int ClampSeconds(int seconds) => Math.Clamp(seconds, 0, MaxActiveSecondsPerActivity);

    /// <summary>
    /// Seri penceresi (gün): seri hesabı için yalnız referans günden geriye bu kadar günlük DISTINCT <c>ActivityDate</c>
    /// yüklenir. Sonuç: bir seri en fazla bu değere ulaşabilir; bu yüzden <c>ActivityDayStreak</c> hedefi katalogda
    /// <see cref="MaxStreakTarget"/> ile sınırlıdır (daha büyük hedef ulaşılamaz olurdu).
    /// </summary>
    public const int StreakWindowDays = 400;

    public const int MaxStreakTarget = StreakWindowDays;

    private static IQueryable<StudentActivityCompletion> ForUser(BadgeDbContext db, int userId, int? subjectId)
    {
        var q = db.StudentActivityCompletions.AsNoTracking().Where(x => x.UserId == userId);
        return subjectId.HasValue ? q.Where(x => x.SubjectId == subjectId.Value) : q;
    }

    /// <summary>COUNT(DISTINCT ActivityId) — yalnız nitelikli (<c>CountsTowardBadges</c>) tamamlamalar, SQL'de.</summary>
    public static Task<int> DistinctActivityCountAsync(BadgeDbContext db, int userId, int? subjectId, CancellationToken ct) =>
        ForUser(db, userId, subjectId).Where(x => x.CountsTowardBadges).Select(x => x.ActivityId).Distinct().CountAsync(ct);

    /// <summary>Süre: nitelik filtresi YOK, her tamamlama sayılır. floor(SUM(min(ActiveSeconds, 1800)) / 60) — SQL'de. <paramref name="date"/> verilirse yalnız o gün.</summary>
    public static async Task<int> StudyMinutesAsync(BadgeDbContext db, int userId, int? subjectId, DateTime? date, CancellationToken ct)
    {
        var q = ForUser(db, userId, subjectId);
        if (date.HasValue) { var d = date.Value.Date; q = q.Where(x => x.ActivityDate == d); }
        var total = await q.SumAsync(x => (long)(x.ActiveSeconds > MaxActiveSecondsPerActivity ? MaxActiveSecondsPerActivity : x.ActiveSeconds), ct);
        return (int)Math.Min(int.MaxValue, total / 60);
    }

    public static Task<int> CountOnDateAsync(BadgeDbContext db, int userId, DateTime date, CancellationToken ct)
    {
        var d = date.Date;
        return ForUser(db, userId, null).CountAsync(x => x.CountsTowardBadges && x.ActivityDate == d, ct);
    }

    /// <summary>
    /// Güncel seri: <paramref name="referenceDate"/> gününde etkinlik varsa o günden, yoksa önceki günden geriye ardışık
    /// gün sayısı (dün biten seri henüz bozulmamıştır). Yalnız son <see cref="StreakWindowDays"/> günün DISTINCT günleri yüklenir.
    /// </summary>
    public static async Task<int> CurrentDayStreakAsync(BadgeDbContext db, int userId, int? subjectId, DateTime referenceDate, CancellationToken ct)
    {
        var refDate = referenceDate.Date;
        var from = refDate.AddDays(-(StreakWindowDays - 1));
        var list = await ForUser(db, userId, subjectId)
            .Where(x => x.CountsTowardBadges && x.ActivityDate >= from && x.ActivityDate <= refDate)
            .Select(x => x.ActivityDate).Distinct().ToListAsync(ct);
        var days = list.Select(d => d.Date).ToHashSet();

        var cursor = refDate;
        if (!days.Contains(cursor)) cursor = cursor.AddDays(-1);
        var streak = 0;
        while (days.Contains(cursor)) { streak++; cursor = cursor.AddDays(-1); }
        return streak;
    }

    /// <summary>Etkinlik kuralının güncel değeri (SQL toplama + sınırlı pencere).</summary>
    public static Task<int> ComputeCurrentAsync(BadgeDbContext db, int userId, ActivityRule rule, DateTime referenceDate, CancellationToken ct) =>
        rule.Kind switch
        {
            ActivityRuleKind.Count => DistinctActivityCountAsync(db, userId, rule.SubjectId, ct),
            ActivityRuleKind.Minutes => StudyMinutesAsync(db, userId, rule.SubjectId, null, ct),
            _ => CurrentDayStreakAsync(db, userId, rule.SubjectId, referenceDate, ct),
        };
}

public enum ActivityRuleKind { Count, Minutes, Streak }

public readonly record struct ActivityRule(ActivityRuleKind Kind, int Target, int? SubjectId);
