using System.Text.Json;
using BadgeService.Entities;
using BadgeService.Services;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Consumers;

/// <summary>Bir yorum bildirimi event'inin (Created/Replied) yazılması için tüm girdiler.</summary>
internal sealed record CommentNotificationRequest(
    Guid EventId,
    string Type,
    int UserId,
    string RecipientSub,
    int WorksheetId,
    int? QuestionId,
    int? QuestionOrder,
    int CommentId,
    int RootCommentId,
    /// <summary>Tek yorum için metin (N=1; yazar adı içerir).</summary>
    LocalizedNotificationText Single,
    /// <summary>N&gt;1 için metin ("N yeni yorum/cevap"; yazar adı YOK).</summary>
    Func<int, LocalizedNotificationText> Many);

internal sealed record CommentNotificationResult(Notification? Notification, bool Duplicate, bool Coalesced)
{
    public static CommentNotificationResult AlreadyProcessed { get; } = new(null, true, false);
}

/// <summary>
/// issue #305 (dilim B) — (alıcı, Type, RootCommentId) bazında bildirim birleştirme.
///
/// Akış (tek transaction): okunmamış satır varsa (aynı UserId + UserKeycloakId + Type + RootCommentId) → atomik
/// <c>CoalescedCount = CoalescedCount + 1</c>, <c>CreatedAt = now</c>, <c>Data</c> (en son yorum) güncellenir, başlık/gövde
/// "N yeni ..." olur; yoksa (ya da satır bu arada okunduysa) yeni satır açılır. Aynı transaction'da
/// <see cref="NotificationEventLog"/> ((Type, EventId) PK) eklenir.
///
/// Idempotency: (1) başta event log'da ya da <c>(Type, SourceEventId)</c> ile Notification'da varsa no-op; (2) yarış:
/// log ve Notification insert'leri <c>INSERT ... ON CONFLICT DO NOTHING</c> ile yapılır — çakışma (0 satır) exception
/// FIRLATMAZ (EF'in "fail" logları basılmaz); aynı EventId'nin eşzamanlı ikinci teslimi log çakışmasıyla transaction geri
/// alınır (sayaç artışı dahil) → no-op. Eşzamanlı FARKLI event'ler: sayaç artışı tek SQL ifadesi (satır kilidi) olduğundan
/// kayıp olmaz; ilk-yorum yarışı (iki event de satır bulamayıp insert eder) filtreli unique index'e
/// (UserId, UserKeycloakId, Type, RootCommentId, okunmamış) takılır → 0 satır → yeniden dener ve birleştirir. Metin güncellemesi
/// yalnız sayacı gördüğü değerde yazar (<c>WHERE CoalescedCount = @count</c>) — son artıran her zaman son metni yazar.
/// </summary>
internal static class CommentNotificationCoalescer
{
    public const int MaxAttempts = 3;

    private enum Step { Done, Conflict, Duplicate }

    public static async Task<CommentNotificationResult> WriteAsync(
        BadgeDbContext db, CommentNotificationRequest r, CancellationToken ct)
    {
        for (var attempt = 1; ; attempt++)
        {
            if (await IsProcessedAsync(db, r.EventId, r.Type, ct))
                return CommentNotificationResult.AlreadyProcessed;

            db.ChangeTracker.Clear();
            await using var tx = await db.Database.BeginTransactionAsync(ct);

            var (step, notification, merged) = await TryMergeAsync(db, r, ct);
            if (step == Step.Done && notification == null)
                (step, notification) = await InsertAsync(db, r, ct);

            if (step == Step.Done)
            {
                await tx.CommitAsync(ct);
                return new CommentNotificationResult(notification, false, merged);
            }

            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();

            // Aynı event başka teslimde işlendi mi (log PK ya da (Type, SourceEventId))? Öyleyse no-op.
            if (step == Step.Duplicate || await IsProcessedAsync(db, r.EventId, r.Type, ct))
                return CommentNotificationResult.AlreadyProcessed;

            // Aksi: ilk-yorum yarışı (okunmamış slot dolu) → yeniden dene, bu sefer birleştirir; tükenirse retry/dead-letter'a devret.
            if (attempt >= MaxAttempts)
                throw new InvalidOperationException(
                    $"Yorum bildirimi yazılamadı: okunmamış slot çakışması {MaxAttempts} denemede çözülmedi (EventId={r.EventId}).");
        }
    }

