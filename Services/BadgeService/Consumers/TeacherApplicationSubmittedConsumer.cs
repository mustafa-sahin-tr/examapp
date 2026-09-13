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
/// Bağımsız öğretmen onay akışının tüketici ucu (issue #94): bir kullanıcı bağımsız öğretmen
/// olarak başvurup admin onayı bekleyen (Pending) YENİ bir kayıt oluşturduğunda exam API'nin
/// yazdığı <see cref="TeacherApplicationSubmittedEvent"/>'i alır, bağlı olan tüm Admin'lere
/// in-app <see cref="Notification"/> satırı oluşturur ve SignalR ile rol bazlı
/// (<see cref="BadgeNotificationHub.AdminGroup"/>) push eder.
///
/// Hata yolu: beklenmeyen hata fırlatılır → MassTransit üç kez immediate retry (bkz.
/// <see cref="TeacherApplicationSubmittedConsumerDefinition"/>) → hâlâ başarısızsa mesaj
/// <c>badge-service_error</c> (dead-letter) kuyruğuna taşınır. Sessiz yutma yok.
/// Duplicate (aynı mesajın tekrar teslimi) hata değildir; loglanıp no-op ile geçilir.
/// </summary>
public class TeacherApplicationSubmittedConsumer : IConsumer<TeacherApplicationSubmittedEvent>
{
    public const string NotificationType = "TeacherApplicationSubmitted";

    private readonly BadgeDbContext _db;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly INotificationTextFactory _texts;
    private readonly ILogger<TeacherApplicationSubmittedConsumer> _logger;

    /// <summary>
    /// <paramref name="texts"/> opsiyonel: DI dışında oluşturan birim testler
    /// (<c>new TeacherApplicationSubmittedConsumer(db, hub, logger)</c>) derlenmeye devam etsin
    /// diye. Üretimde <c>Program.cs</c> DI ile kayıtlı gerçek implementasyonu verir.
    /// </summary>
    public TeacherApplicationSubmittedConsumer(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        ILogger<TeacherApplicationSubmittedConsumer> logger,
        INotificationTextFactory? texts = null)
    {
        _db = db;
        _hub = hub;
        _texts = texts ?? FallbackNotificationTextFactory.Instance;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<TeacherApplicationSubmittedEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        // Idempotency: bu başvuru için Admin bildirimi zaten üretildiyse çık.
        var exists = await _db.Notifications
            .AnyAsync(n => n.Type == NotificationType && n.SourceTeacherApplicationId == e.TeacherId, ct);
        if (exists)
        {
            _logger.LogInformation(
                "TeacherApplicationSubmitted zaten işlenmiş (TeacherId={TeacherId}); atlanıyor.", e.TeacherId);
            return;
        }

        // Admin bildirimleri belirli bir kullanıcıya değil role bağlıdır; hedef admin'in dili
        // bilinmiyor (WorksheetAccessRequestedConsumer'daki gibi tek hedef yok) — platform
        // varsayılan diline (tr) kilitlenir (issue #185 karar #5).
        var culture = CultureInfo.GetCultureInfo(SupportedLocales.DefaultCultureName);
        var applicantName = string.IsNullOrWhiteSpace(e.ApplicantName)
            ? _texts.Resolve("notifications.common.defaultApplicant", culture)
            : e.ApplicantName;
        var text = _texts.Build(NotificationType, culture, applicantName);

        // UserId/UserKeycloakId burada anlamsız (tek hedef yok), boş geçilir.
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
                userId = e.UserId
            }),
            SourceTeacherApplicationId = e.TeacherId,
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
                "TeacherApplicationSubmitted eşzamanlı duplicate (TeacherId={TeacherId}); atlanıyor.", e.TeacherId);
            return;
        }

        await _hub.Clients.Group(BadgeNotificationHub.AdminGroup).SendAsync("TeacherApplicationSubmitted", new
        {
            notificationId = notification.Id,
            teacherId = e.TeacherId,
            userId = e.UserId,
            applicantName = applicantName,
            title = notification.Title,
            body = notification.Body
        }, ct);

        _logger.LogInformation(
            "TeacherApplicationSubmitted işlendi. TeacherId={TeacherId}, UserId={UserId}, NotificationId={NotificationId}",
            e.TeacherId, e.UserId, notification.Id);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };
}
