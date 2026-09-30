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
/// issue #298: öğretmenin hesap onayı askıya alındığında exam API'nin öğrenci başına yazdığı
/// <see cref="BookingTeacherUnavailableEvent"/>'i alır, öğrenciye "öğretmen geçici olarak müsait değil" in-app
/// <see cref="Notification"/> satırı oluşturur ve mevcut <c>BookingUpdate</c> SignalR mesajıyla push eder
/// (<see cref="BookingDecisionConsumer"/> ile aynı desen).
///
/// Güvenlik: metinde öğretmen adı ve askı nedeni YOK (payload'da da yok) — sabit metin + etkilenen randevu sayısı.
///
/// Alıcı sub'ı: event'te boşsa <see cref="NotificationRecipientResolver"/> ile BadgeService verisinden çözülür; çözülemezse
/// throw (bildirim sub'sız kaydedilip kaybolmaz).
/// Idempotency: (Type, SourceEventId) filtreli unique index — event'in kendi Guid'i. Önce AnyAsync ile kontrol, eşzamanlı
/// ikinci teslim unique ihlaline takılırsa no-op.
/// Hata yolu: beklenmeyen hata fırlatılır → 3 immediate retry (<see cref="BookingTeacherUnavailableConsumerDefinition"/>)
/// → <c>badge-service_error</c> (dead-letter). Sessiz yutma yok.
/// </summary>
public class BookingTeacherUnavailableConsumer : IConsumer<BookingTeacherUnavailableEvent>
{
    public const string NotificationType = "BookingTeacherUnavailable";

    /// <summary>SignalR <c>BookingUpdate</c> mesajındaki <c>kind</c> değeri.</summary>
    public const string PushKind = "teacherUnavailable";

    private readonly BadgeDbContext _db;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly IUserLocaleResolver _localeResolver;
    private readonly INotificationTextFactory _texts;
    private readonly ILogger<BookingTeacherUnavailableConsumer> _logger;

    public BookingTeacherUnavailableConsumer(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        IUserLocaleResolver localeResolver,
        INotificationTextFactory texts,
        ILogger<BookingTeacherUnavailableConsumer> logger)
    {
        _db = db;
        _hub = hub;
        _localeResolver = localeResolver ?? throw new ArgumentNullException(nameof(localeResolver));
        _texts = texts ?? throw new ArgumentNullException(nameof(texts));
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<BookingTeacherUnavailableEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        var exists = await _db.Notifications
            .AnyAsync(n => n.Type == NotificationType && n.SourceEventId == e.EventId, ct);
        if (exists)
        {
            _logger.LogInformation(
                "BookingTeacherUnavailable zaten işlenmiş (EventId={EventId}, TeacherId={TeacherId}); atlanıyor.",
                e.EventId, e.TeacherId);
            return;
        }

        // Security review O2: sub boşsa BadgeService verisinden çözülür; çözülemezse throw → retry → dead-letter.
        var keycloakId = await NotificationRecipientResolver.ResolveSubAsync(
            _db, e.StudentUserId, e.TargetKeycloakId, $"BookingTeacherUnavailable (EventId={e.EventId})", ct);
        var culture = await _localeResolver.ResolveAsync(e.StudentUserId, keycloakId, ct);
        var bookingIds = e.BookingIds ?? new List<int>();
        var text = _texts.Build(NotificationType, culture, bookingIds.Count);

        var notification = new Notification
        {
            UserId = e.StudentUserId,
            UserKeycloakId = keycloakId,
            Type = NotificationType,
            Title = text.Title,
            Body = text.Body,
            Data = JsonSerializer.Serialize(new
            {
                teacherId = e.TeacherId,
                bookingIds
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
                "BookingTeacherUnavailable eşzamanlı duplicate (EventId={EventId}); atlanıyor.", e.EventId);
            return;
        }

        await _hub.Clients.User(keycloakId).SendAsync("BookingUpdate", new
        {
            notificationId = notification.Id,
            kind = PushKind,
            teacherId = e.TeacherId,
            bookingIds,
            title = notification.Title,
            body = notification.Body
        }, ct);

        _logger.LogInformation(
            "BookingTeacherUnavailable işlendi. EventId={EventId}, TeacherId={TeacherId}, StudentUserId={StudentUserId}, Bookings={Count}, NotificationId={NotificationId}",
            e.EventId, e.TeacherId, e.StudentUserId, bookingIds.Count, notification.Id);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };
}
