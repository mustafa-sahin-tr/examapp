using System.Text.Json;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Services;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;

namespace BadgeService.Consumers;

/// <summary>
/// Issue #1091: çocuğun planı / denemesi birkaç gün içinde bitiyor. Exam API'nin hatırlatma job'ının her Active veli için ayrı
/// yazdığı <see cref="ParentChildPlanEndingEvent"/>'i alır, velinin in-app <see cref="Notification"/> satırını oluşturur ve SignalR
/// (<c>ParentNotification</c>) ile push eder. Alıcı kararı ve atama başına tek hatırlatma producer'dadır; idempotency
/// <see cref="ParentHomeworkOverdueConsumer"/> ile aynı (<c>(Type, SourceEventId)</c>). Issue #1110: <c>NewGradeLevel</c> doluysa aynı
/// tipte "sınıf bandı değişti, yenileme yeni fiyatla" uyarısı (<c>PlanBandChangeNoticeJob</c>, bitişten 30 gün önce).
/// </summary>
public class ParentChildPlanEndingConsumer : IConsumer<ParentChildPlanEndingEvent>
{
    public const string NotificationType = "ParentChildPlanEnding";

    private readonly BadgeDbContext _db;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly IUserLocaleResolver _localeResolver;
    private readonly INotificationTextFactory _texts;
    private readonly ILogger<ParentChildPlanEndingConsumer> _logger;

    public ParentChildPlanEndingConsumer(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        IUserLocaleResolver localeResolver,
        INotificationTextFactory texts,
        ILogger<ParentChildPlanEndingConsumer> logger)
    {
        _db = db;
        _hub = hub;
        _localeResolver = localeResolver;
        _texts = texts;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ParentChildPlanEndingEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        if (await ParentNotificationWriter.AlreadyProcessedAsync(_db, NotificationType, e.EventId, ct))
        {
            _logger.LogInformation(
                "ParentChildPlanEnding zaten işlenmiş (EventId={EventId}, AssignmentId={AssignmentId}); atlanıyor.", e.EventId, e.AssignmentId);
            return;
        }

        var sub = await NotificationRecipientResolver.ResolveSubAsync(
            _db, e.ParentUserId, e.ParentKeycloakId, $"ParentChildPlanEnding (EventId={e.EventId})", ct);
        var culture = await _localeResolver.ResolveAsync(e.ParentUserId, sub, ct);

        var cleanChild = CommentNotificationSupport.CleanName(e.StudentDisplayName);
        var child = string.IsNullOrWhiteSpace(cleanChild) ? _texts.Resolve("notifications.common.defaultChild", culture) : cleanChild;
        var endsOn = ActivityCompletionStats.ToIstanbulDate(e.EndsAtUtc).ToString("d MMMM", culture);
        // Deneme için ayrı metin ("deneme bitiyor, plan seç"), sınıf bandı değişiminde (#1110) "yenileme yeni sınıfın fiyatıyla";
        // Type aynı (idempotency / arayüz bağlantısı değişmez).
        var textKey = e.NewGradeLevel is not null ? NotificationType + "BandChange"
            : e.IsTrial ? NotificationType + "Trial"
            : NotificationType;
        var text = _texts.Build(textKey, culture, child, endsOn, e.NewGradeLevel ?? 0);

        var data = new { studentId = e.StudentId, assignmentId = e.AssignmentId };
        var notification = await ParentNotificationWriter.TryAddAsync(_db, new Notification
        {
            UserId = e.ParentUserId,
            UserKeycloakId = sub,
            Type = NotificationType,
            Title = text.Title,
            Body = text.Body,
            Data = JsonSerializer.Serialize(data),
            SourceEventId = e.EventId,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        }, ct);
        if (notification == null)
        {
            _logger.LogInformation(
                "ParentChildPlanEnding eşzamanlı duplicate (EventId={EventId}, AssignmentId={AssignmentId}); atlanıyor.", e.EventId, e.AssignmentId);
            return;
        }

        await ParentNotificationWriter.PushAsync(_hub, sub, notification, NotificationType, data, ct);

        _logger.LogInformation(
            "ParentChildPlanEnding işlendi. EventId={EventId}, AssignmentId={AssignmentId}, StudentId={StudentId}, ParentUserId={UserId}, NotificationId={NotificationId}",
            e.EventId, e.AssignmentId, e.StudentId, e.ParentUserId, notification.Id);
    }
}
