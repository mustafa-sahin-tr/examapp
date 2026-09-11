using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Ders planlama akışı (issue #96) — bir öğrenci bir öğretmenin müsaitlik aralığı için randevu
/// talebi oluşturduğunda exam API tarafından outbox'a yazılır. BadgeService bunu tüketip
/// öğretmene in-app bildirim (Notifications tablosu) oluşturur ve SignalR ile push eder.
///
/// Payload minimum tutulur: id'ler + SignalR hedeflemesi için öğretmenin Keycloak subject'i +
/// bildirim metninde kullanılacak öğrenci adı/zaman bilgisi. Hassas veri (e-posta, token) taşınmaz.
/// </summary>
public class BookingRequestCreatedEvent
{
    /// <summary>Booking.Id — idempotency anahtarı.</summary>
    public int BookingId { get; set; }

    public int TeacherId { get; set; }

    /// <summary>Öğretmenin exam/auth user id'si — Notifications tablosunda saklanır.</summary>
    public int TeacherUserId { get; set; }

    /// <summary>
    /// Öğretmenin Keycloak subject'i (NameIdentifier claim). BadgeService
    /// <c>Clients.User(...)</c> hedeflemesi ve Notification.UserKeycloakId bunu kullanır.
    /// </summary>
    public string TargetKeycloakId { get; set; } = string.Empty;

    public int StudentId { get; set; }

    /// <summary>Talebi oluşturan öğrencinin görünen adı; bildirim metninde kullanılır.</summary>
    public string StudentName { get; set; } = string.Empty;

    public int AvailabilitySlotId { get; set; }

    /// <summary>Randevu aralığının günü (bildirim metninde kullanılır; consumer geri sormaz).</summary>
    public DateOnly Date { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }

    /// <summary>Talebin oluşturulduğu an (UTC).</summary>
    public DateTime RequestedAt { get; set; }
}
