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

    /// <summary>issue #298: otomatik ret metninin sözlük anahtarı (<c>notifications.{key}.title/body</c>); Type değil.</summary>
    public const string TeacherUnavailableRejectedTextKey = "BookingRejectedTeacherUnavailable";

    private readonly BadgeDbContext _db;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly IUserLocaleResolver _localeResolver;
    private readonly INotificationTextFactory _texts;
    private readonly ILogger<BookingDecisionConsumer> _logger;

    public BookingDecisionConsumer(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        ILogger<BookingDecisionConsumer> logger,
        IUserLocaleResolver localeResolver,
        INotificationTextFactory texts)
    {
        _db = db;
        _hub = hub;
        _logger = logger;
        _localeResolver = localeResolver ?? throw new ArgumentNullException(nameof(localeResolver));
        _texts = texts ?? throw new ArgumentNullException(nameof(texts));
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

        // Security review O2 (#298): üretici sub'ı çözemediyse BadgeService'in kendi verisinden çözülür; çözülemezse throw →
        // retry → badge-service_error (bildirim sessizce sub'sız kaydedilip kaybolmaz — sahiplik sorguları sub'la yapılır).
        var recipientSub = await NotificationRecipientResolver.ResolveSubAsync(
            _db, e.StudentUserId, e.TargetKeycloakId, $"BookingDecision (BookingId={e.BookingId})", ct);

        var culture = await _localeResolver.ResolveAsync(e.StudentUserId, recipientSub, ct);
        var teacherName = string.IsNullOrWhiteSpace(e.TeacherName)
            ? _texts.Resolve("notifications.common.defaultTeacher", culture)
            : e.TeacherName;
        var whenText = $"{e.Date:dd.MM.yyyy} {e.StartTime:HH:mm}-{e.EndTime:HH:mm}";
        var reasonSuffix = string.IsNullOrWhiteSpace(e.RejectionReason)
            ? string.Empty
            : _texts.Resolve("notifications.common.rejectionReasonSuffix", culture, e.RejectionReason);
        // issue #298: öğretmen askıya alındığı için sistemin otomatik reddi — Type yine BookingRejected (idempotency ve UI
        // aynı), yalnızca metin "öğretmen geçici olarak müsait değil" olur. Askı nedeni event'te yok.
        var textKey = !e.Approved && e.TeacherUnavailable ? TeacherUnavailableRejectedTextKey : type;
        var text = _texts.Build(textKey, culture, teacherName, whenText, reasonSuffix);

        var notification = new Notification
        {
            UserId = e.StudentUserId,
            UserKeycloakId = recipientSub,
            Type = type,
            Title = text.Title,
            Body = text.Body,
            Data = JsonSerializer.Serialize(new
            {
                bookingId = e.BookingId,
                approved = e.Approved,
                teacherUnavailable = e.TeacherUnavailable // issue #298
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
        await _hub.Clients.User(recipientSub).SendAsync("BookingUpdate", new
        {
            notificationId = notification.Id,
            kind = e.Approved ? "approved" : "rejected",
            teacherUnavailable = e.TeacherUnavailable, // issue #298
            bookingId = e.BookingId,
            title = notification.Title,
            body = notification.Body
        }, ct);

        _logger.LogInformation(
            "BookingDecision işlendi. BookingId={BookingId}, Type={Type}, StudentUserId={StudentUserId}, NotificationId={NotificationId}",
            e.BookingId, type, e.StudentUserId, notification.Id);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };
}
