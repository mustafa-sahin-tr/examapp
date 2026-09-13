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
/// Ders planlama akışının tüketici ucu (talep tarafı, issue #96): bir öğrenci bir öğretmenin
/// müsaitlik aralığı için randevu talebi oluşturduğunda exam API'nin yazdığı
/// <see cref="BookingRequestCreatedEvent"/>'i alır, öğretmene in-app <see cref="Notification"/>
/// satırı oluşturur ve SignalR ile push eder.
///
/// Hata yolu: beklenmeyen hata fırlatılır → MassTransit üç kez immediate retry → hâlâ
/// başarısızsa mesaj <c>badge-service_error</c> (dead-letter) kuyruğuna taşınır. Sessiz yutma yok.
/// Duplicate (aynı mesajın tekrar teslimi) hata değildir; loglanıp no-op ile geçilir.
/// </summary>
public class BookingRequestCreatedConsumer : IConsumer<BookingRequestCreatedEvent>
{
    public const string NotificationType = "BookingRequestCreated";

    private readonly BadgeDbContext _db;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly IUserLocaleResolver _localeResolver;
    private readonly INotificationTextFactory _texts;
    private readonly ILogger<BookingRequestCreatedConsumer> _logger;

    /// <summary>
    /// <paramref name="localeResolver"/>/<paramref name="texts"/> opsiyonel: DI dışında oluşturan
    /// birim testler (<c>new BookingRequestCreatedConsumer(db, hub, logger)</c>) derlenmeye devam
    /// etsin diye. Üretimde <c>Program.cs</c> ikisini de DI ile kayıtlı gerçek implementasyonla verir.
    /// </summary>
    public BookingRequestCreatedConsumer(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        ILogger<BookingRequestCreatedConsumer> logger,
        IUserLocaleResolver? localeResolver = null,
        INotificationTextFactory? texts = null)
    {
        _db = db;
        _hub = hub;
        _logger = logger;
        _localeResolver = localeResolver ?? FallbackUserLocaleResolver.Instance;
        _texts = texts ?? FallbackNotificationTextFactory.Instance;
    }

    public async Task Consume(ConsumeContext<BookingRequestCreatedEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        // Idempotency: bu talep için öğretmene bildirim zaten üretildiyse çık.
        var exists = await _db.Notifications
            .AnyAsync(n => n.Type == NotificationType && n.SourceBookingId == e.BookingId, ct);
        if (exists)
        {
            _logger.LogInformation(
                "BookingRequestCreated zaten işlenmiş (BookingId={BookingId}); atlanıyor.", e.BookingId);
            return;
        }

        var culture = await _localeResolver.ResolveAsync(e.TeacherUserId, e.TargetKeycloakId, ct);
        var studentName = string.IsNullOrWhiteSpace(e.StudentName)
            ? _texts.Resolve("notifications.common.defaultStudent", culture)
            : e.StudentName;
        var whenText = $"{e.Date:dd.MM.yyyy} {e.StartTime:HH:mm}-{e.EndTime:HH:mm}";
        var text = _texts.Build(NotificationType, culture, studentName, whenText);

        var notification = new Notification
        {
            UserId = e.TeacherUserId,
            UserKeycloakId = string.IsNullOrWhiteSpace(e.TargetKeycloakId) ? null : e.TargetKeycloakId,
            Type = NotificationType,
            Title = text.Title,
            Body = text.Body,
            Data = JsonSerializer.Serialize(new
            {
                bookingId = e.BookingId,
                availabilitySlotId = e.AvailabilitySlotId
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
                "BookingRequestCreated eşzamanlı duplicate (BookingId={BookingId}); atlanıyor.", e.BookingId);
            return;
        }

        // SignalR: badge/atama izni akışlarıyla aynı hedefleme — Clients.User(keycloak subject).
        if (!string.IsNullOrWhiteSpace(e.TargetKeycloakId))
        {
            await _hub.Clients.User(e.TargetKeycloakId).SendAsync("BookingUpdate", new
            {
                notificationId = notification.Id,
                kind = "requested",
                bookingId = e.BookingId,
                availabilitySlotId = e.AvailabilitySlotId,
                title = notification.Title,
                body = notification.Body
            }, ct);
        }
        else
        {
            _logger.LogWarning(
                "BookingRequestCreated: TargetKeycloakId boş (BookingId={BookingId}); bildirim kaydedildi ama push atlandı.",
                e.BookingId);
        }

        _logger.LogInformation(
            "BookingRequestCreated işlendi. BookingId={BookingId}, TeacherUserId={TeacherUserId}, NotificationId={NotificationId}",
            e.BookingId, e.TeacherUserId, notification.Id);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };
}
