using System.Globalization;
using System.Text.Json;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Services;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Localization;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Consumers;

/// <summary>
/// Öğretmen başvurusu karar akışının tüketici ucu (issue #157): admin bir bağımsız öğretmen/okul
/// bağlantısı başvurusunu onayladığında ya da reddettiğinde exam API'nin yazdığı
/// <see cref="TeacherApplicationDecidedEvent"/>'i alır, başvuru sahibine in-app <see cref="Notification"/>
/// satırı oluşturur ve SignalR ile push eder.
///
/// Güvenlik kararı (issue #157): bildirim metninde ret gerekçesi ve admin kimliği YOK — sadece
/// "onaylandı"/"reddedildi" sabit metni. Gerekçeyi öğretmen kendi başvuru sayfasından ayrıca sorar.
///
/// Hata yolu: beklenmeyen hata fırlatılır → MassTransit üç kez immediate retry (bkz.
/// <see cref="TeacherApplicationDecisionConsumerDefinition"/>) → hâlâ başarısızsa mesaj
/// <c>badge-service_error</c> (dead-letter) kuyruğuna taşınır. Sessiz yutma yok.
/// Duplicate (aynı mesajın tekrar teslimi) hata değildir; loglanıp no-op ile geçilir.
/// </summary>
public class TeacherApplicationDecisionConsumer : IConsumer<TeacherApplicationDecidedEvent>
{
    public const string ApprovedType = "TeacherApplicationApproved";
    public const string RejectedType = "TeacherApplicationRejected";

    private readonly BadgeDbContext _db;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly IUserLocaleResolver _localeResolver;
    private readonly INotificationTextFactory _texts;
    private readonly ILogger<TeacherApplicationDecisionConsumer> _logger;

    public TeacherApplicationDecisionConsumer(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        IUserLocaleResolver localeResolver,
        INotificationTextFactory texts,
        ILogger<TeacherApplicationDecisionConsumer> logger)
    {
        _db = db;
        _hub = hub;
        _localeResolver = localeResolver;
        _texts = texts;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TeacherApplicationDecidedEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;
        var type = e.Approved ? ApprovedType : RejectedType;

        // Idempotency (issue #157 review): TeacherId+Type tek başına yetersiz — aynı öğretmen zaman
        // içinde birden fazla karara konu olabilir (ör. red sonrası yeni okul talebi onaylanır). Dedup
        // event'in kendi Guid kimliğiyle yapılır (Type, SourceEventId) — BadgeDbContext'teki filtreli
        // unique index.
        var exists = await _db.Notifications
            .AnyAsync(n => n.Type == type && n.SourceEventId == e.EventId, ct);
        if (exists)
        {
            _logger.LogInformation(
                "TeacherApplicationDecided zaten işlenmiş (EventId={EventId}, TeacherId={TeacherId}, Type={Type}); atlanıyor.",
                e.EventId, e.TeacherId, type);
            return;
        }

        if (string.IsNullOrWhiteSpace(e.TargetKeycloakId))
        {
            // Üretici sub çözemediyse event hiç yazılmaz (bkz. TeacherApprovalService); burası yalnızca
            // savunma amaçlı — yine de idempotency + log ile no-op.
            _logger.LogWarning(
                "TeacherApplicationDecided: TargetKeycloakId boş (TeacherId={TeacherId}); atlanıyor.",
                e.TeacherId);
            return;
        }

        var culture = await _localeResolver.ResolveAsync(0, e.TargetKeycloakId, ct);
        var text = _texts.Build(type, culture);

        var notification = new Notification
        {
            UserId = 0,
            UserKeycloakId = e.TargetKeycloakId,
            Type = type,
            Title = text.Title,
            Body = text.Body,
            Data = JsonSerializer.Serialize(new
            {
                teacherId = e.TeacherId,
                approved = e.Approved,
                isIndependentTutor = e.IsIndependentTutor
            }),
            // Referans amaçlı (BadgeDbContext'teki filtreli unique index bu tipler için Type'a da
            // bağlı olduğundan burada tekillik kurmaz — bkz. yorum orada).
            SourceTeacherApplicationId = e.TeacherId,
            SourceEventId = e.EventId,
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
                "TeacherApplicationDecided eşzamanlı duplicate (EventId={EventId}, TeacherId={TeacherId}, Type={Type}); atlanıyor.",
                e.EventId, e.TeacherId, type);
            return;
        }

        await _hub.Clients.User(e.TargetKeycloakId).SendAsync("TeacherApplicationDecided", new
        {
            notificationId = notification.Id,
            teacherId = e.TeacherId,
            approved = e.Approved,
            isIndependentTutor = e.IsIndependentTutor,
            title = notification.Title,
            body = notification.Body
        }, ct);

        _logger.LogInformation(
            "TeacherApplicationDecided işlendi. TeacherId={TeacherId}, Type={Type}, NotificationId={NotificationId}",
            e.TeacherId, type, notification.Id);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };
}
