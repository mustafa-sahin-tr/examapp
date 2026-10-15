using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Issue #441 — veli bağlamayan öğrencinin ek süresi bitmeden 7 gün ve 1 gün önce exam API (arka plan işi) outbox'a yazar.
/// Tüketici: BadgeService StudentParentGraceReminderConsumer (öğrenciye uygulama içi bildirim; e-posta değil).
/// </summary>
public class StudentParentGraceReminderEvent
{
    /// <summary>Students.Id.</summary>
    public int StudentId { get; set; }

    /// <summary>Öğrencinin Keycloak sub'ı (bildirim hedefi).</summary>
    public string StudentUserId { get; set; } = string.Empty;

    /// <summary>Bitime kalan gün (7 veya 1).</summary>
    public int DaysLeft { get; set; }

    /// <summary>Ek sürenin bitişi (UTC); (StudentId, GraceEndsAt, DaysLeft) idempotency anahtarı.</summary>
    public DateTime GraceEndsAt { get; set; }

    public DateTime OccurredAt { get; set; }
}
