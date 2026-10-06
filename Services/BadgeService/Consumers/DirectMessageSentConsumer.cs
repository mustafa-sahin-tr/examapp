using System.Text.Json;
using BadgeService.Hubs;
using BadgeService.Services;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;

namespace BadgeService.Consumers;

/// <summary>
/// Doğrudan mesaj bildirimi (issue #106 dilim b): exam API'nin mesajla aynı transaction'da yazdığı
/// <see cref="DirectMessageSentEvent"/>'i alır, mesajın KARŞI tarafına (öğrenci yazdıysa öğretmen, öğretmen yazdıysa öğrenci)
/// in-app <see cref="Entities.Notification"/> oluşturur ve SignalR (<c>DirectMessageReceived</c>, hedef = Keycloak sub) ile push eder.
///
/// Birleştirme (#305 deseni): aynı alıcının aynı konuşmadaki OKUNMAMIŞ bildirimi varsa yeni satır açılmaz; sayaç artar, metin
/// "N yeni mesaj" olur. <see cref="CommentNotificationCoalescer"/> yeniden kullanılır: <c>RootCommentId</c> = konuşma Id,
/// <c>LatestCommentId</c> = mesaj Id (yeni kolon/migration yok; <c>CommentTypes</c> dışında olduğundan gizleme nötrleyicisi bu
/// satırlara dokunmaz). Birleşik metin gönderen adını taşır (konuşmada tek karşı taraf var).
///
/// Idempotency: <see cref="NotificationEventLog"/> <c>(Type, EventId)</c> PK + <c>(Type, SourceEventId)</c> filtreli unique index;
/// aynı EventId ikinci kez gelirse no-op ve push yok (coalescer kontratı). Event başına tek alıcı.
///
/// Hata yolu: beklenmeyen hata fırlatılır -> MassTransit 3 kez immediate retry (bkz. <see cref="DirectMessageSentConsumerDefinition"/>)
/// -> hâlâ başarısızsa <c>badge-service_error</c> (dead-letter). Alıcı sub'ı ne event'te ne BadgeService verisinde varsa
/// <see cref="InvalidOperationException"/> -> retry -> dead-letter (sessiz kayıp yok; sub gelince error kuyruğundan oynatılır).
/// Duplicate hata değildir (loglanıp no-op). Mesaj gövdesi hiçbir yerde (bildirim, push, log) yer almaz.
/// </summary>
public class DirectMessageSentConsumer : IConsumer<DirectMessageSentEvent>
{
    public const string NotificationType = "DirectMessageReceived";

    private readonly BadgeDbContext _db;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly IUserLocaleResolver _localeResolver;
    private readonly INotificationTextFactory _texts;
    private readonly ILogger<DirectMessageSentConsumer> _logger;

    public DirectMessageSentConsumer(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        IUserLocaleResolver localeResolver,
        INotificationTextFactory texts,
        ILogger<DirectMessageSentConsumer> logger)
    {
        _db = db;
        _hub = hub;
        _localeResolver = localeResolver;
        _texts = texts;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<DirectMessageSentEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        if (await CommentNotificationCoalescer.IsProcessedAsync(_db, e.EventId, NotificationType, ct))
        {
            _logger.LogInformation(
                "DirectMessageSent zaten işlenmiş (EventId={EventId}, MessageId={MessageId}); atlanıyor.", e.EventId, e.MessageId);
            return;
        }

        var recipientSub = await NotificationRecipientResolver.ResolveSubAsync(
            _db, e.RecipientUserId, e.RecipientKeycloakId, $"Doğrudan mesaj bildirimi (EventId={e.EventId})", ct);
        var culture = await _localeResolver.ResolveAsync(e.RecipientUserId, recipientSub, ct);

        // Gönderen öğrenciyse alıcı öğretmendir (gelen kutusu /student-messages); öğretmense alıcı öğrencidir (/teacher-messages).
        var fromStudent = string.Equals(e.SenderRole, "Student", StringComparison.OrdinalIgnoreCase);
        var suffix = fromStudent ? "FromStudent" : "FromTeacher";
        var cleanName = CommentNotificationSupport.CleanName(e.SenderDisplayName);
        var senderName = string.IsNullOrWhiteSpace(cleanName)
            ? _texts.Resolve(fromStudent ? "notifications.common.defaultStudent" : "notifications.common.defaultTeacher", culture)
            : cleanName;

        var single = new LocalizedNotificationText(
            _texts.Resolve($"notifications.{NotificationType}.title{suffix}", culture, senderName),
            _texts.Resolve($"notifications.{NotificationType}.body{suffix}", culture, senderName));

        var data = JsonSerializer.Serialize(new
        {
            conversationId = e.ConversationId,
            messageId = e.MessageId,
            senderRole = fromStudent ? "Student" : "Teacher"
        });

        var result = await CommentNotificationCoalescer.WriteAsync(_db, new CommentNotificationRequest(
            e.EventId, NotificationType, e.RecipientUserId, recipientSub, WorksheetId: 0, QuestionId: null, QuestionOrder: null,
            CommentId: e.MessageId, RootCommentId: e.ConversationId,
            single,
            count => new LocalizedNotificationText(
                _texts.Resolve($"notifications.{NotificationType}.titleMany{suffix}", culture, senderName, count),
                _texts.Resolve($"notifications.{NotificationType}.bodyMany{suffix}", culture, senderName, count)),
            data), ct);
        if (result.Duplicate)
        {
            _logger.LogInformation(
                "DirectMessageSent eşzamanlı duplicate (EventId={EventId}, MessageId={MessageId}); atlanıyor.", e.EventId, e.MessageId);
            return;
        }

        var notification = result.Notification!;
        await _hub.Clients.User(recipientSub).SendAsync(NotificationType, new
        {
            notificationId = notification.Id,
            conversationId = e.ConversationId,
            messageId = e.MessageId,
            senderRole = fromStudent ? "Student" : "Teacher",
            title = notification.Title,
            body = notification.Body,
            coalescedCount = notification.CoalescedCount
        }, ct);

        _logger.LogInformation(
            "DirectMessageSent işlendi. EventId={EventId}, ConversationId={ConversationId}, MessageId={MessageId}, RecipientUserId={UserId}, NotificationId={NotificationId}, Coalesced={Coalesced}, Count={Count}",
            e.EventId, e.ConversationId, e.MessageId, e.RecipientUserId, notification.Id, result.Coalesced, notification.CoalescedCount);
    }
}
