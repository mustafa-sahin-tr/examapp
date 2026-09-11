using BadgeService.Consumers;
using BadgeService.Hubs;
using BadgeService.Tests.Support;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BadgeService.Tests;

/// <summary>
/// Issue #96 — BookingRequestCreatedConsumer: öğrenci randevu talebi oluşturduğunda
/// öğretmene bildirim ve SignalR push. İdempotency: aynı teslim no-op.
/// </summary>
public class BookingRequestCreatedConsumerTests : IDisposable
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
        hub.Clients.User(Arg.Any<string>()).SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        return hub;
    }

    private BookingRequestCreatedConsumer NewConsumer(IHubContext<BadgeNotificationHub> hub)
        => new(_db.NewContext(), hub, NullLogger<BookingRequestCreatedConsumer>.Instance);

    private static BookingRequestCreatedEvent Evt(
        int bookingId = 1,
        int teacherUserId = 100,
        int teacherId = 10,
        int studentId = 20,
        int availabilitySlotId = 1,
        string studentName = "Ali Öğrenci",
        string? keycloakId = "kc-teacher-1")
        => new()
        {
            BookingId = bookingId,
            TeacherId = teacherId,
            TeacherUserId = teacherUserId,
            TargetKeycloakId = keycloakId,
            StudentId = studentId,
            StudentName = studentName,
            AvailabilitySlotId = availabilitySlotId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
            StartTime = new TimeOnly(14, 0),
            EndTime = new TimeOnly(15, 0),
            RequestedAt = DateTime.UtcNow
        };

    [Fact]
    public async Task Consume_NewBookingRequest_CreatesNotificationAndPushesToTeacher()
    {
        var hub = NewHub();
        var e = Evt();

        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe(BookingRequestCreatedConsumer.NotificationType);
        n.SourceBookingId.ShouldBe(e.BookingId);
        n.UserId.ShouldBe(e.TeacherUserId);
        n.UserKeycloakId.ShouldBe(e.TargetKeycloakId);
        n.IsRead.ShouldBeFalse();
        n.Title.ShouldContain("Yeni ders talebi");
        n.Body.ShouldContain(e.StudentName);

        await hub.Clients.User(e.TargetKeycloakId!).Received(1).SendCoreAsync(
            "BookingUpdate", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_SameBookingRequestTwice_OnlyCreatesOneNotificationAndSecondCallIsNoOp()
    {
        var e = Evt();

        await NewConsumer(NewHub()).Consume(Context(e));
        await NewConsumer(NewHub()).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync(
            n => n.Type == BookingRequestCreatedConsumer.NotificationType && n.SourceBookingId == e.BookingId))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Consume_DuplicateDelivery_DoesNotPushToTeacherAgain()
    {
        var e = Evt();

        await NewConsumer(NewHub()).Consume(Context(e));

        var secondHub = NewHub();
        await NewConsumer(secondHub).Consume(Context(e));

        // Second delivery should not push (idempotent no-op)
        await secondHub.Clients.User(Arg.Any<string>()).DidNotReceive().SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_MissingStudentName_FallsBackToGenericLabel()
    {
        var hub = NewHub();
        var e = Evt(studentName: "");

        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Body.ShouldContain("Bir öğrenci");
    }

    [Fact]
    public async Task Consume_MissingKeycloakId_LogsWarningButStoreNotification()
    {
        var hub = NewHub();
        var e = Evt(keycloakId: null);

        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.UserKeycloakId.ShouldBeNull();

        // Should not push (no target keycloak id)
        await hub.Clients.User(Arg.Any<string>()).DidNotReceive().SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_MultipleBookingRequests_CreatesMultipleNotifications()
    {
        var hub = NewHub();
        var e1 = Evt(bookingId: 1);
        var e2 = Evt(bookingId: 2);

        await NewConsumer(hub).Consume(Context(e1));
        await NewConsumer(hub).Consume(Context(e2));

        await using var check = _db.NewContext();
        var count = await check.Notifications.CountAsync(
            n => n.Type == BookingRequestCreatedConsumer.NotificationType);
        count.ShouldBe(2);
    }

    [Fact]
    public async Task Consume_NotificationDataHasBookingIdAndSlotId()
    {
        var hub = NewHub();
        var e = Evt(bookingId: 42, availabilitySlotId: 99);

        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Data.ShouldContain("\"bookingId\":42");
        n.Data.ShouldContain("\"availabilitySlotId\":99");
    }

    public void Dispose() => _db.Dispose();
}
