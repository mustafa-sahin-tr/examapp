using BadgeService.Entities;
using BadgeService.Services;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Consumers;

/// <summary>
/// issue #326 (D4): bir yorum gizlendiğinde (exam API <see cref="WorksheetCommentHiddenEvent"/>'i gizlemeyle aynı
/// transaction'da yazar) o yoruma işaret eden bildirimlerin (<c>Notification.LatestCommentId</c>; dilimden önceki satırlarda
/// <c>Data.commentId</c> eşleşmesi) başlık/gövdesi nötr metne çevrilir — yazar adı (reşit olmayan öğrenci adı olabilir)
/// bildirimde kalmaz. OKUNMUŞ olanlar dahil hepsi güncellenir. Birden fazla yorumu birleştiren satır (CoalescedCount &gt; 1)
/// zaten yazar adı içermeyen "N yeni yorum/cevap" metnindedir, dokunulmaz. Unhide'da metin geri getirilmez.
///
/// Idempotency: doğal — tombstone ON CONFLICT DO NOTHING, aynı nötr metin tekrar yazılır; sayaç/yan etki yok.
/// Hata yolu: beklenmeyen hata → 3 immediate retry → <c>badge-service_error</c> (dead-letter); sessiz yutma yok.
/// Sıra bozulması: gizleme event'i bildirimden ÖNCE işlenirse <c>HiddenCommentTombstone</c> kalır; Created/Replied consumer'ları
/// yazdıktan sonra tombstone'a bakıp metni nötrler (bildirim atlanmaz, nötr metinle yazılır).
/// </summary>
public class WorksheetCommentHiddenConsumer : IConsumer<WorksheetCommentHiddenEvent>
{
    private readonly BadgeDbContext _db;
    private readonly IUserLocaleResolver _localeResolver;
    private readonly INotificationTextFactory _texts;
    private readonly ILogger<WorksheetCommentHiddenConsumer> _logger;

    public WorksheetCommentHiddenConsumer(
        BadgeDbContext db,
        IUserLocaleResolver localeResolver,
        INotificationTextFactory texts,
        ILogger<WorksheetCommentHiddenConsumer> logger)
    {
        _db = db;
        _localeResolver = localeResolver;
        _texts = texts;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<WorksheetCommentHiddenEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        // Önce tombstone: bildirim bu event'ten SONRA yazılırsa Created/Replied consumer'ı işareti görüp nötrler (sıra bozulması).
        await CommentHiddenNeutralizer.WriteTombstoneAsync(_db, e.CommentId, ct);
        var neutralised = await CommentHiddenNeutralizer.NeutralizeAsync(_db, _localeResolver, _texts, e.CommentId, ct);

        _logger.LogInformation(
            "WorksheetCommentHidden işlendi. EventId={EventId}, CommentId={CommentId}, WorksheetId={WorksheetId}, NötrleşenBildirim={Count}",
            e.EventId, e.CommentId, e.WorksheetId, neutralised);
    }
}
