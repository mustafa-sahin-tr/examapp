using System.Globalization;
using System.Text;
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
/// Issue #1112: haftalık veli raporu. Exam API'nin <c>ParentWeeklyReportJob</c>'ının veli başına yazdığı
/// <see cref="ParentWeeklyReportEvent"/>'i alır: in-app <see cref="Notification"/> (<see cref="Type"/>, çocuğun Rapor sekmesine; kısa
/// özet + öneri) + raporun tamamıyla e-posta (veli kapattıysa adres boş gelir, yalnız in-app). Dil alıcının tercihi.
///
/// Idempotency: bildirim <c>(Type, SourceEventId)</c>; e-posta <see cref="NotificationEventLog"/> <c>(EmailLogType, EventId)</c>,
/// gönderimden önce kontrol, sonra kayıt (at-least-once: eşzamanlı ikinci teslimde nadiren iki e-posta).
/// </summary>
public class ParentWeeklyReportConsumer : IConsumer<ParentWeeklyReportEvent>
{
    public const string Type = "ParentWeeklyReport";
    public const string EmailLogType = "ParentWeeklyReportEmail";

    private readonly BadgeDbContext _db;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly IUserLocaleResolver _localeResolver;
    private readonly INotificationTextFactory _texts;
    private readonly IEmailSender _email;
    private readonly ILogger<ParentWeeklyReportConsumer> _logger;

    public ParentWeeklyReportConsumer(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        IUserLocaleResolver localeResolver,
        INotificationTextFactory texts,
        IEmailSender email,
        ILogger<ParentWeeklyReportConsumer> logger)
    {
        _db = db;
        _hub = hub;
        _localeResolver = localeResolver;
        _texts = texts;
        _email = email;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<ParentWeeklyReportEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        var sub = await NotificationRecipientResolver.ResolveSubAsync(
            _db, e.RecipientUserId, e.RecipientKeycloakId, $"ParentWeeklyReport (EventId={e.EventId})", ct);
        var culture = await _localeResolver.ResolveAsync(e.RecipientUserId, sub, ct);
        var text = BuildText(e, culture);

        if (await ParentNotificationWriter.AlreadyProcessedAsync(_db, Type, e.EventId, ct))
        {
            _logger.LogInformation("ParentWeeklyReport bildirimi zaten var (EventId={EventId}); yalnız e-posta denenir.", e.EventId);
        }
        else
        {
            var data = new { studentId = e.StudentId, weekStart = DateOnly.FromDateTime(e.WeekStart).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) };
            var notification = await ParentNotificationWriter.TryAddAsync(_db, new Notification
            {
                UserId = e.RecipientUserId,
                UserKeycloakId = sub,
                Type = Type,
                Title = text.Title,
                Body = text.Body,
                Data = JsonSerializer.Serialize(data),
                SourceEventId = e.EventId,
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            }, ct);
            if (notification != null)
                await ParentNotificationWriter.PushAsync(_hub, sub, notification, Type, data, ct);
        }

        await SendEmailOnceAsync(e, text, culture, ct);

