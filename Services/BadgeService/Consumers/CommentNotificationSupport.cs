using ExamApp.Foundation.Security;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Consumers;

/// <summary>
/// issue #105 dilim 2 — yorum bildirim consumer'larının ortak yardımcıları: alıcı sub'ı çözümü ve metin temizliği.
/// </summary>
internal static class CommentNotificationSupport
{
    public const int MaxTitleLength = 200;
    public const int MaxBodyLength = 500;
    public const int MaxNameLength = 100;

    /// <summary>
    /// Event sub taşıyorsa onu kullanır. Taşımıyorsa (exam DB'de öğretmenin sub'ı yoktur; sync auth-api çağrısı yasak)
    /// BadgeService'in kendi verisinden çözer: önce dil tercihi kaydı, sonra bu kullanıcıya daha önce yazılmış bildirim.
    /// Çözülemezse <see cref="InvalidOperationException"/> fırlatır → MassTransit retry, sonra <c>badge-service_error</c>
    /// (sessiz kayıp yok; sub gelince mesaj error kuyruğundan yeniden oynatılabilir).
    /// </summary>
    public static async Task<string> ResolveRecipientSubAsync(
        BadgeDbContext db, int userId, string? sub, Guid eventId, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(sub))
            return sub;

        if (userId > 0)
        {
            var fromLocale = await db.UserLocalePreferences.AsNoTracking()
                .Where(p => p.UserId == userId && p.KeycloakId != null && p.KeycloakId != "")
                .Select(p => p.KeycloakId)
                .FirstOrDefaultAsync(ct);
            if (!string.IsNullOrWhiteSpace(fromLocale))
                return fromLocale;

            var fromNotification = await db.Notifications.AsNoTracking()
                .Where(n => n.UserId == userId && n.UserKeycloakId != null && n.UserKeycloakId != "")
                .OrderByDescending(n => n.Id)
                .Select(n => n.UserKeycloakId)
                .FirstOrDefaultAsync(ct);
            if (!string.IsNullOrWhiteSpace(fromNotification))
                return fromNotification;
        }

        throw new InvalidOperationException(
            $"Yorum bildirimi alıcısının Keycloak sub'ı çözülemedi (EventId={eventId}, RecipientUserId={userId}).");
    }

    public static string CleanName(string? value) => DisplayTextSanitizer.Clean(value, MaxNameLength);

    public static string CleanTitle(string? value) => DisplayTextSanitizer.Clean(value, MaxTitleLength);

    public static string CleanBody(string? value) => DisplayTextSanitizer.Clean(value, MaxBodyLength);
}
