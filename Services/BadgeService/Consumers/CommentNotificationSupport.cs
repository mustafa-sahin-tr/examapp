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

    /// <summary>
    /// issue #305: birleştirilmiş bildirim (N&gt;1) metni — "N yeni yorum/cevap: {worksheet}". Birden fazla yazar olabileceğinden
    /// (ve D4: gizlenen yorumun adı kalmasın diye) yazar adı içermez. Anahtarlar <c>titleMany</c>/<c>bodyMany</c>/
    /// <c>bodyManyQuestionOrder</c>; argümanlar: {0} boş, {1} worksheet, {2} sayı, {3} soru sırası.
    /// </summary>
    public static Services.LocalizedNotificationText BuildMany(
        Services.INotificationTextFactory texts, string type, System.Globalization.CultureInfo culture,
        string worksheetTitle, int count, int? questionOrder)
    {
        var title = texts.Resolve($"notifications.{type}.titleMany", culture, string.Empty, worksheetTitle, count);
        var body = questionOrder is > 0
            ? texts.Resolve($"notifications.{type}.bodyManyQuestionOrder", culture, string.Empty, worksheetTitle, count, questionOrder.Value)
            : texts.Resolve($"notifications.{type}.bodyMany", culture, string.Empty, worksheetTitle, count);
        return new Services.LocalizedNotificationText(title, body);
    }

    public static string CleanName(string? value) => DisplayTextSanitizer.Clean(value, MaxNameLength);

    public static string CleanTitle(string? value) => DisplayTextSanitizer.Clean(value, MaxTitleLength);

    public static string CleanBody(string? value) => DisplayTextSanitizer.Clean(value, MaxBodyLength);
}
