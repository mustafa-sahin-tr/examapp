using System.Net;
using System.Text.Json;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Services;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Security;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Consumers;

/// <summary>Bozuk/eksik davet event'i (ör. NewParentActivation'da ActivationUrl yok, bilinmeyen Kind): deterministik, retry anlamsız, doğrudan error kuyruğu.</summary>
public sealed class InvalidParentInvitationException(string message) : InvalidOperationException(message);

/// <summary>
/// Issue #440: öğrenci kaydında veli e-postası verildiğinde exam API'nin davetle AYNI transaction'da yazdığı
/// <see cref="ParentInvitationRequestedEvent"/>'i alır ve <see cref="IEmailSender"/> ile e-posta gönderir (Keycloak execute-actions-email YOK).
/// <list type="bullet">
/// <item><c>NewParentActivation</c>: aktivasyon bağlantılı e-posta (Türkçe, "Hedef Okul", 7 gün geçerli). Bağlantı token içerir: bu consumer onu ASLA loglamaz
/// (yalnız <see cref="LoggingEmailSender"/> Development'ta yazar).</item>
/// <item><c>ExistingParentApproval</c>: "çocuğunuz sizi veli olarak ekledi" e-postası + veliye uygulama içi bildirim
/// (<see cref="InvitationReceivedType"/>, SignalR <c>ParentNotification</c>, alıcı = ParentUserId Keycloak sub).</item>
/// </list>
///
/// Idempotency (anahtar = <see cref="DedupeId"/>: EventId, boşsa InvitationId; yeniden gönderim yeni EventId taşır): uygulama içi bildirim <c>(Type, SourceEventId)</c> unique index'iyle tekilleşir. E-posta için
/// <see cref="NotificationEventLog"/> <c>(Type="ParentInvitationEmail", EventId)</c> PK'sı: gönderimden ÖNCE kontrol,
/// başarılı gönderimden SONRA kayıt. Gönderim başarılı ama kayıt başarısız olursa nadiren yinelenen e-posta olabilir (at-least-once;
/// harici sağlayıcıda exactly-once mümkün değil), kabul edilen sınır.
/// Hata yolu: sağlayıcı/DB hatası, 3 immediate retry, sonra <c>badge-service_error</c> (dead-letter, korelasyon: InvitationId loglanır).
/// Sub çözülemeyen / bozuk event retry'sız doğrudan error kuyruğuna. Hiçbir hata sessizce yutulmaz.
/// </summary>
public class ParentInvitationRequestedConsumer : IConsumer<ParentInvitationRequestedEvent>
{
    public const string NewParentActivationKind = "NewParentActivation";
    public const string ExistingParentApprovalKind = "ExistingParentApproval";
    public const string InvitationReceivedType = "ParentInvitationReceived";
    public const string EmailLogType = "ParentInvitationEmail";
    public const string Brand = "Hedef Okul";

    private readonly BadgeDbContext _db;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly IUserLocaleResolver _localeResolver;
    private readonly INotificationTextFactory _texts;
    private readonly IEmailSender _email;
    private readonly ActivationLinkProtector _protector;
    private readonly ILogger<ParentInvitationRequestedConsumer> _logger;

