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
/// Okul duyurusu (issue #1281, #1071 S6): exam API'nin duyuru satırıyla AYNI transaction'da yazdığı parçalı
/// <see cref="AnnouncementPublishedEvent"/>'i alır ve parçadaki her alıcıya (öğrenci, öğretmen, veli) in-app
/// <see cref="Notification"/> yazar — toplu: tek sorguyla zaten yazılmışlar, tek sorguyla dil tercihi + sub, tek
/// <c>SaveChanges</c>. Sonra SignalR ile push (best-effort). Alıcı kararı producer'dadır.
///
/// Idempotency: alıcının <see cref="AnnouncementRecipient.NotificationId"/>'si <c>SourceEventId</c> olarak yazılır
/// (<c>(Type, SourceEventId)</c> filtreli unique index); yeniden teslim yazılmışları atlar. Eşzamanlı teslim 23505'e takılırsa
/// istisna retry'a gider, ikinci deneme yazılmışları atlar.
/// Hata yolu: 3 immediate retry (<see cref="AnnouncementPublishedConsumerDefinition"/>) → <c>badge-service_error</c>. Sub'ı ne
/// event'te ne BadgeService verisinde olan alıcı (hiç giriş yapmamış kullanıcı) atlanır ve sayısı loglanır: tek kullanıcı yüzünden
/// bütün parça error kuyruğuna gitmez; bildirimi zaten göremezdi (bildirimler sub ile okunur).
/// </summary>
public class AnnouncementPublishedConsumer : IConsumer<AnnouncementPublishedEvent>
{
    public const string ToStudentType = "AnnouncementToStudent";
    public const string ToTeacherType = "AnnouncementToTeacher";
    public const string ToParentType = "AnnouncementToParent";

    private readonly BadgeDbContext _db;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly INotificationTextFactory _texts;
    private readonly ILogger<AnnouncementPublishedConsumer> _logger;

    public AnnouncementPublishedConsumer(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        INotificationTextFactory texts,
        ILogger<AnnouncementPublishedConsumer> logger)
    {
        _db = db;
        _hub = hub;
        _texts = texts;
        _logger = logger;
    }

    public static string? TypeFor(string audience) => audience switch
    {
        AnnouncementPublishedEvent.StudentAudience => ToStudentType,
        AnnouncementPublishedEvent.TeacherAudience => ToTeacherType,
        AnnouncementPublishedEvent.ParentAudience => ToParentType,
        _ => null
    };

    public async Task Consume(ConsumeContext<AnnouncementPublishedEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        var recipients = e.Recipients
            .Where(r => r.UserId > 0 && r.NotificationId != Guid.Empty && TypeFor(r.Audience) != null)
            .GroupBy(r => r.NotificationId)
            .Select(g => g.First())
            .ToList();
        var invalid = e.Recipients.Count - recipients.Count;

        var ids = recipients.Select(r => r.NotificationId).ToList();
        var done = (await _db.Notifications.AsNoTracking()
                .Where(n => n.SourceEventId != null && ids.Contains(n.SourceEventId.Value)
                            && (n.Type == ToStudentType || n.Type == ToTeacherType || n.Type == ToParentType))
                .Select(n => n.SourceEventId!.Value)
                .ToListAsync(ct))
            .ToHashSet();
        var pending = recipients.Where(r => !done.Contains(r.NotificationId)).ToList();
        if (pending.Count == 0)
        {
            _logger.LogInformation("AnnouncementPublished zaten işlenmiş (EventId={EventId}, AnnouncementId={AnnouncementId}).",
                e.EventId, e.AnnouncementId);
            return;
        }

        var userIds = pending.Select(r => r.UserId).Distinct().ToList();
        var prefs = (await _db.UserLocalePreferences.AsNoTracking()
                .Where(p => userIds.Contains(p.UserId))
                .Select(p => new { p.UserId, p.KeycloakId, p.Locale })
                .ToListAsync(ct))
            .GroupBy(p => p.UserId)
            .ToDictionary(g => g.Key, g => g.First());
        var needSub = pending
            .Where(r => string.IsNullOrWhiteSpace(r.KeycloakId)
                        && !(prefs.TryGetValue(r.UserId, out var p) && !string.IsNullOrWhiteSpace(p.KeycloakId)))
            .Select(r => r.UserId)
            .Distinct()
            .ToList();
        var subFromNotifications = needSub.Count == 0
            ? new Dictionary<int, string>()
            : (await _db.Notifications.AsNoTracking()
                    .Where(n => needSub.Contains(n.UserId) && n.UserKeycloakId != null && n.UserKeycloakId != "")
                    .Select(n => new { n.UserId, n.Id, n.UserKeycloakId })
                    .ToListAsync(ct))
                .GroupBy(n => n.UserId)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(n => n.Id).First().UserKeycloakId!);

