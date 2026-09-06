using System.Text.Json;
using BadgeService.Entities;
using BadgeService.Hubs;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Consumers;

/// <summary>
/// Atama izni akışının tüketici ucu (talep tarafı): bir öğretmen bir sınav için atama izni
/// talep ettiğinde exam API'nin yazdığı <see cref="WorksheetAccessRequestedEvent"/>'i alır,
/// sınavın sahibine in-app <see cref="Notification"/> satırı oluşturur ve SignalR ile push eder.
///
/// Hata yolu: beklenmeyen hata fırlatılır → MassTransit üç kez immediate retry → hâlâ
/// başarısızsa mesaj <c>badge-service_error</c> (dead-letter) kuyruğuna taşınır. Sessiz yutma yok.
/// Duplicate (aynı mesajın tekrar teslimi) hata değildir; loglanıp no-op ile geçilir.
/// </summary>
public class WorksheetAccessRequestedConsumer : IConsumer<WorksheetAccessRequestedEvent>
{
    public const string NotificationType = "WorksheetAccessRequested";

    private readonly BadgeDbContext _db;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly ILogger<WorksheetAccessRequestedConsumer> _logger;

    public WorksheetAccessRequestedConsumer(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        ILogger<WorksheetAccessRequestedConsumer> logger)
    {
        _db = db;
        _hub = hub;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<WorksheetAccessRequestedEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        // Idempotency: bu talep için sahibe bildirim zaten üretildiyse çık.
        var exists = await _db.Notifications
            .AnyAsync(n => n.Type == NotificationType && n.SourceAccessRequestId == e.RequestId, ct);
        if (exists)
        {
            _logger.LogInformation(
                "WorksheetAccessRequested zaten işlenmiş (RequestId={RequestId}); atlanıyor.", e.RequestId);
            return;
        }

        var worksheetName = string.IsNullOrWhiteSpace(e.WorksheetName) ? "bir sınav" : e.WorksheetName;
        var requesterName = string.IsNullOrWhiteSpace(e.RequesterName) ? "Bir öğretmen" : e.RequesterName;

        var notification = new Notification
        {
            UserId = e.OwnerUserId,
            UserKeycloakId = string.IsNullOrWhiteSpace(e.TargetKeycloakId) ? null : e.TargetKeycloakId,
            Type = NotificationType,
            Title = "Yeni atama izni talebi",
            Body = $"{requesterName}, \"{worksheetName}\" sınavı için atama izni istiyor.",
            Data = JsonSerializer.Serialize(new
            {
                requestId = e.RequestId,
                worksheetId = e.WorksheetId
            }),
            SourceAccessRequestId = e.RequestId,
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
                "WorksheetAccessRequested eşzamanlı duplicate (RequestId={RequestId}); atlanıyor.", e.RequestId);
            return;
        }

        // SignalR: badge akışıyla aynı hedefleme — Clients.User(keycloak subject).
        if (!string.IsNullOrWhiteSpace(e.TargetKeycloakId))
        {
            await _hub.Clients.User(e.TargetKeycloakId).SendAsync("AccessRequestUpdate", new
            {
                notificationId = notification.Id,
                kind = "requested",
                requestId = e.RequestId,
                worksheetId = e.WorksheetId,
                worksheetName = worksheetName,
                title = notification.Title,
                body = notification.Body
            }, ct);
        }
        else
        {
            _logger.LogWarning(
                "WorksheetAccessRequested: TargetKeycloakId boş (RequestId={RequestId}); bildirim kaydedildi ama push atlandı.",
                e.RequestId);
        }

        _logger.LogInformation(
            "WorksheetAccessRequested işlendi. RequestId={RequestId}, OwnerUserId={OwnerUserId}, NotificationId={NotificationId}",
            e.RequestId, e.OwnerUserId, notification.Id);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };
}
