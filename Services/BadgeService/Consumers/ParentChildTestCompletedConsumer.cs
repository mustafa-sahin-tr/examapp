using System.Text.Json;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Services;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;

namespace BadgeService.Consumers;

/// <summary>
/// Çocuk test tamamladı bildirimi (issue #423, epic #407 V5): exam API'nin <c>EndTest</c>'te durum değişikliğiyle aynı
/// transaction'da, o an Active bağlantılı her veli için yazdığı <see cref="ParentChildTestCompletedEvent"/>'i alır; velinin
/// in-app <see cref="Notification"/> satırını oluşturur ve SignalR (<c>ParentNotification</c>) ile push eder:
/// "{çocuk} {test} testini tamamladı, puan {Y}".
///
/// Alıcı kararı (yalnız Active bağlantılar) producer'dadır; Pending/Revoked bağlantı için event hiç yazılmaz. Bu consumer
/// bağlantıyı yeniden sorgulamaz (servisler arası DB/HTTP paylaşımı yok). Event başına TEK alıcı.
///
/// Idempotency: <c>(Type, SourceEventId)</c> filtreli unique index + önceden <c>Any</c> kontrolü + 23505 yakalama.
/// Hata yolu: beklenmeyen hata → MassTransit 3 kez immediate retry (<see cref="ParentChildTestCompletedConsumerDefinition"/>) →
/// <c>badge-service_error</c> (dead-letter); alıcı sub'ı çözülemezse <see cref="InvalidOperationException"/> → aynı yol.
/// Duplicate hata değildir (loglanıp no-op).
/// </summary>
public class ParentChildTestCompletedConsumer : IConsumer<ParentChildTestCompletedEvent>
{
    public const string NotificationType = "ParentChildTestCompleted";

    private readonly BadgeDbContext _db;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly IUserLocaleResolver _localeResolver;
    private readonly INotificationTextFactory _texts;
    private readonly ILogger<ParentChildTestCompletedConsumer> _logger;

    public ParentChildTestCompletedConsumer(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        IUserLocaleResolver localeResolver,
        INotificationTextFactory texts,
        ILogger<ParentChildTestCompletedConsumer> logger)
    {
        _db = db;
        _hub = hub;
        _localeResolver = localeResolver;
        _texts = texts;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ParentChildTestCompletedEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        if (await ParentNotificationWriter.AlreadyProcessedAsync(_db, NotificationType, e.EventId, ct))
        {
            _logger.LogInformation(
                "ParentChildTestCompleted zaten işlenmiş (EventId={EventId}, TestInstanceId={InstanceId}); atlanıyor.", e.EventId, e.TestInstanceId);
            return;
        }

        var sub = await NotificationRecipientResolver.ResolveSubAsync(
            _db, e.ParentUserId, e.ParentKeycloakId, $"ParentChildTestCompleted (EventId={e.EventId})", ct);
        var culture = await _localeResolver.ResolveAsync(e.ParentUserId, sub, ct);

        var cleanChild = CommentNotificationSupport.CleanName(e.StudentDisplayName);
        var child = string.IsNullOrWhiteSpace(cleanChild) ? _texts.Resolve("notifications.common.defaultChild", culture) : cleanChild;
        var cleanWorksheet = CommentNotificationSupport.CleanTitle(e.WorksheetName);
        var worksheet = string.IsNullOrWhiteSpace(cleanWorksheet) ? _texts.Resolve("notifications.common.unnamedWorksheet", culture) : cleanWorksheet;
        var text = _texts.Build(NotificationType, culture, child, worksheet, e.Score);

        var data = new { studentId = e.StudentId, testInstanceId = e.TestInstanceId, worksheetId = e.WorksheetId, score = e.Score };
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
                "ParentChildTestCompleted eşzamanlı duplicate (EventId={EventId}, TestInstanceId={InstanceId}); atlanıyor.", e.EventId, e.TestInstanceId);
            return;
        }

        await ParentNotificationWriter.PushAsync(_hub, sub, notification, NotificationType, data, ct);

        _logger.LogInformation(
            "ParentChildTestCompleted işlendi. EventId={EventId}, TestInstanceId={InstanceId}, StudentId={StudentId}, ParentUserId={UserId}, NotificationId={NotificationId}",
            e.EventId, e.TestInstanceId, e.StudentId, e.ParentUserId, notification.Id);
    }
}
