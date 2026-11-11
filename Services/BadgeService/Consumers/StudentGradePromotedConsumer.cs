using System.Text.Json;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Services;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;

namespace BadgeService.Consumers;

/// <summary>
/// Issue #1110: 1 Ağustos sınıf atlaması. Exam API'nin <c>StudentGradePromotionJob</c>'ının sınıf güncellemesiyle AYNI transaction'da
/// yazdığı <see cref="StudentGradePromotedEvent"/>'i alır; alıcının (öğrencinin kendisi ya da Active velisi) in-app
/// <see cref="Notification"/> satırını oluşturur ve SignalR (<c>ParentNotification</c>) ile push eder. Dil alıcının tercihi.
///
/// Alıcı kararı producer'dadır; event başına TEK alıcı. Idempotency <see cref="SchoolChangedConsumer"/> ile aynı
/// (<c>(Type, SourceEventId)</c>). Bilinmeyen alıcı türü loglanıp atlanır.
/// </summary>
public class StudentGradePromotedConsumer : IConsumer<StudentGradePromotedEvent>
{
    public const string ToStudentType = "GradePromotedToStudent";
    public const string ToParentType = "GradePromotedToParent";

    private readonly BadgeDbContext _db;
    private readonly IHubContext<BadgeNotificationHub> _hub;
    private readonly IUserLocaleResolver _localeResolver;
    private readonly INotificationTextFactory _texts;
    private readonly ILogger<StudentGradePromotedConsumer> _logger;

    public StudentGradePromotedConsumer(
        BadgeDbContext db,
        IHubContext<BadgeNotificationHub> hub,
        IUserLocaleResolver localeResolver,
        INotificationTextFactory texts,
        ILogger<StudentGradePromotedConsumer> logger)
    {
        _db = db;
        _hub = hub;
        _localeResolver = localeResolver;
        _texts = texts;
        _logger = logger;
    }

    public static string? TypeFor(StudentGradePromotedEvent e) => e.Audience switch
    {
        StudentGradePromotedEvent.StudentAudience => ToStudentType,
        StudentGradePromotedEvent.ParentAudience => ToParentType,
        _ => null
    };

    public async Task Consume(ConsumeContext<StudentGradePromotedEvent> context)
    {
        var e = context.Message;
        var ct = context.CancellationToken;

        var type = TypeFor(e);
        if (type == null)
        {
            _logger.LogWarning("StudentGradePromoted bilinmeyen alıcı (EventId={EventId}, Audience={Audience}); bildirim üretilmedi.",
                e.EventId, e.Audience);
            return;
        }

        if (await ParentNotificationWriter.AlreadyProcessedAsync(_db, type, e.EventId, ct))
        {
            _logger.LogInformation("StudentGradePromoted zaten işlenmiş (EventId={EventId}, Type={Type}); atlanıyor.", e.EventId, type);
            return;
        }

        var sub = await NotificationRecipientResolver.ResolveSubAsync(
            _db, e.RecipientUserId, e.RecipientKeycloakId, $"StudentGradePromoted (EventId={e.EventId})", ct);
        var culture = await _localeResolver.ResolveAsync(e.RecipientUserId, sub, ct);

        var cleanChild = CommentNotificationSupport.CleanName(e.StudentDisplayName);
        var child = string.IsNullOrWhiteSpace(cleanChild) ? _texts.Resolve("notifications.common.defaultChild", culture) : cleanChild;
        // {0} yeni sınıf seviyesi, {1} velide çocuğun adı.
        var text = _texts.Build(type, culture, e.ToGradeLevel, type == ToParentType ? child : string.Empty);

        var data = new
        {
            studentId = type == ToParentType ? e.StudentId : (int?)null,
            toGradeLevel = e.ToGradeLevel,
            year = e.Year
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
            _logger.LogInformation("StudentGradePromoted eşzamanlı duplicate (EventId={EventId}, Type={Type}); atlanıyor.", e.EventId, type);
            return;
        }

        await ParentNotificationWriter.PushAsync(_hub, sub, notification, type, data, ct);

        _logger.LogInformation(
            "StudentGradePromoted işlendi. EventId={EventId}, Type={Type}, StudentId={StudentId}, RecipientUserId={UserId}, NotificationId={NotificationId}",
            e.EventId, type, e.StudentId, e.RecipientUserId, notification.Id);
    }
}