    internal static async Task<bool> IsProcessedAsync(BadgeDbContext db, Guid eventId, string type, CancellationToken ct) =>
        await db.NotificationEventLogs.AsNoTracking().AnyAsync(l => l.Type == type && l.EventId == eventId, ct)
        || await db.Notifications.AsNoTracking().AnyAsync(n => n.Type == type && n.SourceEventId == eventId, ct);

    private static async Task<(Step Step, Notification? Notification, bool Merged)> TryMergeAsync(
        BadgeDbContext db, CommentNotificationRequest r, CancellationToken ct)
    {
        var id = await db.Notifications.AsNoTracking()
            .Where(n => n.UserId == r.UserId && n.UserKeycloakId == r.RecipientSub && n.Type == r.Type
                && n.RootCommentId == r.RootCommentId && !n.IsRead)
            .Select(n => n.Id)
            .FirstOrDefaultAsync(ct);
        if (id == 0)
            return (Step.Done, null, false);

        var now = DateTime.UtcNow;
        var data = BuildData(r);

        // Tek SQL ifadesi: sayaç artışı satır kilidiyle atomik; satır bu arada okunduysa 0 satır etkilenir → yeni satır.
        var updated = await db.Notifications
            .Where(n => n.Id == id && !n.IsRead)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.CoalescedCount, n => n.CoalescedCount + 1)
                .SetProperty(n => n.CreatedAt, now)
                .SetProperty(n => n.Data, data)
                .SetProperty(n => n.LatestCommentId, r.CommentId), ct);
        if (updated == 0)
            return (Step.Done, null, false);

        var count = await db.Notifications.Where(n => n.Id == id).Select(n => n.CoalescedCount).SingleAsync(ct);
        var text = r.Many(count);
        var title = CommentNotificationSupport.CleanTitle(text.Title);
        var body = CommentNotificationSupport.CleanBody(text.Body);
        await db.Notifications
            .Where(n => n.Id == id && n.CoalescedCount == count)
            .ExecuteUpdateAsync(s => s
                .SetProperty(n => n.Title, title)
                .SetProperty(n => n.Body, body), ct);

        if (!await TryLogAsync(db, r, id, now, ct))
            return (Step.Duplicate, null, false);

        return (Step.Done, await db.Notifications.AsNoTracking().SingleAsync(n => n.Id == id, ct), true);
    }

    private static async Task<(Step Step, Notification? Notification)> InsertAsync(
        BadgeDbContext db, CommentNotificationRequest r, CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var title = CommentNotificationSupport.CleanTitle(r.Single.Title);
        var body = CommentNotificationSupport.CleanBody(r.Single.Body);
        var data = BuildData(r);

        // ON CONFLICT DO NOTHING: (Type, SourceEventId) ya da okunmamış-slot unique index çakışması exception fırlatmaz.
        var inserted = await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO ""Notifications""
                (""UserId"", ""UserKeycloakId"", ""Type"", ""Title"", ""Body"", ""Data"", ""SourceEventId"", ""RootCommentId"",
                 ""LatestCommentId"", ""CoalescedCount"", ""IsRead"", ""CreatedAt"")
            VALUES
                ({r.UserId}, {r.RecipientSub}, {r.Type}, {title}, {body}, {data}, {r.EventId}, {r.RootCommentId},
                 {r.CommentId}, {1}, {false}, {now})
            ON CONFLICT DO NOTHING", ct);
        if (inserted == 0)
            return (Step.Conflict, null);

        var notification = await db.Notifications.AsNoTracking()
            .SingleAsync(n => n.Type == r.Type && n.SourceEventId == r.EventId, ct);

        if (!await TryLogAsync(db, r, notification.Id, now, ct))
            return (Step.Duplicate, null);

        return (Step.Done, notification);
    }

    /// <summary>Event log satırını ekler; (Type, EventId) zaten varsa false (aynı event başka teslimde işlendi).</summary>
    private static async Task<bool> TryLogAsync(
        BadgeDbContext db, CommentNotificationRequest r, int notificationId, DateTime now, CancellationToken ct) =>
        await db.Database.ExecuteSqlInterpolatedAsync($@"
            INSERT INTO ""NotificationEventLogs"" (""Type"", ""EventId"", ""NotificationId"", ""ProcessedAt"")
            VALUES ({r.Type}, {r.EventId}, {notificationId}, {now})
            ON CONFLICT DO NOTHING", ct) > 0;

    private static string BuildData(CommentNotificationRequest r) => JsonSerializer.Serialize(new
    {
        worksheetId = r.WorksheetId,
        questionId = r.QuestionId,
        questionOrder = r.QuestionOrder,
        commentId = r.CommentId,
        rootCommentId = r.RootCommentId
    });
}
