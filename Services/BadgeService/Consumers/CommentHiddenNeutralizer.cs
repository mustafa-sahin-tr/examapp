using BadgeService.Entities;
using BadgeService.Services;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Consumers;

/// <summary>
/// issue #326 (D4): gizlenen yorumun bildirim metnini nötr metne çevirir. Hem <see cref="WorksheetCommentHiddenConsumer"/>
/// hem (tombstone sırası bozulması için) Created/Replied consumer'larının yazma-sonrası kontrolü kullanır.
///
/// Güncelleme KOŞULLU tek SQL ifadesidir: <c>WHERE Id = @id AND CoalescedCount = 1 AND (LatestCommentId = @c OR LatestCommentId IS NULL)</c>.
/// Birleştirme ile yarışta satır bu arada "N yeni" (adsız) metne geçtiyse ya da başka yoruma işaret ediyorsa ezilmez.
/// Okunmuş ve okunmamış tüm satırlar güncellenir. Unhide'da metin geri getirilmez.
/// </summary>
internal static class CommentHiddenNeutralizer
{
    public const string NeutralTextKey = "WorksheetCommentHidden";

    private static readonly string[] CommentTypes =
        { WorksheetCommentCreatedConsumer.NotificationType, WorksheetCommentRepliedConsumer.NotificationType };

    public static async Task<bool> IsHiddenAsync(BadgeDbContext db, int commentId, CancellationToken ct) =>
        await db.HiddenCommentTombstones.AsNoTracking().AnyAsync(t => t.CommentId == commentId, ct);

    /// <summary>Tombstone'u yazar; zaten varsa no-op (ON CONFLICT DO NOTHING — eşzamanlı teslimde exception yok).</summary>
    public static Task<int> WriteTombstoneAsync(BadgeDbContext db, int commentId, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        return db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO ""HiddenCommentTombstones"" (""CommentId"", ""HiddenAt"") VALUES ({commentId}, {now})
            ON CONFLICT DO NOTHING", ct);
    }

    /// <summary>Yorumu işaret eden (tek yorumlu) bildirimleri nötrler; güncellenen satır sayısını döner.</summary>
    public static async Task<int> NeutralizeAsync(
        BadgeDbContext db, IUserLocaleResolver localeResolver, INotificationTextFactory texts, int commentId, CancellationToken ct)
    {
        // Dilim öncesi satırlar için Data JSON eşleşmesi (LatestCommentId null). Serileştirme sırası sabit: commentId, rootCommentId.
        var legacyPattern = $"%\"commentId\":{commentId},\"rootCommentId\":%";
        var candidates = await db.Notifications.AsNoTracking()
            .Where(n => CommentTypes.Contains(n.Type) && n.CoalescedCount == 1
                && (n.LatestCommentId == commentId
                    || (n.LatestCommentId == null && n.Data != null && EF.Functions.Like(n.Data, legacyPattern))))
            .Select(n => new { n.Id, n.UserId, n.UserKeycloakId })
            .ToListAsync(ct);

        var updated = 0;
        foreach (var n in candidates)
        {
            var culture = await localeResolver.ResolveAsync(n.UserId, n.UserKeycloakId, ct);
            var text = texts.Build(NeutralTextKey, culture);
            var title = CommentNotificationSupport.CleanTitle(text.Title);
            var body = CommentNotificationSupport.CleanBody(text.Body);

            updated += await db.Notifications
                .Where(x => x.Id == n.Id && x.CoalescedCount == 1 && (x.LatestCommentId == commentId || x.LatestCommentId == null))
                .ExecuteUpdateAsync(s => s
                    .SetProperty(x => x.Title, title)
                    .SetProperty(x => x.Body, body), ct);
        }

        return updated;
    }
}
