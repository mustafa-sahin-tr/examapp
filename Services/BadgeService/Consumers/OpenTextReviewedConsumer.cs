using System.Text.Json;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Services;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;

namespace BadgeService.Consumers;

/// <summary>
/// Issue #614: öğretmen açık uçlu cevabı değerlendirince exam API'nin aynı transaction'da yazdığı <see cref="OpenTextReviewedEvent"/>'i
/// alır; öğrencinin in-app <see cref="Notification"/> satırını oluşturur ve SignalR (<c>ParentNotification</c>) ile push eder.
/// <c>data</c> etkinlik oturumunu ve maddeyi taşır; UI <c>/activity/{testInstanceId}?item={instanceQuestionId}</c> açar.
///
/// Idempotency: <c>(Type, SourceEventId)</c> filtreli unique index + önceden <c>Any</c> kontrolü + 23505 yakalama.
/// Hata yolu: beklenmeyen hata → 3 kez immediate retry (<see cref="OpenTextReviewedConsumerDefinition"/>) → <c>badge-service_error</c>;
/// alıcı sub'ı çözülemezse retry yok, doğrudan aynı kuyruk.
/// </summary>
public class OpenTextReviewedConsumer : IConsumer<OpenTextReviewedEvent>
{
    public const string NotificationType = "OpenTextReviewed";

    private readonly BadgeDbContext _db;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly IUserLocaleResolver _localeResolver;
    private readonly INotificationTextFactory _texts;
    private readonly ILogger<OpenTextReviewedConsumer> _logger;

    public OpenTextReviewedConsumer(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        IUserLocaleResolver localeResolver,
        INotificationTextFactory texts,
        ILogger<OpenTextReviewedConsumer> logger)
    {
        _db = db;
        _hub = hub;
        _localeResolver = localeResolver;
        _texts = texts;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<OpenTextReviewedEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        if (await ParentNotificationWriter.AlreadyProcessedAsync(_db, NotificationType, e.EventId, ct))
        {
            _logger.LogInformation("OpenTextReviewed zaten işlenmiş (EventId={EventId}); atlanıyor.", e.EventId);
            return;
        }

        var sub = await NotificationRecipientResolver.ResolveSubAsync(
            _db, e.RecipientUserId, e.RecipientKeycloakId, $"OpenTextReviewed (EventId={e.EventId})", ct);
        var culture = await _localeResolver.ResolveAsync(e.RecipientUserId, sub, ct);

        var cleanTitle = CommentNotificationSupport.CleanTitle(e.WorksheetName);
        var worksheet = string.IsNullOrWhiteSpace(cleanTitle)
            ? _texts.Resolve("notifications.common.activityFallback", culture)
            : cleanTitle;
        var text = _texts.Build(NotificationType, culture, worksheet);

        var data = new
        {
            testInstanceId = e.TestInstanceId,
            instanceQuestionId = e.InstanceQuestionId,
            worksheetId = e.WorksheetId
        };
        var notification = await ParentNotificationWriter.TryAddAsync(_db, new Notification
        {
            UserId = e.RecipientUserId,
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
            _logger.LogInformation("OpenTextReviewed eşzamanlı duplicate (EventId={EventId}); atlanıyor.", e.EventId);
            return;
        }

        await ParentNotificationWriter.PushAsync(_hub, sub, notification, NotificationType, data, ct);

        _logger.LogInformation(
            "OpenTextReviewed işlendi. EventId={EventId}, RecipientUserId={UserId}, NotificationId={NotificationId}",
            e.EventId, e.RecipientUserId, notification.Id);
    }
}
