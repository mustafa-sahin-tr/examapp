using System.Text.Json;
using BadgeService.Entities;
using BadgeService.Hubs;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Consumers;

/// <summary>
/// Atama izni akışının tüketici ucu (karar tarafı): sınavın sahibi bir talebi onayladığında
/// ya da reddettiğinde exam API'nin yazdığı <see cref="WorksheetAccessRequestApprovedEvent"/> /
/// <see cref="WorksheetAccessRequestRejectedEvent"/>'i alır, talebi yapan öğretmene in-app
/// <see cref="Notification"/> satırı oluşturur ve SignalR ile push eder.
///
/// Tek consumer iki event'i handle eder; iki <c>Consume</c> overload'ı ortak
/// <see cref="HandleAsync"/> metoduna delege eder.
///
/// Hata yolu: beklenmeyen hata fırlatılır → MassTransit üç kez immediate retry → hâlâ
/// başarısızsa mesaj <c>badge-service_error</c> (dead-letter) kuyruğuna taşınır. Sessiz yutma yok.
/// Duplicate (aynı mesajın tekrar teslimi) hata değildir; loglanıp no-op ile geçilir.
/// </summary>
public class WorksheetAccessDecisionConsumer :
    IConsumer<WorksheetAccessRequestApprovedEvent>,
    IConsumer<WorksheetAccessRequestRejectedEvent>
{
    public const string ApprovedType = "WorksheetAccessApproved";
    public const string RejectedType = "WorksheetAccessRejected";

    private readonly BadgeDbContext _db;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly ILogger<WorksheetAccessDecisionConsumer> _logger;

    public WorksheetAccessDecisionConsumer(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        ILogger<WorksheetAccessDecisionConsumer> logger)
    {
        _db = db;
        _hub = hub;
        _logger = logger;
    }

    public Task Consume(ConsumeContext<WorksheetAccessRequestApprovedEvent> context)
    {
        var e = context.Message;
        return HandleAsync(e.RequestId, e.WorksheetId, e.WorksheetName, e.TargetKeycloakId,
            e.RequesterUserId, approved: true, e.DecidedAt, context.CancellationToken);
    }

    public Task Consume(ConsumeContext<WorksheetAccessRequestRejectedEvent> context)
    {
        var e = context.Message;
        return HandleAsync(e.RequestId, e.WorksheetId, e.WorksheetName, e.TargetKeycloakId,
            e.RequesterUserId, approved: false, e.DecidedAt, context.CancellationToken);
    }

    private async Task HandleAsync(
        int requestId,
        int worksheetId,
        string worksheetName,
        string targetSub,
        int requesterUserId,
        bool approved,
        DateTime decidedAt,
        CancellationToken ct)
    {
        var type = approved ? ApprovedType : RejectedType;

        // Idempotency: bu karar için talep edene bildirim zaten üretildiyse çık.
        var exists = await _db.Notifications
            .AnyAsync(n => n.Type == type && n.SourceAccessRequestId == requestId, ct);
        if (exists)
        {
            _logger.LogInformation(
                "WorksheetAccessDecision zaten işlenmiş (RequestId={RequestId}, Type={Type}); atlanıyor.",
                requestId, type);
            return;
        }

        var name = string.IsNullOrWhiteSpace(worksheetName) ? "bir sınav" : worksheetName;

        var notification = new Notification
        {
            UserId = requesterUserId,
            UserKeycloakId = string.IsNullOrWhiteSpace(targetSub) ? null : targetSub,
            Type = type,
            Title = approved ? "Atama izniniz onaylandı" : "Atama izni talebiniz reddedildi",
            Body = approved
                ? $"\"{name}\" sınavı için atama izni talebiniz onaylandı."
                : $"\"{name}\" sınavı için atama izni talebiniz reddedildi.",
            Data = JsonSerializer.Serialize(new
            {
                requestId,
                worksheetId,
                approved
            }),
            SourceAccessRequestId = requestId,
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
                "WorksheetAccessDecision eşzamanlı duplicate (RequestId={RequestId}, Type={Type}); atlanıyor.",
                requestId, type);
            return;
        }

        // SignalR: badge akışıyla aynı hedefleme — Clients.User(keycloak subject).
        if (!string.IsNullOrWhiteSpace(targetSub))
        {
            await _hub.Clients.User(targetSub).SendAsync("AccessRequestUpdate", new
            {
                notificationId = notification.Id,
                kind = approved ? "approved" : "rejected",
                requestId,
                worksheetId,
                worksheetName = name,
                title = notification.Title,
                body = notification.Body
            }, ct);
        }
        else
        {
            _logger.LogWarning(
                "WorksheetAccessDecision: TargetKeycloakId boş (RequestId={RequestId}); bildirim kaydedildi ama push atlandı.",
                requestId);
        }

        _logger.LogInformation(
            "WorksheetAccessDecision işlendi. RequestId={RequestId}, Type={Type}, RequesterUserId={RequesterUserId}, NotificationId={NotificationId}",
            requestId, type, requesterUserId, notification.Id);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };
}
