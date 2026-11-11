using System.Text.Json;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Services;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;

namespace BadgeService.Consumers;

/// <summary>
/// Issue #1111: "Öğretmene sor" durum değişikliği. Exam API'nin <c>AskTeacherService</c>'inin yazdığı <see cref="AskTeacherNoticeEvent"/>'i
/// alır; alıcının in-app <see cref="Notification"/> satırını oluşturur ve SignalR ile push eder: yeni soru öğretmene
/// (<see cref="AskedType"/>), cevap (<see cref="AnsweredType"/>) ve süre dolumu (<see cref="ExpiredType"/>) öğrenciye. <c>data</c> soru
/// id'sini taşır; UI öğretmeni kuyruktaki soruya, öğrenciyi sorusuna götürür.
///
/// Idempotency: <c>(Type, SourceEventId)</c> filtreli unique index + önceden <c>Any</c> kontrolü + 23505 yakalama. Bilinmeyen
/// <c>Kind</c> loglanıp atlanır (yeni sürüm eski consumer'a düşerse kuyruk tıkanmaz).
/// </summary>
public class AskTeacherNoticeConsumer : IConsumer<AskTeacherNoticeEvent>
{
    public const string AskedType = "AskTeacherAsked";
    public const string AnsweredType = "AskTeacherAnswered";
    public const string ExpiredType = "AskTeacherExpired";

    private readonly BadgeDbContext _db;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly IUserLocaleResolver _localeResolver;
    private readonly INotificationTextFactory _texts;
    private readonly ILogger<AskTeacherNoticeConsumer> _logger;

    public AskTeacherNoticeConsumer(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        IUserLocaleResolver localeResolver,
        INotificationTextFactory texts,
        ILogger<AskTeacherNoticeConsumer> logger)
    {
        _db = db;
        _hub = hub;
        _localeResolver = localeResolver;
        _texts = texts;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<AskTeacherNoticeEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;
        var type = e.Kind switch
        {
            AskTeacherNoticeEvent.Asked => AskedType,
            AskTeacherNoticeEvent.Answered => AnsweredType,
            AskTeacherNoticeEvent.Expired => ExpiredType,
            _ => null,
        };
        if (type is null)
        {
            _logger.LogWarning("AskTeacherNotice bilinmeyen Kind={Kind} (EventId={EventId}); atlanıyor.", e.Kind, e.EventId);
            return;
        }

        if (await ParentNotificationWriter.AlreadyProcessedAsync(_db, type, e.EventId, ct))
        {
            _logger.LogInformation("AskTeacherNotice zaten işlenmiş (EventId={EventId}); atlanıyor.", e.EventId);
            return;
        }

        var sub = await NotificationRecipientResolver.ResolveSubAsync(
            _db, e.RecipientUserId, e.RecipientKeycloakId, $"AskTeacherNotice (EventId={e.EventId})", ct);
        var culture = await _localeResolver.ResolveAsync(e.RecipientUserId, sub, ct);

        var cleanName = CommentNotificationSupport.CleanName(e.CounterpartName);
        var counterpart = string.IsNullOrWhiteSpace(cleanName)
            ? _texts.Resolve(type == AskedType ? "notifications.common.defaultStudent" : "notifications.common.defaultTeacher", culture)
            : cleanName;
        // {0} karşı taraf, {1} yeni soruda kalan cevap süresi (saat, en az 1).
        var hoursLeft = Math.Max(1, (int)Math.Round((e.DueAtUtc - DateTime.UtcNow).TotalHours));
        var text = _texts.Build(type, culture, counterpart, hoursLeft);

        var data = new { questionId = e.QuestionId };
        var notification = await ParentNotificationWriter.TryAddAsync(_db, new Notification
        {
            UserId = e.RecipientUserId,
            UserKeycloakId = sub,
            Type = type,
            Title = text.Title,
            Body = text.Body,
            Data = JsonSerializer.Serialize(data),
            SourceEventId = e.EventId,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        }, ct);
        if (notification == null)
        {
            _logger.LogInformation("AskTeacherNotice eşzamanlı duplicate (EventId={EventId}); atlanıyor.", e.EventId);
            return;
        }

        await ParentNotificationWriter.PushAsync(_hub, sub, notification, type, data, ct);

        _logger.LogInformation(
            "AskTeacherNotice işlendi. EventId={EventId}, Type={Type}, QuestionId={QuestionId}, RecipientUserId={UserId}",
            e.EventId, type, e.QuestionId, e.RecipientUserId);
    }
}
