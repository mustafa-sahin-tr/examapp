using BadgeService.Consumers;
using BadgeService.Hubs;
using BadgeService.Services;
using BadgeService.Tests.Support;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BadgeService.Tests;

/// <summary>
/// Issue #277 (madde 1): okul bağlantısı talebi admin bildirimi tüketicisi — EventId ile
/// dedup (TeacherId TEK BAŞINA tekillik için yetersiz, bir öğretmen birden fazla okul talebi
/// açabilir) ve SignalR admin-grup push'u.
/// </summary>
public class TeacherSchoolRequestSubmittedConsumerTests : IDisposable
{
    private readonly BadgeTestDb _db = BadgeTestDb.Create();

    private static ConsumeContext<T> Context<T>(T message) where T : class
    {
        var ctx = Substitute.For<ConsumeContext<T>>();
        ctx.Message.Returns(message);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    private static IHubContext<BadgeNotificationHub> NewHub()
    {
        var hub = Substitute.For<IHubContext<BadgeNotificationHub>>();
        hub.Clients.Group(Arg.Any<string>()).SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        return hub;
    }

    private TeacherSchoolRequestSubmittedConsumer NewConsumer(IHubContext<BadgeNotificationHub> hub)
        => new(
            _db.NewContext(),
            hub,
            FallbackNotificationTextFactory.Instance,
            NullLogger<TeacherSchoolRequestSubmittedConsumer>.Instance);

    private static TeacherSchoolRequestSubmittedEvent Evt(
        Guid? eventId = null,
        int teacherId = 11,
        int userId = 42,
        int requestedSchoolId = 3,
        string? schoolName = "Atatürk Lisesi",
        string? applicantName = "Ayşe Yılmaz",
        bool isNewRegistration = false)
        => new()
        {
            EventId = eventId ?? Guid.NewGuid(),
            TeacherId = teacherId,
            UserId = userId,
            RequestedSchoolId = requestedSchoolId,
            RequestedSchoolName = schoolName,
            ApplicantName = applicantName,
            IsNewRegistration = isNewRegistration,
            SubmittedAtUtc = DateTime.UtcNow
        };

    [Fact]
    public async Task Consume_NewEvent_CreatesNotificationAndPushesToAdminGroup()
    {
        var hub = NewHub();
        var e = Evt();

        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe(TeacherSchoolRequestSubmittedConsumer.NotificationType);
        n.SourceEventId.ShouldBe(e.EventId);
        n.SourceTeacherApplicationId.ShouldBe(e.TeacherId);
        n.IsRead.ShouldBeFalse();
        n.Body.ShouldContain("Ayşe Yılmaz");
        n.Body.ShouldContain("Atatürk Lisesi");

        await hub.Clients.Group(BadgeNotificationHub.AdminGroup).Received(1).SendCoreAsync(
            "TeacherSchoolRequestSubmitted", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_SameEventTwice_OnlyCreatesOneNotification()
    {
        var e = Evt();

        await NewConsumer(NewHub()).Consume(Context(e));
        await NewConsumer(NewHub()).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync(n => n.SourceEventId == e.EventId)).ShouldBe(1);
    }

    [Fact]
    public async Task Consume_DuplicateDelivery_DoesNotPushToAdminGroupAgain()
    {
        var e = Evt();

        await NewConsumer(NewHub()).Consume(Context(e));

        var secondHub = NewHub();
        await NewConsumer(secondHub).Consume(Context(e));

        await secondHub.Clients.Group(BadgeNotificationHub.AdminGroup).DidNotReceive().SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_SameTeacherDifferentSchoolRequests_CreatesTwoNotifications()
    {
        // Bir öğretmen zamanla birden fazla okul talebi açabilir (ör. ilk talep reddedilir,
        // yeni bir okula talep açar) — TeacherId aynı olsa da her talep (farklı EventId) ayrı
        // bildirim üretmeli; bu yüzden dedup TeacherId değil EventId üzerinden kurulur.
        var e1 = Evt(teacherId: 5, requestedSchoolId: 1);
        var e2 = Evt(teacherId: 5, requestedSchoolId: 2);

        await NewConsumer(NewHub()).Consume(Context(e1));
        await NewConsumer(NewHub()).Consume(Context(e2));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync(n => n.SourceTeacherApplicationId == 5)).ShouldBe(2);
    }

    [Fact]
    public async Task Consume_MissingApplicantAndSchoolName_FallsBackToGenericLabels()
    {
        var hub = NewHub();
        var e = Evt(applicantName: null, schoolName: null);

        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync(n => n.SourceEventId == e.EventId);
        n.Body.ShouldContain("Bir öğretmen");
        n.Body.ShouldContain("bir okul");
    }

    public void Dispose() => _db.Dispose();
}
