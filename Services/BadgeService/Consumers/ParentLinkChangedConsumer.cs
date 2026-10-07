using System.Text.Json;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Services;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;

namespace BadgeService.Consumers;

/// <summary>
/// Veli–öğrenci bağlantısı bildirimleri (issue #423, epic #407 V5). exam API'nin bağlantı değişikliğiyle AYNI transaction'da
/// yazdığı <see cref="ParentLinkedEvent"/> / <see cref="ParentUnlinkedEvent"/>'i alır:
/// <list type="bullet">
/// <item>Linked: veliye "{öğrenci} sizi veli olarak onayladı" (<see cref="LinkedToParentType"/>), öğrenciye "Velinizle bağlantı kuruldu"
/// (<see cref="LinkedToStudentType"/>).</item>
/// <item>Unlinked: yalnız KARŞI TARAFA — öğrenci kopardıysa veliye (<see cref="UnlinkedToParentType"/>), veli kopardıysa öğrenciye
/// (<see cref="UnlinkedToStudentType"/>); koparanın kendisine bildirim yok.</item>
/// </list>
/// Tek consumer iki event'i handle eder (WorksheetAccessDecisionConsumer deseni).
///
/// Idempotency: bir event iki alıcıya gidebildiğinden her alıcı kendi Type'ıyla yazılır; <c>(Type, SourceEventId)</c> filtreli unique
/// index'i + önceden <c>Any</c> kontrolü + eşzamanlı yarışta 23505 yakalama. Alıcılardan biri (sub çözülemedi) düşerse tekrar
/// teslimde yalnız eksik olan yazılır; yazılmış olan için push tekrarlanmaz.
///
/// Hata yolu: beklenmeyen hata fırlatılır → MassTransit 3 kez immediate retry (bkz. <see cref="ParentLinkChangedConsumerDefinition"/>) →
/// hâlâ başarısızsa <c>badge-service_error</c> (dead-letter). Alıcı sub'ı ne event'te ne BadgeService verisinde varsa
/// <see cref="InvalidOperationException"/> → retry → dead-letter (sessiz kayıp yok; sub gelince error kuyruğundan oynatılır).
/// Duplicate hata değildir (loglanıp no-op). Bilinmeyen <c>RevokedByRole</c> yeniden denenemez bir bozuk mesajdır: uyarı loglanır, atlanır.
/// Bildirim tercihi altyapısı (sessize alma) henüz yok; mevcut tüm bildirimler gibi koşulsuz yazılır.
/// </summary>
public class ParentLinkChangedConsumer :
    IConsumer<ParentLinkedEvent>,
    IConsumer<ParentUnlinkedEvent>
{
    public const string LinkedToParentType = "ParentLinkedToParent";
    public const string LinkedToStudentType = "ParentLinkedToStudent";
    public const string UnlinkedToParentType = "ParentUnlinkedToParent";
    public const string UnlinkedToStudentType = "ParentUnlinkedToStudent";

    private readonly BadgeDbContext _db;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly IUserLocaleResolver _localeResolver;
    private readonly INotificationTextFactory _texts;
    private readonly ILogger<ParentLinkChangedConsumer> _logger;

    public ParentLinkChangedConsumer(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        IUserLocaleResolver localeResolver,
        INotificationTextFactory texts,
        ILogger<ParentLinkChangedConsumer> logger)
    {
        _db = db;
        _hub = hub;
        _localeResolver = localeResolver;
        _texts = texts;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ParentLinkedEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        // Veliye: "{öğrenci} sizi veli olarak onayladı" — veli panelinde ilgili çocuk açılır.
        await NotifyAsync(LinkedToParentType, e.EventId, e.LinkId, e.ParentUserId, e.ParentKeycloakId,
            e.StudentDisplayName, "notifications.common.defaultChild", studentId: e.StudentId, ct);
        // Öğrenciye: "Velinizle bağlantı kuruldu".
        await NotifyAsync(LinkedToStudentType, e.EventId, e.LinkId, e.StudentUserId, e.StudentKeycloakId,
            e.ParentDisplayName, "notifications.common.defaultParent", studentId: null, ct);
    }

    public async Task Consume(ConsumeContext<ParentUnlinkedEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        if (string.Equals(e.RevokedByRole, "Student", StringComparison.OrdinalIgnoreCase))
        {
            // Öğrenci kopardı → veliye haber (bağlantı artık yok: derin link çocuk seçmeden /parent'a gider).
            await NotifyAsync(UnlinkedToParentType, e.EventId, e.LinkId, e.ParentUserId, e.ParentKeycloakId,
                e.StudentDisplayName, "notifications.common.defaultChild", studentId: null, ct);
        }
        else if (string.Equals(e.RevokedByRole, "Parent", StringComparison.OrdinalIgnoreCase))
        {
            // Veli kopardı → öğrenciye haber.
            await NotifyAsync(UnlinkedToStudentType, e.EventId, e.LinkId, e.StudentUserId, e.StudentKeycloakId,
                e.ParentDisplayName, "notifications.common.defaultParent", studentId: null, ct);
        }
        else
        {
            _logger.LogWarning(
                "ParentUnlinked bilinmeyen RevokedByRole='{Role}' (EventId={EventId}, LinkId={LinkId}); bildirim üretilmedi.",
                e.RevokedByRole, e.EventId, e.LinkId);
        }
    }

    private async Task NotifyAsync(
        string type, Guid eventId, int linkId, int recipientUserId, string? eventSub,
        string? otherName, string defaultNameKey, int? studentId, CancellationToken ct)
    {
        if (await ParentNotificationWriter.AlreadyProcessedAsync(_db, type, eventId, ct))
        {
            _logger.LogInformation(
                "{Type} zaten işlenmiş (EventId={EventId}, LinkId={LinkId}); atlanıyor.", type, eventId, linkId);
            return;
        }

        var sub = await NotificationRecipientResolver.ResolveSubAsync(
            _db, recipientUserId, eventSub, $"{type} (EventId={eventId})", ct);
        var culture = await _localeResolver.ResolveAsync(recipientUserId, sub, ct);
        var cleanName = CommentNotificationSupport.CleanName(otherName);
        var name = string.IsNullOrWhiteSpace(cleanName) ? _texts.Resolve(defaultNameKey, culture) : cleanName;
        var text = _texts.Build(type, culture, name);

        // Veli tarafı derin link `/parent?child=<studentId>`; öğrenci tarafı için yönlendirme yok (UI yalnız bilinen type'ı yorumlar).
        var data = JsonSerializer.Serialize(new { linkId, studentId });

        var notification = await ParentNotificationWriter.TryAddAsync(_db, new Notification
        {
            UserId = recipientUserId,
            UserKeycloakId = sub,
            Type = type,
            Title = text.Title,
            Body = text.Body,
            Data = data,
            SourceEventId = eventId,
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        }, ct);
        if (notification == null)
        {
            _logger.LogInformation(
                "{Type} eşzamanlı duplicate (EventId={EventId}, LinkId={LinkId}); atlanıyor.", type, eventId, linkId);
            return;
        }

        await ParentNotificationWriter.PushAsync(_hub, sub, notification, type, new { linkId, studentId }, ct);

        _logger.LogInformation(
            "{Type} işlendi. EventId={EventId}, LinkId={LinkId}, RecipientUserId={UserId}, NotificationId={NotificationId}",
            type, eventId, linkId, recipientUserId, notification.Id);
    }
}
