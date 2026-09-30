using Microsoft.EntityFrameworkCore;

namespace BadgeService.Consumers;

/// <summary>
/// Bildirim alıcısının Keycloak sub'ı çözümü — yorum (#105) ve randevu (#298) consumer'larının ortak yardımcısı.
/// Event sub taşıyorsa onu kullanır. Taşımıyorsa (üretici auth-api'ye erişemedi; consumer'dan sync auth-api çağrısı yasak)
/// BadgeService'in kendi verisinden çözer: önce dil tercihi kaydı (<c>UserLocalePreference</c>), sonra bu kullanıcıya daha
/// önce yazılmış bildirim. Çözülemezse <see cref="InvalidOperationException"/> fırlatır → MassTransit retry, sonra
/// <c>badge-service_error</c> (sessiz kayıp yok; sub gelince mesaj error kuyruğundan yeniden oynatılabilir).
/// </summary>
internal static class NotificationRecipientResolver
{
    public static async Task<string> ResolveSubAsync(
        BadgeDbContext db, int userId, string? sub, string eventDescription, CancellationToken ct)
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
            $"{eventDescription}: bildirim alıcısının Keycloak sub'ı çözülemedi (RecipientUserId={userId}).");
    }
}
