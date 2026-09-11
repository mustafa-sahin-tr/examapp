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
/// Issue #96 — BookingDecisionConsumer: öğretmen randevu talebini onaylayıp/reddettiğinde
/// öğrenciye bildirim ve SignalR push. İdempotency: aynı decision teslim no-op.
/// </summary>
public class BookingDecisionConsumerTests : IDisposable
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

    private BookingDecisionConsumer NewConsumer(IHubContext<BadgeNotificationHub> hub)
        => new(_db.NewContext(), hub, NullLogger<BookingDecisionConsumer>.Instance);

    private static BookingDecisionEvent Evt(
        int bookingId = 1,
        int teacherId = 10,
        int studentId = 20,
        int studentUserId = 200,
        bool approved = true,
        string? teacherName = "Ayşe Öğretmen",
        string? rejectionReason = null,
        string? keycloakId = "kc-student-1")
        => new()
        {
            BookingId = bookingId,
            TeacherId = teacherId,
            TeacherName = teacherName,
            StudentId = studentId,
            StudentUserId = studentUserId,
            TargetKeycloakId = keycloakId,
            Approved = approved,
            RejectionReason = rejectionReason,
            Date = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(5)),
            StartTime = new TimeOnly(14, 0),
            EndTime = new TimeOnly(15, 0),
            DecidedAt = DateTime.UtcNow
        };

    [Fact]
    public async Task Consume_ApprovedDecision_CreatesApprovedNotificationAndPushesToStudent()
    {
        var hub = NewHub();
        var e = Evt(approved: true);

        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe(BookingDecisionConsumer.ApprovedType);
        n.SourceBookingId.ShouldBe(e.BookingId);
        n.UserId.ShouldBe(e.StudentUserId);
        n.UserKeycloakId.ShouldBe(e.TargetKeycloakId);
        n.IsRead.ShouldBeFalse();
        n.Title.ShouldContain("onaylandı");
        n.Body.ShouldContain(e.TeacherName);
        n.Body.ShouldContain("onayladı");

        await hub.Clients.User(e.TargetKeycloakId!).Received(1).SendCoreAsync(
            "BookingUpdate", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_RejectedDecision_CreatesRejectedNotificationWithReason()
    {
        var hub = NewHub();
        var e = Evt(approved: false, rejectionReason: "Zaman çakışıyor.");

        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe(BookingDecisionConsumer.RejectedType);
        n.Title.ShouldContain("reddedildi");
        n.Body.ShouldContain("reddetti");
        n.Body.ShouldContain("Zaman çakışıyor");
    }

    [Fact]
    public async Task Consume_RejectedDecisionWithoutReason_OmitsReasonFromBody()
    {
        var hub = NewHub();
        var e = Evt(approved: false, rejectionReason: null);

        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe(BookingDecisionConsumer.RejectedType);
        n.Body.ShouldNotContain("Gerekçe:");
    }

    [Fact]
    public async Task Consume_SameApprovedDecisionTwice_OnlyCreatesOneApprovedNotification()
    {
        var e = Evt(bookingId: 1, approved: true);

        await NewConsumer(NewHub()).Consume(Context(e));
        await NewConsumer(NewHub()).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync(
            n => n.Type == BookingDecisionConsumer.ApprovedType && n.SourceBookingId == e.BookingId))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Consume_SameRejectedDecisionTwice_OnlyCreatesOneRejectedNotification()
    {
        var e = Evt(bookingId: 1, approved: false);

        await NewConsumer(NewHub()).Consume(Context(e));
        await NewConsumer(NewHub()).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync(
            n => n.Type == BookingDecisionConsumer.RejectedType && n.SourceBookingId == e.BookingId))
            .ShouldBe(1);
    }

    [Fact]
    public async Task Consume_DuplicateDelivery_DoesNotPushToStudentAgain()
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
    public async Task Consume_MissingTeacherName_FallsBackToGenericLabel()
    {
        var hub = NewHub();
        var e = Evt(teacherName: "");

        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Body.ShouldContain("Öğretmeniniz");
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
    public async Task Consume_MultipleApprovedDecisions_CreatesMultipleNotifications()
    {
        var hub = NewHub();
        var e1 = Evt(bookingId: 1, approved: true);
        var e2 = Evt(bookingId: 2, approved: true);

        await NewConsumer(hub).Consume(Context(e1));
        await NewConsumer(hub).Consume(Context(e2));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync(n => n.Type == BookingDecisionConsumer.ApprovedType))
            .ShouldBe(2);
    }

    [Fact]
    public async Task Consume_NotificationDataHasApprovedFlag()
    {
        var hub = NewHub();
        var e = Evt(bookingId: 42, approved: true);

        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Data.ShouldContain("\"bookingId\":42");
        n.Data.ShouldContain("\"approved\":true");
    }

    [Fact]
    public async Task Consume_RejectedDecisionDataHasApprovedFalse()
    {
        var hub = NewHub();
        var e = Evt(bookingId: 43, approved: false);

        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Data.ShouldContain("\"approved\":false");
    }

    public void Dispose() => _db.Dispose();
}
