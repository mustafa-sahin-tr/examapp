using System.Globalization;
using System.Text.Json;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Services;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Consumers;

/// <summary>
/// Issue #1116: süren satın almanın yenileme fiyatı artacak. Exam API'nin <c>PlanPriceChangeJob</c>'ının ödeyen başına yazdığı
/// <see cref="PlanPriceChangeNoticeEvent"/>'i alır: in-app <see cref="Notification"/> (velide <see cref="ToParentType"/>, çocuğun
/// plan sekmesine; kendi planında <see cref="ToSelfType"/>) + aynı metinle e-posta. Dil alıcının tercihi.
///
/// Idempotency: bildirim <c>(Type, SourceEventId)</c>; e-posta <see cref="NotificationEventLog"/> <c>(EmailLogType, EventId)</c>,
/// gönderimden önce kontrol, sonra kayıt (at-least-once: eşzamanlı ikinci teslimde nadiren iki e-posta). E-posta adresi boşsa
/// yalnız in-app.
/// </summary>
public class PlanPriceChangeNoticeConsumer : IConsumer<PlanPriceChangeNoticeEvent>
{
    public const string ToParentType = "PlanPriceChangeToParent";
    public const string ToSelfType = "PlanPriceChange";
    public const string EmailLogType = "PlanPriceChangeEmail";

    private readonly BadgeDbContext _db;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly IUserLocaleResolver _localeResolver;
    private readonly INotificationTextFactory _texts;
    private readonly IEmailSender _email;
    private readonly ILogger<PlanPriceChangeNoticeConsumer> _logger;

    public PlanPriceChangeNoticeConsumer(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        IUserLocaleResolver localeResolver,
        INotificationTextFactory texts,
        IEmailSender email,
        ILogger<PlanPriceChangeNoticeConsumer> logger)
    {
        _db = db;
        _hub = hub;
        _localeResolver = localeResolver;
        _texts = texts;
        _email = email;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<PlanPriceChangeNoticeEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;
        var type = e.StudentId is null ? ToSelfType : ToParentType;

        var sub = await NotificationRecipientResolver.ResolveSubAsync(
            _db, e.RecipientUserId, e.RecipientKeycloakId, $"PlanPriceChangeNotice (EventId={e.EventId})", ct);
        var culture = await _localeResolver.ResolveAsync(e.RecipientUserId, sub, ct);
        var text = BuildText(e, type, culture);

        if (await ParentNotificationWriter.AlreadyProcessedAsync(_db, type, e.EventId, ct))
        {
            _logger.LogInformation("PlanPriceChangeNotice bildirimi zaten var (EventId={EventId}); yalnız e-posta denenir.", e.EventId);
        }
        else
        {
            var data = new { studentId = e.StudentId, assignmentId = e.AssignmentId };
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
            if (notification != null)
                await ParentNotificationWriter.PushAsync(_hub, sub, notification, type, data, ct);
        }

        await SendEmailOnceAsync(e, text, ct);

        _logger.LogInformation(
            "PlanPriceChangeNotice işlendi. EventId={EventId}, Type={Type}, AssignmentId={AssignmentId}, RecipientUserId={UserId}",
            e.EventId, type, e.AssignmentId, e.RecipientUserId);
    }

    private (string Title, string Body) BuildText(PlanPriceChangeNoticeEvent e, string type, CultureInfo culture)
    {
        var cleanChild = CommentNotificationSupport.CleanName(e.StudentDisplayName);
        var child = string.IsNullOrWhiteSpace(cleanChild) ? _texts.Resolve("notifications.common.defaultChild", culture) : cleanChild;
        var from = ActivityCompletionStats.ToIstanbulDate(e.EffectiveFromUtc).ToString("d MMMM yyyy", culture);
        var renews = ActivityCompletionStats.ToIstanbulDate(e.RenewsAtUtc).ToString("d MMMM yyyy", culture);
        // {0} plan, {1} bugünkü fiyat, {2} yeni fiyat, {3} yürürlük, {4} yenileme tarihi, {5} velide çocuğun adı.
        var built = _texts.Build(type, culture, e.PlanName, Money(e.CurrentPrice, e.Currency, culture),
            Money(e.NewPrice, e.Currency, culture), from, renews, child);
        return (built.Title, built.Body);
    }

    private static string Money(decimal amount, string currency, CultureInfo culture) =>
        $"{amount.ToString(amount == decimal.Truncate(amount) ? "N0" : "N2", culture)} {currency}";

    private async Task SendEmailOnceAsync(PlanPriceChangeNoticeEvent e, (string Title, string Body) text, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(e.RecipientEmail))
        {
            _logger.LogInformation("PlanPriceChangeNotice alıcı e-postası yok (EventId={EventId}); yalnız in-app.", e.EventId);
            return;
        }
        if (await _db.NotificationEventLogs.AsNoTracking().AnyAsync(l => l.Type == EmailLogType && l.EventId == e.EventId, ct))
            return;

        await _email.SendAsync(BuildMessage(e.RecipientEmail, text.Title, text.Body), ct);

        _db.NotificationEventLogs.Add(new NotificationEventLog { Type = EmailLogType, EventId = e.EventId, NotificationId = 0 });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            _logger.LogInformation("PlanPriceChangeNotice e-posta kaydı zaten var (EventId={EventId}).", e.EventId);
        }
    }

    internal static EmailMessage BuildMessage(string to, string title, string body)
    {
        var brand = ParentInvitationRequestedConsumer.Brand;
        var html = $"<p>{System.Net.WebUtility.HtmlEncode(body)}</p><p>{brand}</p>";
        return new EmailMessage(to, $"{brand} - {title}", $"{body}\n\n{brand}", html);
    }
}