    public ParentInvitationRequestedConsumer(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        IUserLocaleResolver localeResolver,
        INotificationTextFactory texts,
        IEmailSender email,
        ActivationLinkProtector protector,
        ILogger<ParentInvitationRequestedConsumer> logger)
    {
        _db = db;
        _hub = hub;
        _localeResolver = localeResolver;
        _texts = texts;
        _email = email;
        _protector = protector;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ParentInvitationRequestedEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        switch (e.Kind)
        {
            case NewParentActivationKind:
                if (string.IsNullOrWhiteSpace(e.ProtectedActivationUrl))
                    throw new InvalidParentInvitationException(
                        $"ParentInvitationRequested {e.InvitationId} (EventId={DedupeId(e)}): NewParentActivation için ProtectedActivationUrl yok.");
                break;
            case ExistingParentApprovalKind:
                break;
            default:
                throw new InvalidParentInvitationException(
                    $"ParentInvitationRequested {e.InvitationId}: bilinmeyen Kind '{e.Kind}'.");
        }

        if (string.IsNullOrWhiteSpace(e.ParentEmail))
            throw new InvalidParentInvitationException($"ParentInvitationRequested {e.InvitationId}: ParentEmail boş.");

        // Şifre çözme başarısızlığı (yanlış anahtar / bozulmuş mesaj) deterministik: retry anlamsız, error kuyruğu.
        string? activationUrl = null;
        if (e.Kind == NewParentActivationKind)
        {
            try
            {
                activationUrl = _protector.Unprotect(e.ProtectedActivationUrl!);
            }
            catch (System.Security.Cryptography.CryptographicException ex)
            {
                throw new InvalidParentInvitationException(
                    $"ParentInvitationRequested {e.InvitationId} (EventId={DedupeId(e)}): aktivasyon bağlantısı çözülemedi ({ex.GetType().Name}).");
            }
        }

        if (e.Kind == ExistingParentApprovalKind)
            await NotifyExistingParentAsync(e, ct);

        await SendEmailOnceAsync(e, activationUrl, ct);
    }

    /// <summary>
    /// Tekilleştirme anahtarı: gönderim başına <c>EventId</c> (yeniden gönderim aynı InvitationId'yi yeni EventId ile taşır; InvitationId'ye
    /// göre dedupe yeni bağlantıyı yutardı). Eski mesajlarda EventId boşsa InvitationId'ye düşer.
    /// </summary>
    internal static Guid DedupeId(ParentInvitationRequestedEvent e) => e.EventId != Guid.Empty ? e.EventId : e.InvitationId;

    private async Task NotifyExistingParentAsync(ParentInvitationRequestedEvent e, CancellationToken ct)
    {
        if (await ParentNotificationWriter.AlreadyProcessedAsync(_db, InvitationReceivedType, DedupeId(e), ct))
        {
            _logger.LogInformation("{Type} zaten işlenmiş (InvitationId={InvitationId}); atlanıyor.", InvitationReceivedType, e.InvitationId);
            return;
        }

        if (string.IsNullOrWhiteSpace(e.ParentUserId))
            throw new RecipientSubUnresolvedException(
                $"{InvitationReceivedType} (InvitationId={e.InvitationId}): ParentUserId (Keycloak sub) boş.");

        var sub = e.ParentUserId;
        var culture = await _localeResolver.ResolveAsync(0, sub, ct);
        var child = CommentNotificationSupport.CleanName(e.StudentFirstName);
        if (string.IsNullOrWhiteSpace(child))
            child = _texts.Resolve("notifications.common.defaultChild", culture);
        var text = _texts.Build(InvitationReceivedType, culture, child);
        var data = new { invitationId = e.InvitationId, studentId = e.StudentId, expiresAt = e.ExpiresAt };

        var notification = await ParentNotificationWriter.TryAddAsync(_db, new Notification
        {
            UserId = 0,
            UserKeycloakId = sub,
            Type = InvitationReceivedType,
            Title = text.Title,
            Body = text.Body,
            Data = JsonSerializer.Serialize(data),
            SourceEventId = DedupeId(e),
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        }, ct);
        if (notification == null)
        {
            _logger.LogInformation("{Type} eşzamanlı duplicate (InvitationId={InvitationId}); atlanıyor.", InvitationReceivedType, e.InvitationId);
            return;
        }

        await ParentNotificationWriter.PushAsync(_hub, sub, notification, InvitationReceivedType, data, ct);
        _logger.LogInformation(
            "{Type} işlendi. InvitationId={InvitationId}, NotificationId={NotificationId}", InvitationReceivedType, e.InvitationId, notification.Id);
    }

