using BadgeService.Hubs;
using BadgeService.Services;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;

namespace BadgeService.Consumers;

/// <summary>
/// Veli onayı bekleyen ders talebi bildirimi (issue #439): exam API'nin talep satırıyla aynı transaction'da yazdığı
/// <see cref="BookingParentApprovalRequestedEvent"/>'i alır ve Active bağlantılı HER veliye in-app bildirim üretir
/// ("Çocuğunuz X, Öğretmen Y ile {zaman} için ders talep etti — onayınız bekleniyor"), SignalR (<c>ParentNotification</c>) ile push eder.
/// Alıcı listesi producer'dadır (servisler arası DB/HTTP yok). Bildirim <c>data.studentId</c> taşır → UI <c>/parent?child=</c> açar.
///
/// Idempotency: alıcı başına deterministik anahtar (<see cref="BookingParentNotifier.RecipientKey"/>) <c>SourceEventId</c> olarak
/// <c>(Type, SourceEventId)</c> unique index'ine yazılır; önceden Any kontrolü + 23505 yakalama.
/// Hata yolu: beklenmeyen hata → 3 immediate retry (<see cref="BookingParentApprovalRequestedConsumerDefinition"/>) →
/// <c>badge-service_error</c>; sub çözülemeyen alıcı retry'sız error kuyruğuna (diğer alıcılar yazılır).
/// </summary>
public class BookingParentApprovalRequestedConsumer : IConsumer<BookingParentApprovalRequestedEvent>
{
    public const string NotificationType = "ParentBookingApprovalRequested";

    private readonly BadgeDbContext _db;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly IUserLocaleResolver _localeResolver;
    private readonly INotificationTextFactory _texts;
    private readonly ILogger<BookingParentApprovalRequestedConsumer> _logger;

    public BookingParentApprovalRequestedConsumer(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        IUserLocaleResolver localeResolver,
        INotificationTextFactory texts,
        ILogger<BookingParentApprovalRequestedConsumer> logger)
    {
        _db = db;
        _hub = hub;
        _localeResolver = localeResolver;
        _texts = texts;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<BookingParentApprovalRequestedEvent> context)
    {
        var e = context.Message;
        var when = BookingWhenText.Format(e.Date, e.StartTime, e.EndTime);
        var data = new { bookingId = e.BookingId, studentId = e.StudentId, expiresAt = e.ExpiresAt };

        await BookingParentNotifier.NotifyAsync(
            _db, _hub, _localeResolver, _logger, e.Parents, NotificationType,
            BookingParentNotifier.EventScope(e.EventId),
            culture => _texts.Build(NotificationType, culture,
                BookingParentNotifier.Child(_texts, culture, e.StudentName),
                BookingParentNotifier.Teacher(_texts, culture, e.TeacherName),
                when),
            data, $"EventId={e.EventId}, BookingId={e.BookingId}", excludeParentUserId: null, context.CancellationToken);
    }
}
