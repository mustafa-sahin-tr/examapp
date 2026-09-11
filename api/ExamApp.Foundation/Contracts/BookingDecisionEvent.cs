using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Ders planlama akışı (issue #96) — öğretmen bir randevu talebini onayladığında ya da
/// reddettiğinde exam API tarafından outbox'a yazılır. BadgeService bunu tüketip talebi
/// oluşturan öğrenciye in-app bildirim oluşturur ve SignalR ile push eder.
///
/// Onay/ret tek event'te <see cref="Approved"/> alanıyla ayrılır — <c>DecideAsync</c> zaten
/// tek metotta iki kararı da işlediği için outbox tarafında da tek event tipi tercih edildi
/// (bkz. atama izni akışındaki iki ayrı event'in alternatifi; burada tutarlılık BookingService'in
/// kendi iç deseniyle sağlandı).
///
/// Payload minimum tutulur: id'ler + SignalR hedeflemesi için öğrencinin Keycloak subject'i +
/// bildirim metninde kullanılacak öğretmen adı/zaman bilgisi. Hassas veri taşınmaz.
/// </summary>
public class BookingDecisionEvent
{
    /// <summary>Booking.Id — idempotency anahtarı.</summary>
    public int BookingId { get; set; }

    public int TeacherId { get; set; }

    /// <summary>Bildirim metninde kullanılır; consumer'ın exam API'ye geri sormasını önler.</summary>
    public string TeacherName { get; set; } = string.Empty;

    public int StudentId { get; set; }

    /// <summary>Talebi oluşturan öğrencinin exam/auth user id'si — Notifications tablosunda saklanır.</summary>
    public int StudentUserId { get; set; }

    /// <summary>
    /// Öğrencinin Keycloak subject'i (NameIdentifier claim). BadgeService
    /// <c>Clients.User(...)</c> hedeflemesi ve Notification.UserKeycloakId bunu kullanır.
    /// </summary>
    public string TargetKeycloakId { get; set; } = string.Empty;

    public bool Approved { get; set; }

    /// <summary>Sadece ret durumunda dolu olabilir.</summary>
    public string? RejectionReason { get; set; }

    public DateOnly Date { get; set; }

    public TimeOnly StartTime { get; set; }

    public TimeOnly EndTime { get; set; }

    /// <summary>Kararın verildiği an (UTC).</summary>
    public DateTime DecidedAt { get; set; }
}
