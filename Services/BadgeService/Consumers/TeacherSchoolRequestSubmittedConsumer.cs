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
/// Okul bağlantısı talebi admin bildiriminin tüketici ucu (issue #277, madde 1): exam API'nin
/// yazdığı <see cref="TeacherSchoolRequestSubmittedEvent"/>'i alır, bağlı olan tüm Admin'lere
/// in-app <see cref="Notification"/> satırı oluşturur ve SignalR ile rol bazlı
/// (<see cref="BadgeNotificationHub.AdminGroup"/>) push eder — aynı desen
/// <see cref="TeacherApplicationSubmittedConsumer"/> ile (issue #94).
///
/// Idempotency: <see cref="TeacherApplicationSubmittedConsumer"/>'dan FARKLI — TeacherId burada
/// tekillik için yetersiz (bir öğretmen zamanla birden fazla okul talebi açabilir), dolayısıyla
/// dedup event'in kendi Guid kimliğiyle (<c>Type</c>, <see cref="Notification.SourceEventId"/>)
/// yapılır; BadgeDbContext'teki filtreli unique index (issue #157'den beri var) tekrar kullanılır.
///
/// Hata yolu: beklenmeyen hata fırlatılır → MassTransit üç kez immediate retry (bkz.
/// <see cref="TeacherSchoolRequestSubmittedConsumerDefinition"/>) → hâlâ başarısızsa mesaj
/// <c>badge-service_error</c> (dead-letter) kuyruğuna taşınır. Sessiz yutma yok.
/// Duplicate (aynı mesajın tekrar teslimi) hata değildir; loglanıp no-op ile geçilir.
/// </summary>
public class TeacherSchoolRequestSubmittedConsumer : IConsumer<TeacherSchoolRequestSubmittedEvent>
{
    public const string NotificationType = "TeacherSchoolRequestSubmitted";

    private readonly BadgeDbContext _db;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly INotificationTextFactory _texts;
    private readonly ILogger<TeacherSchoolRequestSubmittedConsumer> _logger;

    public TeacherSchoolRequestSubmittedConsumer(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        INotificationTextFactory texts,
        ILogger<TeacherSchoolRequestSubmittedConsumer> logger)
    {
        _db = db;
        _hub = hub;
        _texts = texts ?? throw new ArgumentNullException(nameof(texts));
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TeacherSchoolRequestSubmittedEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        // Idempotency: bu talep için üretilen event zaten işlendiyse çık (Type + SourceEventId).
        var exists = await _db.Notifications
            .AnyAsync(n => n.Type == NotificationType && n.SourceEventId == e.EventId, ct);
        if (exists)
        {
            _logger.LogInformation(
                "TeacherSchoolRequestSubmitted zaten işlenmiş (EventId={EventId}, TeacherId={TeacherId}); atlanıyor.",
                e.EventId, e.TeacherId);
            return;
        }

        // Admin bildirimleri belirli bir kullanıcıya değil role bağlıdır; hedef admin'in dili
        // bilinmiyor — platform varsayılan diline (tr) kilitlenir (TeacherApplicationSubmittedConsumer
        // ile aynı karar, issue #185 karar #5).
        var culture = CultureInfo.GetCultureInfo(SupportedLocales.DefaultCultureName);
        var applicantName = string.IsNullOrWhiteSpace(e.ApplicantName)
            ? _texts.Resolve("notifications.common.defaultApplicant", culture)
            : e.ApplicantName;
        var schoolName = string.IsNullOrWhiteSpace(e.RequestedSchoolName)
            ? _texts.Resolve("notifications.common.defaultSchool", culture)
            : e.RequestedSchoolName;
        var text = _texts.Build(NotificationType, culture, applicantName, schoolName);

        // UserId/UserKeycloakId burada anlamsız (tek hedef yok, admin grubuna gider), boş geçilir.
        var notification = new Notification
        {
            UserId = 0,
            UserKeycloakId = null,
            Type = NotificationType,
            Title = text.Title,
            Body = text.Body,
            Data = JsonSerializer.Serialize(new
            {
                teacherId = e.TeacherId,
                userId = e.UserId,
                requestedSchoolId = e.RequestedSchoolId,
                isNewRegistration = e.IsNewRegistration
            }),
            // Referans amaçlı dolu — bu Type için tekillik SourceEventId'ye bağlı, SourceTeacherApplicationId'e değil
            // (aynı öğretmen birden fazla okul talebi açabilir).
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
                "TeacherSchoolRequestSubmitted eşzamanlı duplicate (EventId={EventId}, TeacherId={TeacherId}); atlanıyor.",
                e.EventId, e.TeacherId);
            return;
        }

        await _hub.Clients.Group(BadgeNotificationHub.AdminGroup).SendAsync("TeacherSchoolRequestSubmitted", new
        {
            notificationId = notification.Id,
            teacherId = e.TeacherId,
            userId = e.UserId,
            requestedSchoolId = e.RequestedSchoolId,
            applicantName,
            schoolName,
            title = notification.Title,
            body = notification.Body
        }, ct);

        _logger.LogInformation(
            "TeacherSchoolRequestSubmitted işlendi. EventId={EventId}, TeacherId={TeacherId}, UserId={UserId}, NotificationId={NotificationId}",
            e.EventId, e.TeacherId, e.UserId, notification.Id);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };
}

/// <summary>
/// Retry politikasını SADECE <see cref="TeacherSchoolRequestSubmittedConsumer"/>'a scope'lar.
/// Beklenmeyen hata: 3 kez immediate retry → hâlâ başarısızsa mesaj
/// <c>badge-service_error</c> (dead-letter) kuyruğuna taşınır.
/// </summary>
public class TeacherSchoolRequestSubmittedConsumerDefinition : ConsumerDefinition<TeacherSchoolRequestSubmittedConsumer>
{
    protected override void ConfigureConsumer(
        IReceiveEndpointConfigurator endpointConfigurator,
        IConsumerConfigurator<TeacherSchoolRequestSubmittedConsumer> consumerConfigurator,
        IRegistrationContext context)
    {
        consumerConfigurator.UseMessageRetry(r => r.Immediate(3));
    }
}
