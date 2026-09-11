using System.Text.Json;
using BadgeService.Entities;
using BadgeService.Hubs;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Consumers;

/// <summary>
/// Ders planlama akışının tüketici ucu (karar tarafı, issue #96): öğretmen bir randevu talebini
/// onayladığında ya da reddettiğinde exam API'nin yazdığı <see cref="BookingDecisionEvent"/>'i
/// alır, talebi oluşturan öğrenciye in-app <see cref="Notification"/> satırı oluşturur ve
/// SignalR ile push eder.
///
/// Hata yolu: beklenmeyen hata fırlatılır → MassTransit üç kez immediate retry → hâlâ
/// başarısızsa mesaj <c>badge-service_error</c> (dead-letter) kuyruğuna taşınır. Sessiz yutma yok.
/// Duplicate (aynı mesajın tekrar teslimi) hata değildir; loglanıp no-op ile geçilir.
/// </summary>
public class BookingDecisionConsumer : IConsumer<BookingDecisionEvent>
{
    public const string ApprovedType = "BookingApproved";
    public const string RejectedType = "BookingRejected";

    private readonly BadgeDbContext _db;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly ILogger<BookingDecisionConsumer> _logger;

    public BookingDecisionConsumer(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        ILogger<BookingDecisionConsumer> logger)
    {
        _db = db;
        _hub = hub;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<BookingDecisionEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;
        var type = e.Approved ? ApprovedType : RejectedType;

        // Idempotency: bu karar için öğrenciye bildirim zaten üretildiyse çık.
        var exists = await _db.Notifications
            .AnyAsync(n => n.Type == type && n.SourceBookingId == e.BookingId, ct);
        if (exists)
        {
            _logger.LogInformation(
                "BookingDecision zaten işlenmiş (BookingId={BookingId}, Type={Type}); atlanıyor.",
                e.BookingId, type);
            return;
        }

        var teacherName = string.IsNullOrWhiteSpace(e.TeacherName) ? "Öğretmeniniz" : e.TeacherName;
        var whenText = $"{e.Date:dd.MM.yyyy} {e.StartTime:HH:mm}-{e.EndTime:HH:mm}";

        var body = e.Approved
            ? $"{teacherName}, {whenText} için ders talebinizi onayladı."
            : $"{teacherName}, {whenText} için ders talebinizi reddetti."
                + (string.IsNullOrWhiteSpace(e.RejectionReason) ? string.Empty : $" Gerekçe: {e.RejectionReason}");

        var notification = new Notification
        {
            UserId = e.StudentUserId,
            UserKeycloakId = string.IsNullOrWhiteSpace(e.TargetKeycloakId) ? null : e.TargetKeycloakId,
            Type = type,
            Title = e.Approved ? "Ders talebiniz onaylandı" : "Ders talebiniz reddedildi",
            Body = body,
            Data = JsonSerializer.Serialize(new
            {
                bookingId = e.BookingId,
                approved = e.Approved
            }),
            SourceBookingId = e.BookingId,
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
                "BookingDecision eşzamanlı duplicate (BookingId={BookingId}, Type={Type}); atlanıyor.",
                e.BookingId, type);
            return;
        }

        // SignalR: badge/atama izni akışlarıyla aynı hedefleme — Clients.User(keycloak subject).
        if (!string.IsNullOrWhiteSpace(e.TargetKeycloakId))
        {
            await _hub.Clients.User(e.TargetKeycloakId).SendAsync("BookingUpdate", new
            {
                notificationId = notification.Id,
                kind = e.Approved ? "approved" : "rejected",
                bookingId = e.BookingId,
                title = notification.Title,
                body = notification.Body
            }, ct);
        }
        else
        {
            _logger.LogWarning(
                "BookingDecision: TargetKeycloakId boş (BookingId={BookingId}); bildirim kaydedildi ama push atlandı.",
                e.BookingId);
        }

        _logger.LogInformation(
            "BookingDecision işlendi. BookingId={BookingId}, Type={Type}, StudentUserId={StudentUserId}, NotificationId={NotificationId}",
            e.BookingId, type, e.StudentUserId, notification.Id);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };
}
