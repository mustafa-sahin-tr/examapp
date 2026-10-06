using System.Globalization;
using System.Text.Json;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Services;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Localization;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Consumers;

/// <summary>
/// Doğrudan mesaj şikayeti admin bildirimi (issue #106 dilim b): exam API'nin raporla aynı transaction'da yazdığı
/// <see cref="DirectMessageReportedEvent"/>'i alır, Admin'lere in-app <see cref="Notification"/> (rol bazlı: UserId=0,
/// UserKeycloakId=null — <see cref="TeacherApplicationSubmittedConsumer"/> ile aynı desen) oluşturur ve SignalR
/// <see cref="BadgeNotificationHub.AdminGroup"/> grubuna push eder. Şikayet notu / mesaj gövdesi / kimlikler taşınmaz.
///
/// Idempotency: <c>(Type, SourceEventId)</c> filtreli unique index + önceden AnyAsync kontrolü; eşzamanlı ikinci teslim
/// unique ihlaliyle no-op (push yok). Event başına tek bildirim (admin sayısından bağımsız; grup push'u).
///
/// Hata yolu: beklenmeyen hata fırlatılır -> 3 kez immediate retry (bkz. <see cref="DirectMessageReportedConsumerDefinition"/>)
/// -> <c>badge-service_error</c> (dead-letter). Sessiz yutma yok; duplicate hata değildir (loglanıp no-op).
/// Not: engellenen öğrencinin reddedilen mesajı hiç yazılmadığı için bu akışa girmez.
/// </summary>
public class DirectMessageReportedConsumer : IConsumer<DirectMessageReportedEvent>
{
    public const string NotificationType = "DirectMessageReported";

    private readonly BadgeDbContext _db;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly INotificationTextFactory _texts;
    private readonly ILogger<DirectMessageReportedConsumer> _logger;

    public DirectMessageReportedConsumer(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        INotificationTextFactory texts,
        ILogger<DirectMessageReportedConsumer> logger)
    {
        _db = db;
        _hub = hub;
        _texts = texts;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<DirectMessageReportedEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        if (await _db.Notifications.AnyAsync(n => n.Type == NotificationType && n.SourceEventId == e.EventId, ct))
        {
            _logger.LogInformation(
                "DirectMessageReported zaten işlenmiş (EventId={EventId}, ReportId={ReportId}); atlanıyor.", e.EventId, e.ReportId);
            return;
        }

        // Admin hedef dili bilinmiyor: platform varsayılan dili (issue #185 karar #5, diğer admin bildirimleriyle aynı).
        var culture = CultureInfo.GetCultureInfo(SupportedLocales.DefaultCultureName);
        var reporterIsStudent = string.Equals(e.ReporterRole, "Student", StringComparison.OrdinalIgnoreCase);
        var title = _texts.Resolve($"notifications.{NotificationType}.title", culture);
        var body = _texts.Resolve($"notifications.{NotificationType}.body{(reporterIsStudent ? "Student" : "Teacher")}", culture);

        var notification = new Notification
        {
            UserId = 0,
            UserKeycloakId = null,
            Type = NotificationType,
            Title = CommentNotificationSupport.CleanTitle(title),
            Body = CommentNotificationSupport.CleanBody(body),
            Data = JsonSerializer.Serialize(new
            {
                reportId = e.ReportId,
                conversationId = e.ConversationId,
                reporterRole = reporterIsStudent ? "Student" : "Teacher"
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
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            _logger.LogInformation(
                "DirectMessageReported eşzamanlı duplicate (EventId={EventId}, ReportId={ReportId}); atlanıyor.", e.EventId, e.ReportId);
            return;
        }

        await _hub.Clients.Group(BadgeNotificationHub.AdminGroup).SendAsync(NotificationType, new
        {
            notificationId = notification.Id,
            reportId = e.ReportId,
            conversationId = e.ConversationId,
            reporterRole = reporterIsStudent ? "Student" : "Teacher",
            title = notification.Title,
            body = notification.Body
        }, ct);

        _logger.LogInformation(
            "DirectMessageReported işlendi. EventId={EventId}, ReportId={ReportId}, ConversationId={ConversationId}, NotificationId={NotificationId}",
            e.EventId, e.ReportId, e.ConversationId, notification.Id);
    }
}
