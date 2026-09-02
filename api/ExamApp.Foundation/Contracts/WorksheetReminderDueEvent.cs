using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// "Planla &amp; Hatırlat" — bir worksheet hatırlatmasının tetiklenme anı geldiğinde
/// exam API tarafından outbox'a yazılır. BadgeService bunu tüketip in-app bildirim
/// (Notifications tablosu) oluşturur ve SignalR ile öğrenciye push eder.
///
/// Payload minimum tutulur: id'ler + SignalR hedeflemesi için Keycloak subject +
/// bildirim metninde kullanılacak worksheet adı. Hassas veri (e-posta, token) taşınmaz.
/// </summary>
public class WorksheetReminderDueEvent
{
    /// <summary>WorksheetReminder.Id — idempotency anahtarı.</summary>
    public int ReminderId { get; set; }

    public int WorksheetId { get; set; }

    /// <summary>Student.Id (exam DB).</summary>
    public int StudentId { get; set; }

    /// <summary>Student.UserId (exam/auth user id) — Notifications tablosunda saklanır.</summary>
    public int UserId { get; set; }

    /// <summary>
    /// Öğrencinin Keycloak subject'i (NameIdentifier claim). BadgeService
    /// <c>Clients.User(...)</c> hedeflemesi bunu kullanır — badge akışıyla aynı mekanizma.
    /// </summary>
    public string UserKeycloakId { get; set; } = string.Empty;

    /// <summary>Bildirim metninde kullanılır; consumer'ın exam API'ye geri sormasını önler.</summary>
    public string WorksheetName { get; set; } = string.Empty;

    /// <summary>Sınavın planlandığı an (UTC).</summary>
    public DateTime ScheduledFor { get; set; }

    public int RemindBeforeMinutes { get; set; }
}
