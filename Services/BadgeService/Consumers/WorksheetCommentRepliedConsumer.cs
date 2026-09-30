using BadgeService.Hubs;
using BadgeService.Services;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;

namespace BadgeService.Consumers;

/// <summary>
/// Yorum/soru thread'i bildirimi, öğrenci tarafı (issue #105 dilim 2): bir thread'e reply yazıldığında exam API'nin
/// yorumla aynı transaction'da yazdığı <see cref="WorksheetCommentRepliedEvent"/>'i alır, kök yorumun öğrenci
/// yazarına in-app <see cref="Notification"/> oluşturur ve SignalR (<c>WorksheetCommentReplied</c>) ile push eder.
/// Reply'ı öğretmen yazdıysa metin "Öğretmenin cevap verdi", başka öğrenci yazdıysa "{Ad S.} yorumuna cevap yazdı".
///
/// Idempotency ve hata yolu <see cref="WorksheetCommentCreatedConsumer"/> ile aynıdır: <c>(Type, SourceEventId)</c>
/// unique index + AnyAsync kontrolü; beklenmeyen hata ya da çözülemeyen alıcı sub'ı → 3 immediate retry → <c>badge-service_error</c> (dead-letter).
/// </summary>
public class WorksheetCommentRepliedConsumer : IConsumer<WorksheetCommentRepliedEvent>
{
    public const string NotificationType = "WorksheetCommentReplied";

    /// <summary>Metin anahtarı — başka öğrencinin reply'ı için (Notification.Type yine <see cref="NotificationType"/>).</summary>
    private const string StudentAuthorTextKey = "WorksheetCommentRepliedByStudent";

    private readonly BadgeDbContext _db;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly IUserLocaleResolver _localeResolver;
    private readonly INotificationTextFactory _texts;
    private readonly ILogger<WorksheetCommentRepliedConsumer> _logger;

    public WorksheetCommentRepliedConsumer(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        IUserLocaleResolver localeResolver,
        INotificationTextFactory texts,
        ILogger<WorksheetCommentRepliedConsumer> logger)
    {
        _db = db;
        _hub = hub;
        _localeResolver = localeResolver;
        _texts = texts;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<WorksheetCommentRepliedEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        if (await CommentNotificationCoalescer.IsProcessedAsync(_db, e.EventId, NotificationType, ct))
        {
            _logger.LogInformation(
                "WorksheetCommentReplied zaten işlenmiş (EventId={EventId}, CommentId={CommentId}); atlanıyor.",
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

        string textKey;
        string authorName;
        if (string.Equals(e.AuthorRole, "Teacher", StringComparison.OrdinalIgnoreCase))
        {
            textKey = NotificationType;
            authorName = string.Empty;
        }
        else
        {
            textKey = StudentAuthorTextKey;
            authorName = string.IsNullOrWhiteSpace(cleanAuthor)
                ? _texts.Resolve("notifications.common.defaultStudent", culture)
                : cleanAuthor;
        }

        var text = _texts.Build(textKey, culture, authorName, worksheetTitle);

        // issue #309: soru thread'inde sıra biliniyorsa gövde "{n}. soru hakkındaki" der; yoksa genel metin.
        var body = e.QuestionOrder is > 0
            ? _texts.Resolve($"notifications.{textKey}.bodyQuestionOrder", culture, authorName, worksheetTitle,
                e.QuestionOrder.Value)
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
                "WorksheetCommentReplied eşzamanlı duplicate (EventId={EventId}, CommentId={CommentId}); atlanıyor.",
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
            "WorksheetCommentReplied işlendi. EventId={EventId}, CommentId={CommentId}, RecipientUserId={UserId}, NotificationId={NotificationId}, Coalesced={Coalesced}, Count={Count}",
            e.EventId, e.CommentId, e.RecipientUserId, notification.Id, result.Coalesced, notification.CoalescedCount);
    }
}
