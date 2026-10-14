using System.Text.Json;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Services;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;

namespace BadgeService.Consumers;

/// <summary>
/// Veli onayı sonucu bildirimi (issue #439): <see cref="BookingParentDecisionEvent"/> (Approved | Rejected | Expired).
/// <list type="bullet">
/// <item>Öğrenci (tek alıcı, <c>SourceEventId = EventId</c>): Approved "Velin onayladı, öğretmen onayı bekleniyor";
/// Rejected "Velin reddetti" (+ not); Expired "veli onayı zamanında gelmedi". Types: BookingParentApproved / BookingParentRejected / BookingParentExpired.</item>
/// <item>Veliler (alıcı başına deterministik anahtar): Expired → hepsine ParentBookingExpired; Approved/Rejected → kararı veren
/// HARİÇ diğerlerine ParentBookingApprovedByOther / ParentBookingRejectedByOther ("talep başka bir veli kararıyla kapandı").</item>
/// </list>
/// Idempotency: öğrenci satırı <c>(Type, EventId)</c>, veli satırları <c>(Type, MD5(EventId:ParentUserId))</c> unique index'i; redelivery'de
/// yalnız eksik olanlar yazılır (öğrenci satırı varsa bile veli fan-out'u devam eder).
/// Hata yolu: 3 immediate retry (sub çözülemedi hariç) → <c>badge-service_error</c>.
/// </summary>
public class BookingParentDecisionConsumer : IConsumer<BookingParentDecisionEvent>
{
    public const string StudentApprovedType = "BookingParentApproved";
    public const string StudentRejectedType = "BookingParentRejected";
    public const string StudentExpiredType = "BookingParentExpired";
    public const string ParentExpiredType = "ParentBookingExpired";
    public const string ParentApprovedByOtherType = "ParentBookingApprovedByOther";
    public const string ParentRejectedByOtherType = "ParentBookingRejectedByOther";

    private readonly BadgeDbContext _db;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly IUserLocaleResolver _localeResolver;
    private readonly INotificationTextFactory _texts;
    private readonly ILogger<BookingParentDecisionConsumer> _logger;

    public BookingParentDecisionConsumer(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        IUserLocaleResolver localeResolver,
        INotificationTextFactory texts,
        ILogger<BookingParentDecisionConsumer> logger)
    {
        _db = db;
        _hub = hub;
        _localeResolver = localeResolver;
        _texts = texts;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<BookingParentDecisionEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        var studentType = e.Decision switch
        {
            BookingParentDecisions.Approved => StudentApprovedType,
            BookingParentDecisions.Rejected => StudentRejectedType,
            BookingParentDecisions.Expired => StudentExpiredType,
            _ => throw new InvalidOperationException($"BookingParentDecision: bilinmeyen karar '{e.Decision}' (EventId={e.EventId}).")
        };

        // Öğrenci sub'ı çözülemezse veli fan-out'u yine çalışır; hata sonda yeniden fırlatılır (error kuyruğu, replay'de eksik yazılır).
        RecipientSubUnresolvedException? studentUnresolved = null;
        try
        {
            await NotifyStudentAsync(e, studentType, ct);
        }
        catch (RecipientSubUnresolvedException ex)
        {
            studentUnresolved = ex;
            _logger.LogError(ex, "{Type} öğrenci sub'ı çözülemedi (EventId={EventId}); veli bildirimleri yine de yazılacak.", studentType, e.EventId);
        }

        var scope = BookingParentNotifier.EventScope(e.EventId);
        var when = BookingWhenText.Format(e.Date, e.StartTime, e.EndTime);
        var data = new { bookingId = e.BookingId, studentId = e.StudentId, decision = e.Decision };
        var log = $"EventId={e.EventId}, BookingId={e.BookingId}";

        if (e.Decision == BookingParentDecisions.Expired)
        {
            await BookingParentNotifier.NotifyAsync(
                _db, _hub, _localeResolver, _logger, e.Parents, ParentExpiredType, scope,
                culture => _texts.Build(ParentExpiredType, culture,
                    BookingParentNotifier.Child(_texts, culture, e.StudentName),
                    BookingParentNotifier.Teacher(_texts, culture, e.TeacherName),
                    when),
                data, log, excludeParentUserId: null, ct);
            if (studentUnresolved != null) throw studentUnresolved;
            return;
        }

        var otherType = e.Decision == BookingParentDecisions.Approved ? ParentApprovedByOtherType : ParentRejectedByOtherType;
        var deciderName = CommentNotificationSupport.CleanName(e.DecidedByParentName);
        await BookingParentNotifier.NotifyAsync(
            _db, _hub, _localeResolver, _logger, e.Parents, otherType, scope,
            culture => _texts.Build(otherType, culture,
                BookingParentNotifier.Child(_texts, culture, e.StudentName),
                BookingParentNotifier.Teacher(_texts, culture, e.TeacherName),
                when,
                string.IsNullOrWhiteSpace(deciderName)
                    ? _texts.Resolve("notifications.common.defaultCoParent", culture)
                    : deciderName),
            data, log, excludeParentUserId: e.DecidedByParentUserId, ct);
        if (studentUnresolved != null) throw studentUnresolved;
    }

    private async Task NotifyStudentAsync(BookingParentDecisionEvent e, string type, CancellationToken ct)
    {
        if (await ParentNotificationWriter.AlreadyProcessedAsync(_db, type, e.EventId, ct))
        {
            _logger.LogInformation("{Type} öğrenci bildirimi zaten işlenmiş (EventId={EventId}); atlanıyor.", type, e.EventId);
            return;
        }

        var sub = await NotificationRecipientResolver.ResolveSubAsync(
            _db, e.StudentUserId, e.StudentKeycloakId, $"{type} (EventId={e.EventId}, BookingId={e.BookingId})", ct);
        var culture = await _localeResolver.ResolveAsync(e.StudentUserId, sub, ct);
        var teacher = BookingParentNotifier.Teacher(_texts, culture, e.TeacherName);
        var when = BookingWhenText.Format(e.Date, e.StartTime, e.EndTime);
        var noteSuffix = type == StudentRejectedType && !string.IsNullOrWhiteSpace(e.Note)
            ? _texts.Resolve("notifications.common.rejectionReasonSuffix", culture, e.Note.Trim())
            : string.Empty;
        var text = _texts.Build(type, culture, teacher, when, noteSuffix);

        var data = new { bookingId = e.BookingId, decision = e.Decision };
        var notification = await ParentNotificationWriter.TryAddAsync(_db, new Notification
        {
            UserId = e.StudentUserId,
            UserKeycloakId = sub,
            Type = type,
            Title = text.Title,
            Body = text.Body,
            Data = JsonSerializer.Serialize(data),
            SourceEventId = e.EventId,
            SourceBookingId = e.BookingId,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        }, ct);
        if (notification == null)
        {
            _logger.LogInformation("{Type} öğrenci eşzamanlı duplicate (EventId={EventId}); atlanıyor.", type, e.EventId);
            return;
        }

        // Öğrenci tarafı mevcut randevu push'u (BookingUpdate): zil + liste tazelenir.
        await _hub.Clients.User(sub).SendAsync("BookingUpdate", new
        {
            notificationId = notification.Id,
            kind = "parent" + e.Decision,
            bookingId = e.BookingId,
            title = notification.Title,
            body = notification.Body
        }, ct);

        _logger.LogInformation(
            "{Type} işlendi. EventId={EventId}, BookingId={BookingId}, StudentUserId={UserId}, NotificationId={NotificationId}",
            type, e.EventId, e.BookingId, e.StudentUserId, notification.Id);
    }
}