        string? SubFor(AnnouncementRecipient r)
        {
            if (!string.IsNullOrWhiteSpace(r.KeycloakId))
                return r.KeycloakId;
            if (prefs.TryGetValue(r.UserId, out var p) && !string.IsNullOrWhiteSpace(p.KeycloakId))
                return p.KeycloakId;
            return subFromNotifications.GetValueOrDefault(r.UserId);
        }

        var title = CommentNotificationSupport.CleanTitle(e.Title);
        var body = CommentNotificationSupport.CleanBody(e.Body);
        var sender = CommentNotificationSupport.CleanName(e.SenderDisplayName);
        var school = CommentNotificationSupport.CleanTitle(e.SchoolName);
        var now = DateTime.UtcNow;

        var rows = new List<(Notification Notification, string Sub, object Data)>();
        var unresolved = 0;
        foreach (var r in pending)
        {
            var sub = SubFor(r);
            if (sub == null)
            {
                unresolved++;
                continue;
            }

            var culture = CultureInfo.GetCultureInfo(prefs.TryGetValue(r.UserId, out var pref)
                ? SupportedLocales.ToCultureName(pref.Locale)
                : SupportedLocales.DefaultCultureName);
            var type = TypeFor(r.Audience)!;
            var schoolLabel = string.IsNullOrWhiteSpace(school) ? _texts.Resolve("notifications.common.defaultSchool", culture) : school;
            var source = string.IsNullOrWhiteSpace(sender) ? schoolLabel : $"{sender} ({schoolLabel})";
            var cleanChild = CommentNotificationSupport.CleanName(r.StudentDisplayName);
            var child = string.IsNullOrWhiteSpace(cleanChild) ? _texts.Resolve("notifications.common.defaultChild", culture) : cleanChild;
            // {0} başlık, {1} metin, {2} kaynak ("Ad S. (Okul)"), {3} velide çocuk.
            var text = _texts.Build(type, culture, title, body, source, type == ToParentType ? child : string.Empty);
            var data = new
            {
                announcementId = e.AnnouncementId,
                studentId = type == ToParentType ? r.StudentId : null
            };
            rows.Add((new Notification
            {
                UserId = r.UserId,
                UserKeycloakId = sub,
                Type = type,
                Title = text.Title,
                Body = text.Body,
                Data = JsonSerializer.Serialize(data),
                SourceEventId = r.NotificationId,
                IsRead = false,
                CreatedAt = now
            }, sub, data));
        }

        if (rows.Count > 0)
        {
            _db.Notifications.AddRange(rows.Select(x => x.Notification));
            await _db.SaveChangesAsync(ct);
        }

        foreach (var (notification, sub, data) in rows)
        {
            try
            {
                await ParentNotificationWriter.PushAsync(_hub, sub, notification, notification.Type, data, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Satır yazıldı; push kaybı zil açılınca telafi edilir. Retry'a gitmez (yazılanlar zaten atlanırdı).
                _logger.LogWarning(ex, "AnnouncementPublished push başarısız (NotificationId={NotificationId}).", notification.Id);
            }
        }

        if (unresolved > 0 || invalid > 0)
            _logger.LogWarning(
                "AnnouncementPublished: {Unresolved} alıcının sub'ı çözülemedi, {Invalid} geçersiz alıcı atlandı (EventId={EventId}, AnnouncementId={AnnouncementId}).",
                unresolved, invalid, e.EventId, e.AnnouncementId);
        _logger.LogInformation(
            "AnnouncementPublished işlendi. EventId={EventId}, AnnouncementId={AnnouncementId}, Chunk={Chunk}/{ChunkCount}, Written={Written}, Skipped={Skipped}",
            e.EventId, e.AnnouncementId, e.ChunkIndex + 1, e.ChunkCount, rows.Count, done.Count);
    }
}