    private async Task SendEmailOnceAsync(ParentInvitationRequestedEvent e, string? activationUrl, CancellationToken ct)
    {
        if (await _db.NotificationEventLogs.AsNoTracking()
                .AnyAsync(l => l.Type == EmailLogType && l.EventId == DedupeId(e), ct))
        {
            _logger.LogInformation("Davet e-postası zaten gönderilmiş (InvitationId={InvitationId}); atlanıyor.", e.InvitationId);
            return;
        }

        await _email.SendAsync(BuildMessage(e, activationUrl), ct);

        _db.NotificationEventLogs.Add(new NotificationEventLog { Type = EmailLogType, EventId = DedupeId(e), NotificationId = 0 });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            // Eşzamanlı ikinci teslim aynı anda gönderdi ve kaydetti; kayıt zaten var (at-least-once sınırı).
            _logger.LogInformation("Davet e-postası kaydı zaten var (InvitationId={InvitationId}).", e.InvitationId);
        }

        // Alıcı e-postası ve URL bilerek loglanmaz (PII/token).
        _logger.LogInformation("Davet e-postası gönderildi. InvitationId={InvitationId}, Kind={Kind}", e.InvitationId, e.Kind);
    }

    internal static EmailMessage BuildMessage(ParentInvitationRequestedEvent e, string? activationUrl)
    {
        var name = string.IsNullOrWhiteSpace(e.ParentFullName) ? "Merhaba" : $"Merhaba {e.ParentFullName.Trim()}";
        var child = string.IsNullOrWhiteSpace(e.StudentFirstName) ? "Çocuğunuz" : e.StudentFirstName.Trim();

        if (e.Kind == NewParentActivationKind)
        {
            var url = activationUrl!;
            var text =
                $"{name},\n\n{child}, {Brand} platformunda sizi veli olarak ekledi. Hesabınızı etkinleştirmek ve şifrenizi belirlemek için aşağıdaki bağlantıyı kullanın:\n\n{url}\n\n" +
                "Bu bağlantı 7 gün geçerlidir; süre dolarsa çocuğunuzdan yeni davet göndermesini isteyin.\n\n" +
                $"Bu e-postayı beklemiyorsanız dikkate almayabilirsiniz.\n\n{Brand}";
            var html =
                $"<p>{WebUtility.HtmlEncode(name)},</p>" +
                $"<p><strong>{WebUtility.HtmlEncode(child)}</strong>, {Brand} platformunda sizi veli olarak ekledi. Hesabınızı etkinleştirmek ve şifrenizi belirlemek için aşağıdaki bağlantıyı kullanın:</p>" +
                $"<p><a href=\"{WebUtility.HtmlEncode(url)}\">Hesabımı etkinleştir</a></p>" +
                "<p>Bu bağlantı 7 gün geçerlidir; süre dolarsa çocuğunuzdan yeni davet göndermesini isteyin.</p>" +
                $"<p>Bu e-postayı beklemiyorsanız dikkate almayabilirsiniz.</p><p>{Brand}</p>";
            return new EmailMessage(e.ParentEmail, $"{Brand} - Veli hesabınızı etkinleştirin", text, html);
        }

        var textExisting =
            $"{name},\n\nÇocuğunuz {child} sizi {Brand} platformunda veli olarak ekledi. Giriş yapıp Çocuklarım sayfasından bağlantıyı onaylayın. " +
            "Davet 7 gün geçerlidir.\n\n" + Brand;
        var htmlExisting =
            $"<p>{WebUtility.HtmlEncode(name)},</p>" +
            $"<p>Çocuğunuz <strong>{WebUtility.HtmlEncode(child)}</strong> sizi {Brand} platformunda veli olarak ekledi. Giriş yapıp <em>Çocuklarım</em> sayfasından bağlantıyı onaylayın.</p>" +
            $"<p>Davet 7 gün geçerlidir.</p><p>{Brand}</p>";
        return new EmailMessage(e.ParentEmail, $"{Brand} - Çocuğunuz sizi veli olarak ekledi", textExisting, htmlExisting);
    }
}
