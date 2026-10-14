using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Services;
using ExamApp.Foundation.Contracts;
using Microsoft.AspNetCore.SignalR;

namespace BadgeService.Consumers;

/// <summary>
/// issue #439: randevu event'lerinin çok alıcılı veli fan-out'u için ortak yazıcı. Bir event birden çok veliye bildirim üretir;
/// <c>(Type, SourceEventId)</c> unique index'i global olduğundan her alıcı için DETERMİNİSTİK bir
/// <see cref="RecipientKey"/> (event/booking anahtarı + ParentUserId üzerinden MD5 → Guid) <c>SourceEventId</c> olarak yazılır.
/// Böylece aynı mesajın tekrar teslimi yalnız eksik alıcıları yazar; bir alıcının sub'ı çözülemese bile (deterministik hata)
/// diğer alıcılar yine işlenir, hata sonda <see cref="RecipientSubUnresolvedException"/> olarak fırlatılır (→ error kuyruğu,
/// sessiz kayıp yok; tekrar oynatmada yalnız eksik olan yazılır).
/// </summary>
internal static class BookingParentNotifier
{
    /// <summary>Alıcı başına deterministik idempotency anahtarı: <c>MD5("{scope}:{parentUserId}")</c>.</summary>
    public static Guid RecipientKey(string scope, int parentUserId)
        => new(MD5.HashData(Encoding.UTF8.GetBytes($"{scope}:{parentUserId}")));

    public static string EventScope(Guid eventId) => $"booking-parent:{eventId:N}";

    /// <summary>BookingDecisionEvent'te EventId yok; talep (BookingId) başına tek öğretmen kararı olduğundan BookingId kullanılır.</summary>
    public static string BookingScope(int bookingId) => $"booking-decision:{bookingId}";

    public static async Task NotifyAsync(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        IUserLocaleResolver localeResolver,
        ILogger logger,
        IEnumerable<BookingParentRecipient>? recipients,
        string type,
        string scope,
        Func<CultureInfo, LocalizedNotificationText> buildText,
        object data,
        string logDescription,
        int? excludeParentUserId,
        CancellationToken ct)
    {
        if (recipients == null)
            return;

        RecipientSubUnresolvedException? unresolved = null;
        foreach (var r in recipients.Where(r => r.ParentUserId > 0 && r.ParentUserId != excludeParentUserId)
                     .GroupBy(r => r.ParentUserId).Select(g => g.First()))
        {
            var key = RecipientKey(scope, r.ParentUserId);
            if (await ParentNotificationWriter.AlreadyProcessedAsync(db, type, key, ct))
            {
                logger.LogInformation(
                    "{Type} zaten işlenmiş ({Log}, ParentUserId={ParentUserId}); atlanıyor.", type, logDescription, r.ParentUserId);
                continue;
            }

            string sub;
            try
            {
                sub = await NotificationRecipientResolver.ResolveSubAsync(
                    db, r.ParentUserId, r.KeycloakId, $"{type} ({logDescription}, ParentUserId={r.ParentUserId})", ct);
            }
            catch (RecipientSubUnresolvedException ex)
            {
                unresolved ??= ex;
                logger.LogError(ex, "{Type} alıcı sub'ı çözülemedi ({Log}, ParentUserId={ParentUserId}).", type, logDescription, r.ParentUserId);
                continue;
            }

            var culture = await localeResolver.ResolveAsync(r.ParentUserId, sub, ct);
            var text = buildText(culture);
            var notification = await ParentNotificationWriter.TryAddAsync(db, new Notification
            {
                UserId = r.ParentUserId,
                UserKeycloakId = sub,
                Type = type,
                Title = text.Title,
                Body = text.Body,
                Data = JsonSerializer.Serialize(data),
                SourceEventId = key,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            }, ct);
            if (notification == null)
            {
                logger.LogInformation(
                    "{Type} eşzamanlı duplicate ({Log}, ParentUserId={ParentUserId}); atlanıyor.", type, logDescription, r.ParentUserId);
                continue;
            }

            await ParentNotificationWriter.PushAsync(hub, sub, notification, type, data, ct);
            logger.LogInformation(
                "{Type} işlendi. {Log}, ParentUserId={ParentUserId}, NotificationId={NotificationId}",
                type, logDescription, r.ParentUserId, notification.Id);
        }

        if (unresolved != null)
            throw unresolved; // en az bir alıcı yazılamadı: error kuyruğu (diğerleri yazıldı, replay'de yalnız eksik yazılır).
    }

    public static string Child(INotificationTextFactory texts, CultureInfo culture, string? name)
    {
        var clean = CommentNotificationSupport.CleanName(name);
        return string.IsNullOrWhiteSpace(clean) ? texts.Resolve("notifications.common.defaultChild", culture) : clean;
    }

    public static string Teacher(INotificationTextFactory texts, CultureInfo culture, string? name)
        => string.IsNullOrWhiteSpace(name) ? texts.Resolve("notifications.common.defaultTeacher", culture) : name;
}
