using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Atama izni akışı — sınavın sahibi bir atama izni talebini onayladığında exam API tarafından
/// outbox'a yazılır. BadgeService bunu tüketip talebi yapan öğretmene in-app bildirim oluşturur
/// ve SignalR ile push eder.
///
/// Payload minimum tutulur: id'ler + SignalR hedeflemesi için talep edenin Keycloak subject'i +
/// bildirim metninde kullanılacak worksheet adı. Hassas veri taşınmaz.
/// </summary>
public class WorksheetAccessRequestApprovedEvent
{
    /// <summary>WorksheetAccessRequest.Id — idempotency anahtarı.</summary>
    public int RequestId { get; set; }

    public int WorksheetId { get; set; }

    /// <summary>Bildirim metninde kullanılır; consumer'ın exam API'ye geri sormasını önler.</summary>
    public string WorksheetName { get; set; } = string.Empty;

    /// <summary>Talebi yapan öğretmenin exam/auth user id'si — Notifications tablosunda saklanır.</summary>
    public int RequesterUserId { get; set; }

    /// <summary>
    /// Talebi yapan öğretmenin Keycloak subject'i (NameIdentifier claim). BadgeService
    /// <c>Clients.User(...)</c> hedeflemesi ve Notification.UserKeycloakId bunu kullanır.
    /// </summary>
    public string TargetKeycloakId { get; set; } = string.Empty;

    /// <summary>Kararın verildiği an (UTC).</summary>
    public DateTime DecidedAt { get; set; }
}
