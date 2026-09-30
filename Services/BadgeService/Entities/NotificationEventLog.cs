namespace BadgeService.Entities;

/// <summary>
/// issue #305 (dilim B): bir yorum bildirimi event'inin (ilk yorum ya da okunmamış satıra birleştirilen) işlendiğinin kaydı.
/// Birleştirmede <see cref="Notification.SourceEventId"/> yalnız satırı açan ilk event'i tutar; sonraki event'lerin
/// tekrar teslimde sayacı ikinci kez artırmaması için EventId burada PK olarak tutulur ve sayaç güncellemesiyle AYNI
/// transaction'da eklenir (PK ihlali 23505 → transaction geri alınır, no-op).
/// </summary>
public class NotificationEventLog
{
    /// <summary>Bildirim tipi — tekillik (Type, EventId) çiftinedir (eski (Type, SourceEventId) sözleşmesi korunur).</summary>
    public string Type { get; set; } = string.Empty;

    public Guid EventId { get; set; }

    /// <summary>Event'in yazıldığı/birleştirildiği bildirim (FK değil: bildirim silinse de idempotency kaydı durur).</summary>
    public int NotificationId { get; set; }

    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
