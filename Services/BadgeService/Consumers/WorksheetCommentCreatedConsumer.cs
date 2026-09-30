using System.Text.Json;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Services;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

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

        var exists = await _db.Notifications
            .AnyAsync(n => n.Type == NotificationType && n.SourceEventId == e.EventId, ct);
        if (exists)
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
        var body = e.QuestionId.HasValue
            ? _texts.Resolve($"notifications.{NotificationType}.bodyQuestion", culture, authorName, worksheetTitle)
            : text.Body;

        var notification = new Notification
        {
            UserId = e.RecipientUserId,
            UserKeycloakId = recipientSub,
            Type = NotificationType,
            Title = CommentNotificationSupport.CleanTitle(text.Title),
            Body = CommentNotificationSupport.CleanBody(body),
            Data = JsonSerializer.Serialize(new
            {
                worksheetId = e.WorksheetId,
                questionId = e.QuestionId,
                commentId = e.CommentId,
                rootCommentId = e.RootCommentId
            }),
            SourceEventId = e.EventId,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        _db.Notifications.Add(notification);

        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            _logger.LogInformation(
                "WorksheetCommentCreated eşzamanlı duplicate (EventId={EventId}, CommentId={CommentId}); atlanıyor.",
                e.EventId, e.CommentId);
            return;
        }

        await _hub.Clients.User(recipientSub).SendAsync(NotificationType, new
        {
            notificationId = notification.Id,
            worksheetId = e.WorksheetId,
            questionId = e.QuestionId,
            commentId = e.CommentId,
            rootCommentId = e.RootCommentId,
            worksheetTitle,
            title = notification.Title,
            body = notification.Body
        }, ct);

        _logger.LogInformation(
            "WorksheetCommentCreated işlendi. EventId={EventId}, CommentId={CommentId}, RecipientUserId={UserId}, NotificationId={NotificationId}",
            e.EventId, e.CommentId, e.RecipientUserId, notification.Id);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };
}
