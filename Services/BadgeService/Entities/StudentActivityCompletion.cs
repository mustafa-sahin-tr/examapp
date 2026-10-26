using System;

namespace BadgeService.Entities;

/// <summary>
/// Issue #572 (epic #569): bir öğrencinin tamamladığı etkinlik denemesi (<c>ActivityCompletedEvent</c>) için idempotency
/// ledger + etkinlik rozetlerinin (sayı / süre / seri) tek veri kaynağı. Puan, XP, günlük aktivite
/// (<see cref="StudentDailyActivity"/>) ve soru/ders aggregate'leriyle HİÇBİR bağı yoktur — etkinlikler puansızdır.
/// <para>
/// Idempotency anahtarları (ikisi de unique): <see cref="EventId"/> (outbox satırı Id'si; aynı mesajın tekrar teslimi) ve
/// (<see cref="UserId"/>, <see cref="WorksheetInstanceId"/>) (öğrenci + deneme; farklı EventId ile gelen aynı deneme).
/// Sayı rozeti ayrıca (UserId, ActivityId) üzerinden DISTINCT sayılır: aynı etkinliği tekrar çözmek yeni satır
/// ekler (süre/seri için sayılır) ama sayıyı artırmaz.
/// </para>
/// </summary>
public class StudentActivityCompletion
{
    public Guid Id { get; set; }

    /// <summary>Producer-assigned <c>ActivityCompletedEvent.EventId</c> (= outbox satırı Id'si). Unique.</summary>
    public Guid EventId { get; set; }

    public int UserId { get; set; }

    /// <summary>Etkinlik = <c>Worksheets.Id</c>.</summary>
    public int ActivityId { get; set; }

    /// <summary>Deneme = <c>WorksheetInstances.Id</c>. (UserId, WorksheetInstanceId) unique.</summary>
    public int WorksheetInstanceId { get; set; }

    public int? SubjectId { get; set; }

    /// <summary>Etkin çalışma süresi (sn); tüketicide <see cref="Services.ActivityCompletionStats.MaxActiveSecondsPerActivity"/>'e savunmacı olarak yeniden kırpılır.</summary>
    public int ActiveSeconds { get; set; }

    /// <summary>Oturumdaki madde sayısı (boş bırakılanlar dahil) — denetim için saklanır.</summary>
    public int ItemCount { get; set; }

    /// <summary>Cevaplanan madde sayısı — denetim için saklanır.</summary>
    public int AnsweredItemCount { get; set; }

    /// <summary>
    /// Tüketicide <c>AnsweredItemCount &gt;= max(1, ceil(ItemCount/2))</c> ile hesaplanıp kalıcı yazılır. Sayı
    /// (<c>CompletedActivityCount</c>), seri (<c>ActivityDayStreak</c>) ve "bugün" sayacı yalnız bunu sağlayan satırları sayar;
    /// süre (<c>ActivityStudyTimeMinutes</c>) her tamamlamayı sayar. Bool seçildi: SQL filtresi tek kolon (tavan/ceil ifadesi
    /// yok) ve kural sonradan değişse bile tarihsel satırların o günkü kararı korunur.
    /// </summary>
    public bool CountsTowardBadges { get; set; }

    public DateTime CompletedAtUtc { get; set; }

    /// <summary><see cref="CompletedAtUtc"/>'nin Europe/Istanbul yerel günü (yalnız tarih; Kind=Unspecified). Seri bu güne göre hesaplanır.</summary>
    public DateTime ActivityDate { get; set; }
}
