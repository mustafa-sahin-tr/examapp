using System.Text.Json;
using BadgeService.Entities;
using BadgeService.Hubs;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Consumers;

/// <summary>
/// "Planla &amp; Hatırlat" akışının tüketici ucu: reminder zamanı geldiğinde exam API'nin
/// yazdığı <see cref="WorksheetReminderDueEvent"/>'i alır, in-app <see cref="Notification"/>
/// satırı oluşturur ve SignalR ile öğrenciye push eder.
///
/// Hata yolu: beklenmeyen hata fırlatılır → MassTransit üç kez immediate retry → hâlâ
/// başarısızsa mesaj <c>badge-service_error</c> (dead-letter) kuyruğuna taşınır. Sessiz yutma yok.
/// Duplicate (aynı mesajın tekrar teslimi) hata değildir; loglanıp no-op ile geçilir.
/// </summary>
public class WorksheetReminderDueConsumer : IConsumer<WorksheetReminderDueEvent>
{
    public const string NotificationType = "WorksheetReminderDue";

    private readonly BadgeDbContext _db;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly ILogger<WorksheetReminderDueConsumer> _logger;

    public WorksheetReminderDueConsumer(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        ILogger<WorksheetReminderDueConsumer> logger)
    {
        _db = db;
        _hub = hub;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<WorksheetReminderDueEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        // Idempotency: bu reminder için bildirim zaten üretildiyse çık.
        var exists = await _db.Notifications
            .AnyAsync(n => n.Type == NotificationType && n.SourceReminderId == e.ReminderId, ct);
        if (exists)
        {
            _logger.LogInformation(
                "WorksheetReminderDue zaten işlenmiş (ReminderId={ReminderId}); atlanıyor.", e.ReminderId);
            return;
        }

        var worksheetName = string.IsNullOrWhiteSpace(e.WorksheetName) ? "Sınavın" : e.WorksheetName;

        var notification = new Notification
        {
            UserId = e.UserId,
            UserKeycloakId = string.IsNullOrWhiteSpace(e.UserKeycloakId) ? null : e.UserKeycloakId,
            Type = NotificationType,
            Title = "Sınavın yaklaşıyor",
            Body = $"{worksheetName} {e.RemindBeforeMinutes} dakika sonra başlıyor.",
            Data = JsonSerializer.Serialize(new
            {
                reminderId = e.ReminderId,
                worksheetId = e.WorksheetId,
                scheduledFor = e.ScheduledFor
            }),
            SourceReminderId = e.ReminderId,
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
            // Eşzamanlı ikinci teslim unique index'e takıldı — idempotent no-op.
            _logger.LogInformation(
                "WorksheetReminderDue eşzamanlı duplicate (ReminderId={ReminderId}); atlanıyor.", e.ReminderId);
            return;
        }

        // SignalR: badge akışıyla aynı hedefleme — Clients.User(keycloak subject).
        if (!string.IsNullOrWhiteSpace(e.UserKeycloakId))
        {
            await _hub.Clients.User(e.UserKeycloakId).SendAsync("ReminderDue", new
            {
                notificationId = notification.Id,
                worksheetId = e.WorksheetId,
                worksheetName = worksheetName,
                scheduledFor = e.ScheduledFor,
                title = notification.Title,
                body = notification.Body
            }, ct);
        }
        else
        {
            _logger.LogWarning(
                "WorksheetReminderDue: UserKeycloakId boş (ReminderId={ReminderId}); bildirim kaydedildi ama push atlanamadı.",
                e.ReminderId);
        }

        _logger.LogInformation(
            "WorksheetReminderDue işlendi. ReminderId={ReminderId}, UserId={UserId}, NotificationId={NotificationId}",
            e.ReminderId, e.UserId, notification.Id);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };
}
