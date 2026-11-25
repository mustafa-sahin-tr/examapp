using System.Text.Json;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Services;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;

namespace BadgeService.Consumers;

/// <summary>
/// Issue #519: the study nudge. Takes the <see cref="StudentStudyNudgeEvent"/> the exam API's <c>StudentStudyNudgeJob</c> wrote with
/// the student's stamp and creates the student's in-app <see cref="Notification"/> ("3 gündür soru çözmedin"), pushed over SignalR.
/// Template text in the recipient's language. Idempotency as <see cref="StudentGradePromotedConsumer"/>: <c>(Type, SourceEventId)</c>.
/// </summary>
public class StudentStudyNudgeConsumer : IConsumer<StudentStudyNudgeEvent>
{
    public const string NotificationType = "StudyNudge";

    private readonly BadgeDbContext _db;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly IUserLocaleResolver _localeResolver;
    private readonly INotificationTextFactory _texts;
    private readonly ILogger<StudentStudyNudgeConsumer> _logger;

    public StudentStudyNudgeConsumer(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        IUserLocaleResolver localeResolver,
        INotificationTextFactory texts,
        ILogger<StudentStudyNudgeConsumer> logger)
    {
        _db = db;
        _hub = hub;
        _localeResolver = localeResolver;
        _texts = texts;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<StudentStudyNudgeEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        if (await ParentNotificationWriter.AlreadyProcessedAsync(_db, NotificationType, e.EventId, ct))
        {
            _logger.LogInformation("StudentStudyNudge already processed (EventId={EventId}); skipping.", e.EventId);
            return;
        }

        var sub = await NotificationRecipientResolver.ResolveSubAsync(
            _db, e.RecipientUserId, e.RecipientKeycloakId, $"StudentStudyNudge (EventId={e.EventId})", ct);
        var culture = await _localeResolver.ResolveAsync(e.RecipientUserId, sub, ct);
        // {0} days without an answer.
        var text = _texts.Build(NotificationType, culture, e.InactiveDays);

        var data = new { inactiveDays = e.InactiveDays };
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
            _logger.LogInformation("StudentStudyNudge concurrent duplicate (EventId={EventId}); skipping.", e.EventId);
            return;
        }

        await ParentNotificationWriter.PushAsync(_hub, sub, notification, NotificationType, data, ct);

        _logger.LogInformation(
            "StudentStudyNudge processed. EventId={EventId}, StudentId={StudentId}, RecipientUserId={UserId}, NotificationId={NotificationId}",
            e.EventId, e.StudentId, e.RecipientUserId, notification.Id);
    }
}
