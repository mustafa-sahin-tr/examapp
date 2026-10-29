using System.Text.Json;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Services;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;

namespace BadgeService.Consumers;

/// <summary>
/// Okul değişimi bildirimi (issue #542, #194 K5): exam API'nin admin okul değişikliğiyle (öğrenci #277/#541, öğretmen #313/#539)
/// AYNI transaction'da yazdığı <see cref="SchoolChangedEvent"/>'i alır; alıcının (öğrencinin kendisi, öğretmen ya da öğrencinin
/// Active velisi) in-app <see cref="Notification"/> satırını oluşturur ve SignalR (<c>ParentNotification</c>) ile push eder. Metin
/// alıcıya ve geçişe göre seçilir: okul → okul (değişti), okulsuz → okul (belirlendi), okul → okulsuz (kaldırıldı, #541); öğretmen
/// mesajı özel yapılan "Okulum" sınavı sayısını içerir (#539). Dil alıcının tercihi.
///
/// Alıcı kararı producer'dadır (yalnız okul gerçekten değişince, yalnız Active veliler); event başına TEK alıcı.
/// Idempotency: <c>(Type, SourceEventId)</c> filtreli unique index + önceden <c>Any</c> kontrolü + 23505 yakalama.
/// Hata yolu: beklenmeyen hata → 3 kez immediate retry (<see cref="SchoolChangedConsumerDefinition"/>) → <c>badge-service_error</c>;
/// alıcı sub'ı çözülemezse <see cref="InvalidOperationException"/> → aynı yol. Bilinmeyen alıcı türü loglanıp atlanır.
/// </summary>
public class SchoolChangedConsumer : IConsumer<SchoolChangedEvent>
{
    public const string ChangedToStudentType = "SchoolChangedToStudent";
    public const string AssignedToStudentType = "SchoolAssignedToStudent";
    public const string RemovedToStudentType = "SchoolRemovedToStudent";
    public const string ChangedToTeacherType = "SchoolChangedToTeacher";
    public const string AssignedToTeacherType = "SchoolAssignedToTeacher";
    public const string ChangedToParentType = "SchoolChangedToParent";
    public const string AssignedToParentType = "SchoolAssignedToParent";
    public const string RemovedToParentType = "SchoolRemovedToParent";

    private readonly BadgeDbContext _db;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly IUserLocaleResolver _localeResolver;
    private readonly INotificationTextFactory _texts;
    private readonly ILogger<SchoolChangedConsumer> _logger;

    public SchoolChangedConsumer(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        IUserLocaleResolver localeResolver,
        INotificationTextFactory texts,
        ILogger<SchoolChangedConsumer> logger)
    {
        _db = db;
        _hub = hub;
        _localeResolver = localeResolver;
        _texts = texts;
        _logger = logger;
    }

    /// <summary>Alıcı + geçiş → bildirim tipi; okulsuzdan okulsuza ya da öğretmenin okulsuz yapılması (yok) → null.</summary>
    public static string? TypeFor(SchoolChangedEvent e)
    {
        var assigned = e.FromSchoolId is null && e.ToSchoolId is not null;
        var removed = e.FromSchoolId is not null && e.ToSchoolId is null;
        var changed = e.FromSchoolId is not null && e.ToSchoolId is not null && e.FromSchoolId != e.ToSchoolId;
        return e.Audience switch
        {
            SchoolChangedEvent.StudentAudience when changed => ChangedToStudentType,
            SchoolChangedEvent.StudentAudience when assigned => AssignedToStudentType,
            SchoolChangedEvent.StudentAudience when removed => RemovedToStudentType,
            SchoolChangedEvent.TeacherAudience when changed => ChangedToTeacherType,
            SchoolChangedEvent.TeacherAudience when assigned => AssignedToTeacherType,
            SchoolChangedEvent.ParentAudience when changed => ChangedToParentType,
            SchoolChangedEvent.ParentAudience when assigned => AssignedToParentType,
            SchoolChangedEvent.ParentAudience when removed => RemovedToParentType,
            _ => null
        };
    }

    public async Task Consume(ConsumeContext<SchoolChangedEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        var type = TypeFor(e);
        if (type == null)
        {
            _logger.LogWarning(
                "SchoolChanged bilinmeyen alıcı/geçiş (EventId={EventId}, Audience={Audience}, From={From}, To={To}); bildirim üretilmedi.",
                e.EventId, e.Audience, e.FromSchoolId, e.ToSchoolId);
            return;
        }

        if (await ParentNotificationWriter.AlreadyProcessedAsync(_db, type, e.EventId, ct))
        {
            _logger.LogInformation("SchoolChanged zaten işlenmiş (EventId={EventId}, Type={Type}); atlanıyor.", e.EventId, type);
            return;
        }

        var sub = await NotificationRecipientResolver.ResolveSubAsync(
            _db, e.RecipientUserId, e.RecipientKeycloakId, $"SchoolChanged (EventId={e.EventId})", ct);
        var culture = await _localeResolver.ResolveAsync(e.RecipientUserId, sub, ct);

        string School(string name)
        {
            var clean = CommentNotificationSupport.CleanTitle(name);
            return string.IsNullOrWhiteSpace(clean) ? _texts.Resolve("notifications.common.defaultSchool", culture) : clean;
        }

        var cleanChild = CommentNotificationSupport.CleanName(e.StudentDisplayName);
        var child = string.IsNullOrWhiteSpace(cleanChild) ? _texts.Resolve("notifications.common.defaultChild", culture) : cleanChild;
        // {0} eski okul, {1} yeni okul, {2} öğretmende özel yapılan sınav cümlesi / velide çocuğun adı.
        var third = type switch
        {
            ChangedToTeacherType => e.PrivatizedWorksheetCount > 0
                ? _texts.Resolve("notifications.common.schoolChangePrivatized", culture, e.PrivatizedWorksheetCount)
                : string.Empty,
            ChangedToParentType or AssignedToParentType or RemovedToParentType => child,
            _ => string.Empty
        };
        var text = _texts.Build(type, culture, School(e.FromSchoolName), School(e.ToSchoolName), third);

        var data = new
        {
            studentId = e.Audience == SchoolChangedEvent.ParentAudience ? e.StudentId : null,
            fromSchoolId = e.FromSchoolId,
            toSchoolId = e.ToSchoolId,
            privatizedWorksheetCount = e.PrivatizedWorksheetCount
        };
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
            _logger.LogInformation("SchoolChanged eşzamanlı duplicate (EventId={EventId}, Type={Type}); atlanıyor.", e.EventId, type);
            return;
        }

        await ParentNotificationWriter.PushAsync(_hub, sub, notification, type, data, ct);

        _logger.LogInformation(
            "SchoolChanged işlendi. EventId={EventId}, Type={Type}, RecipientUserId={UserId}, NotificationId={NotificationId}",
            e.EventId, type, e.RecipientUserId, notification.Id);
    }
}
