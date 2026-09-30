using ExamApp.Foundation.Security;

namespace BadgeService.Consumers;

/// <summary>
/// issue #105 dilim 2 — yorum bildirim consumer'larının ortak yardımcıları: alıcı sub'ı çözümü ve metin temizliği.
/// </summary>
internal static class CommentNotificationSupport
{
    public const int MaxTitleLength = 200;
    public const int MaxBodyLength = 500;
    public const int MaxNameLength = 100;

    /// <summary>Bkz. <see cref="NotificationRecipientResolver"/> (#298'de randevu consumer'larıyla ortaklaştırıldı).</summary>
    public static Task<string> ResolveRecipientSubAsync(
        BadgeDbContext db, int userId, string? sub, Guid eventId, CancellationToken ct)
        => NotificationRecipientResolver.ResolveSubAsync(db, userId, sub, $"Yorum bildirimi (EventId={eventId})", ct);

    public static string CleanName(string? value) => DisplayTextSanitizer.Clean(value, MaxNameLength);

    public static string CleanTitle(string? value) => DisplayTextSanitizer.Clean(value, MaxTitleLength);

    public static string CleanBody(string? value) => DisplayTextSanitizer.Clean(value, MaxBodyLength);
}
