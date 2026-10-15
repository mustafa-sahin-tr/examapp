using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Services;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;

namespace BadgeService.Consumers;

/// <summary>
/// Issue #441: veli bağlamayan öğrencinin ek süresi bitmeden 7 gün / 1 gün önce exam API'nin yazdığı
/// <see cref="StudentParentGraceReminderEvent"/>'i alır ve ÖĞRENCİYE uygulama içi bildirim üretir (e-posta değil),
/// SignalR (<c>ParentNotification</c>) ile push eder. Bildirim <c>data.link = "/parent-required"</c> taşır.
///
/// Idempotency: <c>(Type, SourceEventId)</c> unique index'ine <see cref="ReminderKey"/> = MD5("student-parent-grace:{StudentId}:{GraceEndsAt ticks}:{DaysLeft}")
/// yazılır; önceden Any kontrolü + 23505 yakalama (ParentNotificationWriter). Event'te EventId olmadığından anahtar içerikten türetilir.
/// Hata yolu: beklenmeyen hata, 3 immediate retry, sonra <c>badge-service_error</c>; StudentUserId boşsa
/// <see cref="RecipientSubUnresolvedException"/> (retry'sız error kuyruğu, sessiz kayıp yok).
/// Notification.UserId = 0 (event yalnız Keycloak sub taşır; sorgu/SignalR sub üzerinden, admin yayın satırlarıyla aynı emsal).
/// </summary>
public class StudentParentGraceReminderConsumer : IConsumer<StudentParentGraceReminderEvent>
{
    public const string NotificationType = "StudentParentGraceReminder";
    public const string ParentRequiredLink = "/parent-required";

    private readonly BadgeDbContext _db;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly IUserLocaleResolver _localeResolver;
    private readonly INotificationTextFactory _texts;
    private readonly ILogger<StudentParentGraceReminderConsumer> _logger;

    public StudentParentGraceReminderConsumer(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        IUserLocaleResolver localeResolver,
        INotificationTextFactory texts,
        ILogger<StudentParentGraceReminderConsumer> logger)
    {
        _db = db;
        _hub = hub;
        _localeResolver = localeResolver;
        _texts = texts;
        _logger = logger;
    }

    public static Guid ReminderKey(int studentId, DateTime graceEndsAt, int daysLeft)
        => new(MD5.HashData(Encoding.UTF8.GetBytes(
            $"student-parent-grace:{studentId}:{graceEndsAt.ToUniversalTime().Ticks}:{daysLeft}")));

    public async Task Consume(ConsumeContext<StudentParentGraceReminderEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;
        var key = ReminderKey(e.StudentId, e.GraceEndsAt, e.DaysLeft);
        var desc = $"StudentId={e.StudentId}, DaysLeft={e.DaysLeft}, GraceEndsAt={e.GraceEndsAt:O}";

        if (await ParentNotificationWriter.AlreadyProcessedAsync(_db, NotificationType, key, ct))
        {
            _logger.LogInformation("{Type} zaten işlenmiş ({Desc}); atlanıyor.", NotificationType, desc);
            return;
        }

        if (string.IsNullOrWhiteSpace(e.StudentUserId))
            throw new RecipientSubUnresolvedException($"{NotificationType} ({desc}): StudentUserId (Keycloak sub) boş.");

        var sub = e.StudentUserId;
        var culture = await _localeResolver.ResolveAsync(0, sub, ct);
        var text = _texts.Build(NotificationType, culture, e.DaysLeft);
        var data = new { studentId = e.StudentId, daysLeft = e.DaysLeft, graceEndsAt = e.GraceEndsAt, link = ParentRequiredLink };

        var notification = await ParentNotificationWriter.TryAddAsync(_db, new Notification
        {
            UserId = 0,
            UserKeycloakId = sub,
            Type = NotificationType,
            Title = text.Title,
            Body = text.Body,
            Data = JsonSerializer.Serialize(data),
            SourceEventId = key,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        }, ct);
        if (notification == null)
        {
            _logger.LogInformation("{Type} eşzamanlı duplicate ({Desc}); atlanıyor.", NotificationType, desc);
            return;
        }

        await ParentNotificationWriter.PushAsync(_hub, sub, notification, NotificationType, data, ct);
        _logger.LogInformation("{Type} işlendi. {Desc}, NotificationId={NotificationId}", NotificationType, desc, notification.Id);
    }
}
