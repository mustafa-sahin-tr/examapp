using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Atama izni akışı — bir öğretmen, kendisine ait olmayan <c>PublicView</c> bir sınav (worksheet)
/// için atama izni talep ettiğinde exam API tarafından outbox'a yazılır. BadgeService bunu tüketip
/// sınavın sahibine in-app bildirim (Notifications tablosu) oluşturur ve SignalR ile push eder.
///
/// Payload minimum tutulur: id'ler + SignalR hedeflemesi için sahibin Keycloak subject'i +
/// bildirim metninde kullanılacak worksheet/talep eden adı. Hassas veri (e-posta, token) taşınmaz.
/// </summary>
public class WorksheetAccessRequestedEvent
{
    /// <summary>WorksheetAccessRequest.Id — idempotency anahtarı.</summary>
    public int RequestId { get; set; }

    public int WorksheetId { get; set; }

    /// <summary>Bildirim metninde kullanılır; consumer'ın exam API'ye geri sormasını önler.</summary>
    public string WorksheetName { get; set; } = string.Empty;

    /// <summary>Talebi yapan öğretmenin exam/auth user id'si.</summary>
    public int RequesterUserId { get; set; }

    /// <summary>Talebi yapan öğretmenin görünen adı; bildirim metninde kullanılır.</summary>
    public string RequesterName { get; set; } = string.Empty;

    /// <summary>Sınavın sahibinin exam/auth user id'si — Notifications tablosunda saklanır.</summary>
    public int OwnerUserId { get; set; }

    /// <summary>
    /// Sınav sahibinin Keycloak subject'i (NameIdentifier claim). BadgeService
    /// <c>Clients.User(...)</c> hedeflemesi ve Notification.UserKeycloakId bunu kullanır.
    /// </summary>
    public string TargetKeycloakId { get; set; } = string.Empty;

    /// <summary>Talep eden öğretmenin bıraktığı serbest not (opsiyonel).</summary>
    public string? Note { get; set; }

    /// <summary>Talebin oluşturulduğu an (UTC).</summary>
    public DateTime RequestedAt { get; set; }
}
