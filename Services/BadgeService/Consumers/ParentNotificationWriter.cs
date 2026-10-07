using BadgeService.Entities;
using BadgeService.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Consumers;

/// <summary>
/// issue #423 (epic #407 V5): veli bildirimi consumer'larının ortak yazıcısı. Tek alıcılı notification satırını
/// <c>(Type, SourceEventId)</c> ile tekilleştirir (filtreli unique index + önceden kontrol) ve SignalR ile push eder.
/// Aynı event'ten iki farklı alıcı (ParentLinked: veli + öğrenci) FARKLI Type ile yazılır, böylece global unique index
/// ihlal edilmez ve biri düşse (ör. sub çözülemedi → retry) tekrar teslimde yalnız eksik olan yazılır.
/// </summary>
internal static class ParentNotificationWriter
{
    /// <summary>SignalR method adı — UI yalnızca zil sayacını tazeler.</summary>
    public const string HubMethod = "ParentNotification";

    public static async Task<bool> AlreadyProcessedAsync(BadgeDbContext db, string type, Guid eventId, CancellationToken ct)
        => await db.Notifications.AnyAsync(n => n.Type == type && n.SourceEventId == eventId, ct);

    /// <summary>
    /// Satırı yazar. Eşzamanlı ikinci teslim unique index'e takılırsa (23505) false döner (idempotent no-op, push yok).
    /// </summary>
    public static async Task<Notification?> TryAddAsync(BadgeDbContext db, Notification notification, CancellationToken ct)
    {
        db.Notifications.Add(notification);
        try
        {
            await db.SaveChangesAsync(ct);
            return notification;
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            db.Entry(notification).State = EntityState.Detached;
            return null;
        }
    }

    public static Task PushAsync(IHubContext<BadgeNotificationHub> hub, string sub, Notification n, string kind, object? extra, CancellationToken ct)
        => hub.Clients.User(sub).SendAsync(HubMethod, new
        {
            notificationId = n.Id,
            kind,
            title = n.Title,
            body = n.Body,
            data = extra
        }, ct);
}