        _logger.LogInformation(
            "ParentWeeklyReport işlendi. EventId={EventId}, StudentId={StudentId}, RecipientUserId={UserId}",
            e.EventId, e.StudentId, e.RecipientUserId);
    }

    private string ChildName(ParentWeeklyReportEvent e, CultureInfo culture)
    {
        var clean = CommentNotificationSupport.CleanName(e.StudentDisplayName);
        return string.IsNullOrWhiteSpace(clean) ? _texts.Resolve("notifications.common.defaultChild", culture) : clean;
    }

    internal (string Title, string Body) BuildText(ParentWeeklyReportEvent e, CultureInfo culture)
    {
        var week = e.WeekStart.ToString("d MMMM", culture);
        var accuracy = e.QuestionsSolved == 0 ? 0 : (int)Math.Round(e.Correct * 100m / e.QuestionsSolved, MidpointRounding.AwayFromZero);
        // {0} çocuk, {1} haftanın Pazartesi'si, {2} çözülen, {3} doğru yüzdesi, {4} aktif gün, {5} öneri cümlesi.
        var built = _texts.Build(Type, culture, ChildName(e, culture), week, e.QuestionsSolved, accuracy, e.ActiveDays,
            Suggestion(e, culture));
        return (built.Title, built.Body);
    }

    private string Suggestion(ParentWeeklyReportEvent e, CultureInfo culture)
    {
        var key = e.Suggestion is "noActivity" or "overdue" or "reviewTopic" or "moreDays" or "keepGoing" ? e.Suggestion : "keepGoing";
        return _texts.Resolve($"notifications.weeklyReport.suggestion.{key}", culture, e.SuggestionTopic ?? string.Empty);
    }

    /// <summary>E-posta gövdesi: bildirimin özeti + süre / puan, ödevler, dersler, düşen konular, denemeler, rozetler.</summary>
    internal string BuildEmailBody(ParentWeeklyReportEvent e, string summary, CultureInfo culture)
    {
        string R(string key, params object[] args) => _texts.Resolve($"notifications.weeklyReport.{key}", culture, args);

        var sb = new StringBuilder();
        sb.AppendLine(summary).AppendLine();
        sb.AppendLine(R("studyTime", e.StudyMinutes, e.Points));
        sb.AppendLine(R("assignments", e.AssignmentsCompleted, e.AssignmentsOverdue));
        if (e.Subjects.Count > 0)
        {
            sb.AppendLine().AppendLine(R("subjectsTitle"));
            foreach (var s in e.Subjects)
                sb.AppendLine("- " + R("subjectLine", s.Name, s.Answered, s.AccuracyPercent));
        }
        if (e.DroppedTopics.Count > 0)
        {
            sb.AppendLine().AppendLine(R("droppedTitle"));
            foreach (var t in e.DroppedTopics)
                sb.AppendLine("- " + R("droppedLine", t.Name, t.PreviousAccuracyPercent, t.AccuracyPercent));
        }
        if (e.MockExams.Count > 0)
        {
            sb.AppendLine().AppendLine(R("mockExamsTitle"));
            foreach (var m in e.MockExams)
            {
                var net = m.Net.ToString(m.Net == decimal.Truncate(m.Net) ? "N0" : "N2", culture);
                sb.AppendLine("- " + (m is { Rank: { } rank, Takers: { } takers }
                    ? R("mockExamRanked", m.Name, net, rank, takers)
                    : R("mockExamLine", m.Name, net)));
            }
        }
        if (e.Badges.Count > 0)
            sb.AppendLine().AppendLine(R("badges", string.Join(", ", e.Badges)));
        sb.AppendLine().Append(R("panelHint"));
        return sb.ToString();
    }

    private async Task SendEmailOnceAsync(ParentWeeklyReportEvent e, (string Title, string Body) text, CultureInfo culture,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(e.RecipientEmail))
        {
            _logger.LogInformation("ParentWeeklyReport e-postası yok ya da kapalı (EventId={EventId}); yalnız in-app.", e.EventId);
            return;
        }
        if (await _db.NotificationEventLogs.AsNoTracking().AnyAsync(l => l.Type == EmailLogType && l.EventId == e.EventId, ct))
            return;

        await _email.SendAsync(BuildMessage(e.RecipientEmail, text.Title, BuildEmailBody(e, text.Body, culture)), ct);

        _db.NotificationEventLogs.Add(new NotificationEventLog { Type = EmailLogType, EventId = e.EventId, NotificationId = 0 });
        try
        {
            await _db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is Npgsql.PostgresException { SqlState: "23505" })
        {
            _logger.LogInformation("ParentWeeklyReport e-posta kaydı zaten var (EventId={EventId}).", e.EventId);
        }
    }

    internal static EmailMessage BuildMessage(string to, string title, string body)
    {
        var brand = ParentInvitationRequestedConsumer.Brand;
        var paragraphs = body.Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
            .Select(p => "<p>" + System.Net.WebUtility.HtmlEncode(p.Trim()).Replace("\n", "<br>") + "</p>");
        var html = string.Concat(paragraphs) + $"<p>{brand}</p>";
        return new EmailMessage(to, $"{brand} - {title}", $"{body}\n\n{brand}", html);
    }
}
