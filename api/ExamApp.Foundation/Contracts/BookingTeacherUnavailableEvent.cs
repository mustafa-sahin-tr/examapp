using System;
using System.Collections.Generic;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// issue #298: admin bir öğretmenin hesap onayını askıya aldığında (#289) exam API, öğretmenin henüz bitmemiş ONAYLI
/// randevusu olan HER ÖĞRENCİ için bu event'ten TEK bir tane yazar (aynı öğretmenle birden fazla randevu → tek event,
/// <see cref="BookingIds"/> hepsini taşır). BadgeService öğrenciye "öğretmen geçici olarak müsait değil" bildirimi
/// oluşturur ve SignalR ile push eder. Randevular iptal edilmez (iptal kuralları #315); yalnızca bilgilendirme.
///
/// Neden ayrı event (BookingDecisionEvent'e yeni karar türü değil): o event randevu BAŞINA bir karardır ve
/// (Type, BookingId) ile tekilleşir; burada öğrenci başına tek bildirim gerekir ve randevunun durumu değişmez.
///
/// Güvenlik kararı: payload'da PII (öğretmen/öğrenci adı, e-posta) ve askı nedeni YOK. Öğrencinin Keycloak sub'ı
/// yalnızca SignalR hedeflemesi için taşınır (diğer booking event'leriyle aynı).
/// </summary>
public class BookingTeacherUnavailableEvent
{
    /// <summary>Idempotency anahtarı — (askı, öğrenci) başına üretilen tek event.</summary>
    public Guid EventId { get; set; }

    /// <summary>Teacher.Id.</summary>
    public int TeacherId { get; set; }

    /// <summary>Etkilenen öğrencinin exam/auth user id'si — Notifications.UserId.</summary>
    public int StudentUserId { get; set; }

    /// <summary>
    /// Öğrencinin Keycloak subject'i. Askı anında auth-api'den çözülemezse boş gelir; consumer sub'ı BadgeService
    /// verisinden (dil tercihi / önceki bildirim) çözer, çözemezse retry → dead-letter.
    /// </summary>
    public string TargetKeycloakId { get; set; } = string.Empty;

    /// <summary>Öğrencinin bu öğretmenle henüz bitmemiş Approved randevuları (Booking.Id). UI derin linki için.</summary>
    public List<int> BookingIds { get; set; } = new();

    /// <summary>Askıya alma anı (UTC).</summary>
    public DateTime UnavailableSinceUtc { get; set; }
}
