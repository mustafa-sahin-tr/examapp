using BadgeService.Hubs;
using BadgeService.Services;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;

namespace BadgeService.Consumers;

/// <summary>
/// Yorum/soru thread'i bildirimi, öğretmen tarafı (issue #105 dilim 2): bir öğrenci yorum/soru yazdığında exam API'nin
/// yorumla aynı transaction'da yazdığı <see cref="WorksheetCommentCreatedEvent"/>'i alır, alıcı öğretmene in-app
/// <see cref="Notification"/> oluşturur ve SignalR (<c>WorksheetCommentCreated</c>) ile push eder.
///
/// Idempotency: <c>(Type, SourceEventId)</c> filtreli unique index (BadgeDbContext) + önceden AnyAsync kontrolü;
/// event başına tek alıcı olduğundan EventId tek başına yeterlidir. Eşzamanlı ikinci teslim unique ihlaliyle no-op.
///
/// Hata yolu: beklenmeyen hata fırlatılır → MassTransit üç kez immediate retry (bkz.
/// <see cref="WorksheetCommentCreatedConsumerDefinition"/>) → hâlâ başarısızsa mesaj <c>badge-service_error</c>
/// (dead-letter) kuyruğuna taşınır. Sessiz yutma yok. Duplicate hata değildir (loglanıp no-op). Event sub taşımıyorsa sub BadgeService verisinden çözülür; çözülemezse exception → retry → dead-letter (sessiz kayıp yok).
/// </summary>
public class WorksheetCommentCreatedConsumer : IConsumer<WorksheetCommentCreatedEvent>
{
    public const string NotificationType = "WorksheetCommentCreated";

    private readonly BadgeDbContext _db;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly IUserLocaleResolver _localeResolver;
    private readonly INotificationTextFactory _texts;
    private readonly ILogger<WorksheetCommentCreatedConsumer> _logger;

    public WorksheetCommentCreatedConsumer(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        IUserLocaleResolver localeResolver,
        INotificationTextFactory texts,
        ILogger<WorksheetCommentCreatedConsumer> logger)
    {
        _db = db;
        _hub = hub;
        _localeResolver = localeResolver;
        _texts = texts;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<WorksheetCommentCreatedEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        if (await CommentNotificationCoalescer.IsProcessedAsync(_db, e.EventId, NotificationType, ct))
        {
            _logger.LogInformation(
                "WorksheetCommentCreated zaten işlenmiş (EventId={EventId}, CommentId={CommentId}); atlanıyor.",
                e.EventId, e.CommentId);
            return;
        }

        var recipientSub = await CommentNotificationSupport.ResolveRecipientSubAsync(
            _db, e.RecipientUserId, e.RecipientKeycloakId, e.EventId, ct);
        var culture = await _localeResolver.ResolveAsync(e.RecipientUserId, recipientSub, ct);
        var cleanTitle = CommentNotificationSupport.CleanTitle(e.WorksheetTitle);
        var cleanAuthor = CommentNotificationSupport.CleanName(e.AuthorDisplayName);
        var worksheetTitle = string.IsNullOrWhiteSpace(cleanTitle)
            ? _texts.Resolve("notifications.common.unnamedWorksheet", culture)
            : cleanTitle;
        var authorName = string.IsNullOrWhiteSpace(cleanAuthor)
            ? _texts.Resolve("notifications.common.defaultStudent", culture)
            : cleanAuthor;
        var text = _texts.Build(NotificationType, culture, authorName, worksheetTitle);
        // issue #309: soru thread'inde sıra biliniyorsa "{n}. soru"; eski üreticiden (QuestionOrder yok) genel soru metni.
        var body = e.QuestionOrder is > 0
            ? _texts.Resolve($"notifications.{NotificationType}.bodyQuestionOrder", culture, authorName, worksheetTitle,
                e.QuestionOrder.Value)
            : e.QuestionId.HasValue
                ? _texts.Resolve($"notifications.{NotificationType}.bodyQuestion", culture, authorName, worksheetTitle)
                : text.Body;

        var result = await CommentNotificationCoalescer.WriteAsync(_db, new CommentNotificationRequest(
            e.EventId, NotificationType, e.RecipientUserId, recipientSub, e.WorksheetId, e.QuestionId, e.QuestionOrder,
            e.CommentId, e.RootCommentId,
            new LocalizedNotificationText(text.Title, body),
            count => CommentNotificationSupport.BuildMany(_texts, NotificationType, culture, worksheetTitle, count,
                e.QuestionOrder)), ct);
        if (result.Duplicate)
        {
            _logger.LogInformation(
                "WorksheetCommentCreated eşzamanlı duplicate (EventId={EventId}, CommentId={CommentId}); atlanıyor.",
                e.EventId, e.CommentId);
            return;
        }

        var notification = result.Notification!;
        // D4 (issue #326): gizleme event'i bu bildirimden önce işlendiyse (tombstone) metin nötrlenir; push de nötr metinle gider.
        if (await CommentHiddenNeutralizer.IsHiddenAsync(_db, e.CommentId, ct)
            && await CommentHiddenNeutralizer.NeutralizeAsync(_db, _localeResolver, _texts, e.CommentId, ct) > 0)
        {
            var neutral = _texts.Build(CommentHiddenNeutralizer.NeutralTextKey, culture);
            notification.Title = CommentNotificationSupport.CleanTitle(neutral.Title);
            notification.Body = CommentNotificationSupport.CleanBody(neutral.Body);
        }

        await _hub.Clients.User(recipientSub).SendAsync(NotificationType, new
        {
            notificationId = notification.Id,
            worksheetId = e.WorksheetId,
            questionId = e.QuestionId,
            questionOrder = e.QuestionOrder,
            commentId = e.CommentId,
            rootCommentId = e.RootCommentId,
            worksheetTitle,
            title = notification.Title,
            body = notification.Body,
            coalescedCount = notification.CoalescedCount
        }, ct);

        _logger.LogInformation(
            "WorksheetCommentCreated işlendi. EventId={EventId}, CommentId={CommentId}, RecipientUserId={UserId}, NotificationId={NotificationId}, Coalesced={Coalesced}, Count={Count}",
            e.EventId, e.CommentId, e.RecipientUserId, notification.Id, result.Coalesced, notification.CoalescedCount);
    }
}
