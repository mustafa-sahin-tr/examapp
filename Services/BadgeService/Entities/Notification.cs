using System;

namespace BadgeService.Entities;

/// <summary>
/// Kullanıcıya gösterilecek in-app bildirim. Outbox event'lerini tüketen consumer'lar
/// (örn. <c>WorksheetReminderDueConsumer</c>) buraya satır yazar; frontend zil bileşeni okur.
/// </summary>
public class Notification
{
    public int Id { get; set; }

    /// <summary>exam/auth user id (BadgeService genelinde kullanılan UserId ile aynı).</summary>
    public int UserId { get; set; }

    /// <summary>
    /// Öğrencinin Keycloak subject'i. Sahiplik kontrolü (IDOR koruması) ve SignalR
    /// hedeflemesi bunun üzerinden yapılır; API sorguları çağıranın sub'ı ile filtrelenir.
    /// </summary>
    public string? UserKeycloakId { get; set; }

    public string Type { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Body { get; set; } = string.Empty;

    /// <summary>Serbest biçimli JSON — client'ın derin link kurması için (worksheetId vb.).</summary>
    public string? Data { get; set; }

    /// <summary>
    /// Idempotency anahtarı: bir reminder'dan üretilen bildirim tekilliğini sağlar.
    /// Reminder kaynaklı olmayan bildirimlerde null.
    /// </summary>
    public int? SourceReminderId { get; set; }

    /// <summary>
    /// Idempotency anahtarı: bir atama izni talebinden (WorksheetAccessRequest) üretilen bildirim
    /// tekilliğini sağlar. Talep/karar kaynaklı olmayan bildirimlerde null.
    /// </summary>
    public int? SourceAccessRequestId { get; set; }

    public bool IsRead { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
