using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Issue #106 (dilim b) — bir doğrudan mesaj/konuşma ŞİKAYET edildiğinde (yeni rapor; tekrar eden/idempotent şikayet
/// değil) exam API tarafından raporla AYNI transaction'da outbox'a yazılır. BadgeService tüketip admin(ler)e in-app bildirim
/// üretir ve SignalR admin grubuna push eder.
///
/// Güvenlik: şikayet notu, mesaj gövdesi, şikayet eden/edilen kimliği ve adları taşınmaz — admin ayrıntıyı rapor
/// listesinden (yetkili uç) okur. Yalnızca id'ler ve şikayet edenin rolü.
/// </summary>
public class DirectMessageReportedEvent
{
    /// <summary>Idempotency anahtarı — bu rapor için üretilen tek event'i tekilleştirir.</summary>
    public Guid EventId { get; set; }

    public int ReportId { get; set; }

    public int ConversationId { get; set; }

    /// <summary>Şikayet edenin rolü: "Student" ya da "Teacher".</summary>
    public string ReporterRole { get; set; } = string.Empty;

    /// <summary>Raporun oluşturulduğu an (UTC).</summary>
    public DateTime CreatedAtUtc { get; set; }
}
