using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

public enum WorksheetReminderStatus
{
    Pending = 0,
    Sent = 1,
    Cancelled = 2
}

/// <summary>
/// "Planla &amp; Hatırlat" — bir öğrencinin belirli bir worksheet'i ne zaman çözmeyi planladığı
/// ve ne kadar önce hatırlatılmak istediği. Tetikleme Hangfire ile yapılır; asıl bildirim
/// teslimi outbox event üzerinden (event-integration-dev) gerçekleşir.
/// </summary>
public class WorksheetReminder : BaseEntity
{
    public int Id { get; set; }

    public int WorksheetId { get; set; }

    [ForeignKey(nameof(WorksheetId))]
    public Worksheet Worksheet { get; set; } = default!;

    public int StudentId { get; set; }

    [ForeignKey(nameof(StudentId))]
    public Student Student { get; set; } = default!;

    /// <summary>Sınavın planlandığı an (UTC).</summary>
    public DateTime ScheduledFor { get; set; }

    /// <summary>Sınavdan kaç dakika önce hatırlatılsın.</summary>
    public int RemindBeforeMinutes { get; set; } = 60;

    public WorksheetReminderStatus Status { get; set; } = WorksheetReminderStatus.Pending;

    /// <summary>Bu reminder için zamanlanmış Hangfire job kimliği.</summary>
    public string? HangfireJobId { get; set; }

    /// <summary>
    /// Öğrencinin Keycloak subject'i, reminder oluşturulurken (HTTP context varken) yakalanır.
    /// Dispatcher Hangfire job'ında çalıştığı için auth-api'ye senkron çağrı yapmadan
    /// SignalR hedeflemesini <see cref="WorksheetReminderDueEvent.UserKeycloakId"/> ile taşıyabilsin diye saklanır.
    /// </summary>
    public string? StudentKeycloakId { get; set; }
}
