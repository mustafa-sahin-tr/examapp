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
/// Issue #94: bağımsız öğretmen başvurusu tüketicisi — idempotency ve SignalR admin-grup push'u.
/// </summary>
public class TeacherApplicationSubmittedConsumerTests : IDisposable
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

    private TeacherApplicationSubmittedConsumer NewConsumer(IHubContext<BadgeNotificationHub> hub)
        => new(_db.NewContext(), hub, NullLogger<TeacherApplicationSubmittedConsumer>.Instance);

    private static TeacherApplicationSubmittedEvent Evt(int teacherId = 7, int userId = 42, string? name = "Ayşe Yılmaz")
        => new()
        {
            TeacherId = teacherId,
            UserId = userId,
            ApplicantName = name,
            SubmittedAt = DateTime.UtcNow
        };

    [Fact]
    public async Task Consume_NewTeacherId_CreatesNotificationAndPushesToAdminGroup()
    {
        var hub = NewHub();
        var e = Evt();

        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe(TeacherApplicationSubmittedConsumer.NotificationType);
        n.SourceTeacherApplicationId.ShouldBe(e.TeacherId);
        n.IsRead.ShouldBeFalse();

        await hub.Clients.Group(BadgeNotificationHub.AdminGroup).Received(1).SendCoreAsync(
            "TeacherApplicationSubmitted", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Consume_SameEventTwice_OnlyCreatesOneNotificationAndSecondCallIsNoOp()
    {
        var e = Evt();

        await NewConsumer(NewHub()).Consume(Context(e));
        await NewConsumer(NewHub()).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync(n => n.SourceTeacherApplicationId == e.TeacherId)).ShouldBe(1);
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
    public async Task Consume_MissingApplicantName_FallsBackToGenericLabelInBody()
    {
        var hub = NewHub();
        var e = Evt(teacherId: 8, name: null);

        await NewConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync(n => n.SourceTeacherApplicationId == 8);
        n.Body.ShouldContain("Bir öğretmen");
    }

    public void Dispose() => _db.Dispose();
}
