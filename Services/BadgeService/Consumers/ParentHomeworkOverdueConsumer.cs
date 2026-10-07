using System.Text.Json;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Services;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;

namespace BadgeService.Consumers;

/// <summary>
/// Gecikmiş ödev bildirimi (issue #423, epic #407 V5): exam API'nin süpürücü job'ının (atama, öğrenci) işaretçisiyle aynı
/// transaction'da, her Active veli için ayrı yazdığı <see cref="ParentHomeworkOverdueEvent"/>'i alır, velinin in-app
/// <see cref="Notification"/> satırını oluşturur ve SignalR (<c>ParentNotification</c>) ile push eder.
///
/// "Hangi veliler" kararı producer'dadır (veli–öğrenci bağlantıları exam DB'de; servisler arası DB/HTTP paylaşımı yok) — bu consumer
/// bağlantıyı yeniden sorgulamaz. Event başına TEK alıcı.
///
/// Idempotency: <c>(Type, SourceEventId)</c> filtreli unique index + önceden <c>Any</c> kontrolü + 23505 yakalama. (Atama, öğrenci)
/// başına tek bildirim garantisi producer'ın işaretçi tablosundadır; bu katman aynı event'in tekrar teslimini keser.
///
/// Hata yolu: beklenmeyen hata → MassTransit 3 kez immediate retry (<see cref="ParentHomeworkOverdueConsumerDefinition"/>) →
/// <c>badge-service_error</c> (dead-letter). Alıcı sub'ı çözülemezse <see cref="InvalidOperationException"/> → aynı yol.
/// Duplicate hata değildir (loglanıp no-op).
/// </summary>
public class ParentHomeworkOverdueConsumer : IConsumer<ParentHomeworkOverdueEvent>
{
    public const string NotificationType = "ParentHomeworkOverdue";

    private readonly BadgeDbContext _db;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly IUserLocaleResolver _localeResolver;
    private readonly INotificationTextFactory _texts;
    private readonly ILogger<ParentHomeworkOverdueConsumer> _logger;

    public ParentHomeworkOverdueConsumer(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        IUserLocaleResolver localeResolver,
        INotificationTextFactory texts,
        ILogger<ParentHomeworkOverdueConsumer> logger)
    {
        _db = db;
        _hub = hub;
        _localeResolver = localeResolver;
        _texts = texts;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ParentHomeworkOverdueEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        if (await ParentNotificationWriter.AlreadyProcessedAsync(_db, NotificationType, e.EventId, ct))
        {
            _logger.LogInformation(
                "ParentHomeworkOverdue zaten işlenmiş (EventId={EventId}, AssignmentId={AssignmentId}); atlanıyor.", e.EventId, e.AssignmentId);
            return;
        }

        var sub = await NotificationRecipientResolver.ResolveSubAsync(
            _db, e.ParentUserId, e.ParentKeycloakId, $"ParentHomeworkOverdue (EventId={e.EventId})", ct);
        var culture = await _localeResolver.ResolveAsync(e.ParentUserId, sub, ct);

        var cleanChild = CommentNotificationSupport.CleanName(e.StudentDisplayName);
        var child = string.IsNullOrWhiteSpace(cleanChild) ? _texts.Resolve("notifications.common.defaultChild", culture) : cleanChild;
        var cleanWorksheet = CommentNotificationSupport.CleanTitle(e.WorksheetName);
        var worksheet = string.IsNullOrWhiteSpace(cleanWorksheet) ? _texts.Resolve("notifications.common.unnamedWorksheet", culture) : cleanWorksheet;
        // Süresi dolarak kapanan oturum için ayrı metin ("süresi doldu"); Type aynı (idempotency/UI değişmez).
        var text = _texts.Build(e.InstanceExpired ? NotificationType + "Expired" : NotificationType, culture, child, worksheet);

        var notification = await ParentNotificationWriter.TryAddAsync(_db, new Notification
        {
            UserId = e.ParentUserId,
            UserKeycloakId = sub,
            Type = NotificationType,
            Title = text.Title,
            Body = text.Body,
            Data = JsonSerializer.Serialize(new { studentId = e.StudentId, assignmentId = e.AssignmentId, worksheetId = e.WorksheetId }),
            SourceEventId = e.EventId,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        }, ct);
        if (notification == null)
        {
            _logger.LogInformation(
                "ParentHomeworkOverdue eşzamanlı duplicate (EventId={EventId}, AssignmentId={AssignmentId}); atlanıyor.", e.EventId, e.AssignmentId);
            return;
        }

        await ParentNotificationWriter.PushAsync(_hub, sub, notification, NotificationType,
            new { studentId = e.StudentId, assignmentId = e.AssignmentId, worksheetId = e.WorksheetId }, ct);

        _logger.LogInformation(
            "ParentHomeworkOverdue işlendi. EventId={EventId}, AssignmentId={AssignmentId}, StudentId={StudentId}, ParentUserId={UserId}, NotificationId={NotificationId}",
            e.EventId, e.AssignmentId, e.StudentId, e.ParentUserId, notification.Id);
    }
}
