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
/// issue #298 — öğretmen askıya alınınca öğrenciye "öğretmen geçici olarak müsait değil" bildirimi
/// (<see cref="BookingTeacherUnavailableConsumer"/>) ve otomatik reddin metni (<see cref="BookingDecisionConsumer"/>).
/// </summary>
public class BookingTeacherUnavailableConsumerTests : IDisposable
{
    private readonly BadgeTestDb _db = BadgeTestDb.Create();

    public void Dispose() => _db.Dispose();

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
        hub.Clients.User(Arg.Any<string>()).SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        return hub;
    }

    private BookingTeacherUnavailableConsumer NewConsumer(IHubContext<BadgeNotificationHub> hub)
        => new(_db.NewContext(), hub, FallbackUserLocaleResolver.Instance, FallbackNotificationTextFactory.Instance,
            NullLogger<BookingTeacherUnavailableConsumer>.Instance);

    private static BookingTeacherUnavailableEvent Evt(Guid? eventId = null, string? keycloakId = "kc-s1", params int[] bookingIds)
        => new()
        {
            EventId = eventId ?? Guid.NewGuid(),
            TeacherId = 10,
            StudentUserId = 200,
            TargetKeycloakId = keycloakId!,
            BookingIds = bookingIds.Length == 0 ? [7, 8] : bookingIds.ToList(),
            UnavailableSinceUtc = DateTime.UtcNow
        };

    [Fact]
    public async Task Consume_creates_a_teacher_unavailable_notification_and_pushes_BookingUpdate()
    {
        var hub = NewHub();
        var e = Evt();

        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe(BookingTeacherUnavailableConsumer.NotificationType);
        n.UserId.ShouldBe(200);
        n.UserKeycloakId.ShouldBe("kc-s1");
        n.SourceEventId.ShouldBe(e.EventId);
        n.SourceBookingId.ShouldBeNull();
        n.IsRead.ShouldBeFalse();
        n.Title.ShouldContain("geçici olarak müsait değil");
        n.Body.ShouldContain("2 dersinizin");
        n.Data.ShouldBe("{\"teacherId\":10,\"bookingIds\":[7,8]}");

        await hub.Clients.User("kc-s1").Received(1).SendCoreAsync(
            "BookingUpdate",
            Arg.Is<object?[]>(args => args.Length == 1 && args[0]!.ToString()!.Contains("kind = teacherUnavailable")),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Same_event_twice_creates_one_notification_and_pushes_once()
    {
        var e = Evt();
        await NewConsumer(NewHub()).Consume(Context(e));

        var secondHub = NewHub();
        await NewConsumer(secondHub).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(1);
        await secondHub.Clients.User(Arg.Any<string>()).DidNotReceive().SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Different_events_for_the_same_student_are_separate_notifications()
    {
        await NewConsumer(NewHub()).Consume(Context(Evt()));
        await NewConsumer(NewHub()).Consume(Context(Evt()));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task Missing_keycloak_id_is_resolved_from_an_earlier_notification()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.Notifications.Add(new BadgeService.Entities.Notification
            {
                UserId = 200, UserKeycloakId = "kc-earlier", Type = "BookingApproved", Title = "t", Body = "b",
                SourceBookingId = 1, CreatedAt = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }
        var hub = NewHub();
        var e = Evt(keycloakId: "");

        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.SingleAsync(n => n.SourceEventId == e.EventId)).UserKeycloakId.ShouldBe("kc-earlier");
        await hub.Clients.User("kc-earlier").Received(1).SendCoreAsync(
            "BookingUpdate", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Unresolvable_keycloak_id_throws_for_retry_and_dead_letter()
    {
        var hub = NewHub();

        await Should.ThrowAsync<InvalidOperationException>(() => NewConsumer(hub).Consume(Context(Evt(keycloakId: ""))));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(0);
    }

    // ---- BookingDecisionEvent.TeacherUnavailable (otomatik ret) ----

    private BookingDecisionConsumer NewDecisionConsumer(IHubContext<BadgeNotificationHub> hub)
        => new(_db.NewContext(), hub, NullLogger<BookingDecisionConsumer>.Instance,
            FallbackUserLocaleResolver.Instance, FallbackNotificationTextFactory.Instance);

    private static BookingDecisionEvent Decision(bool teacherUnavailable) => new()
    {
        BookingId = 42,
        TeacherId = 10,
        TeacherName = "Ayşe Öğretmen",
        StudentId = 20,
        StudentUserId = 200,
        TargetKeycloakId = "kc-s1",
        Approved = false,
        TeacherUnavailable = teacherUnavailable,
        Date = new DateOnly(2026, 10, 5),
        StartTime = new TimeOnly(14, 0),
        EndTime = new TimeOnly(15, 0),
        DecidedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task Auto_rejection_uses_the_teacher_unavailable_text_but_keeps_the_BookingRejected_type()
    {
        await NewDecisionConsumer(NewHub()).Consume(Context(Decision(teacherUnavailable: true)));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe(BookingDecisionConsumer.RejectedType);
        n.SourceBookingId.ShouldBe(42);
        n.Title.ShouldBe("Ders talebiniz yanıtlanamadı");
        n.Body.ShouldContain("geçici olarak müsait olmadığı için otomatik olarak reddedildi");
        n.Body.ShouldNotContain("Gerekçe:");
        n.Data.ShouldContain("\"teacherUnavailable\":true");
    }

    [Fact]
    public async Task Regular_rejection_keeps_the_old_text()
    {
        await NewDecisionConsumer(NewHub()).Consume(Context(Decision(teacherUnavailable: false)));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Title.ShouldContain("reddedildi");
        n.Body.ShouldContain("reddetti");
        n.Data.ShouldContain("\"teacherUnavailable\":false");
    }
}
