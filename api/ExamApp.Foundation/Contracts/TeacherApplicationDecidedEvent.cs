using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Öğretmen başvurusu karar akışı (issue #157) — admin bağımsız öğretmen ya da okul bağlantısı
/// başvurusunu onayladığında/reddettiğinde exam API tarafından outbox'a yazılır. BadgeService bunu
/// tüketip başvuru sahibine in-app bildirim oluşturur ve SignalR ile push eder.
///
/// Güvenlik kararı (issue #157 security review): payload'da ret gerekçesi ve admin kimliği TAŞINMAZ.
/// Bildirim metni gerekçesiz sabit metindir ("onaylandı" / "reddedildi"); gerekçeyi öğrenmek isteyen
/// öğretmen kendi başvuru/profil sayfasından exam API'ye ayrıca sorar (RBAC ile korunur), event
/// payload'ı ya da SignalR mesajı üzerinden sızmaz.
///
/// Payload minimum tutulur: id'ler + SignalR hedeflemesi için başvuru sahibinin Keycloak subject'i +
/// karar (onay/ret) + karar anı. Hassas veri taşınmaz.
/// </summary>
public class TeacherApplicationDecidedEvent
{
    /// <summary>Idempotency anahtarı — bu karar için üretilen tek event'i tekilleştirir.</summary>
    public Guid EventId { get; set; }

    /// <summary>Teacher.Id.</summary>
    public int TeacherId { get; set; }

    /// <summary>
    /// Başvuru sahibinin Keycloak subject'i (NameIdentifier claim). BadgeService
    /// <c>Clients.User(...)</c> hedeflemesi ve Notification.UserKeycloakId bunu kullanır.
    /// Karar anında auth-api'den çözülemezse event hiç yazılmaz (bkz. TeacherApprovalService).
    /// </summary>
    public string TargetKeycloakId { get; set; } = string.Empty;

    /// <summary>true → onaylandı, false → reddedildi.</summary>
    public bool Approved { get; set; }

    /// <summary>
    /// issue #157 review: PII değil, öğretmenin başvuru türü (Teacher.IsIndependentTutor). UI'ın karar
    /// bildirimine tıklandığında doğru sayfaya yönlendirmesi için taşınır — bağımsız öğretmen (true)
    /// <c>/tutor-profile</c>'a, okul bağlantısı talebi (false) öğretmenin kendi profil sayfasına gider.
    /// </summary>
    public bool IsIndependentTutor { get; set; }

    /// <summary>Kararın verildiği an (UTC).</summary>
    public DateTime DecidedAtUtc { get; set; }
}
