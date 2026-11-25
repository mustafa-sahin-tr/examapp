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

/// <summary>Issue #519: the study nudge notification: text with the day count, push, idempotent redelivery.</summary>
public class StudentStudyNudgeConsumerTests : IDisposable
{
    private const int StudentUser = 9401;
    private const string StudentSub = "kc-student-519";

    private readonly BadgeTestDb _db = BadgeTestDb.Create();

    public void Dispose() => _db.Dispose();

    private static ConsumeContext<StudentStudyNudgeEvent> Context(StudentStudyNudgeEvent message)
    {
        var ctx = Substitute.For<ConsumeContext<StudentStudyNudgeEvent>>();
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

    private StudentStudyNudgeConsumer Consumer(IHubContext<BadgeNotificationHub> hub) => new(
        _db.NewContext(), hub, FallbackUserLocaleResolver.Instance, FallbackNotificationTextFactory.Instance,
        NullLogger<StudentStudyNudgeConsumer>.Instance);

    [Fact]
    public async Task The_student_gets_one_nudge_with_the_day_count_even_when_delivered_twice()
    {
        var hub = NewHub();
        var e = new StudentStudyNudgeEvent
        {
            EventId = Guid.NewGuid(), StudentId = 31, RecipientUserId = StudentUser, RecipientKeycloakId = StudentSub,
            InactiveDays = 4, CreatedAtUtc = DateTime.UtcNow,
        };

        await Consumer(hub).Consume(Context(e));
        await Consumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe(StudentStudyNudgeConsumer.NotificationType);
        n.UserId.ShouldBe(StudentUser);
        n.Title.ShouldBe("4 gündür soru çözmedin");
        n.Body.ShouldContain("pratik ekranında");
        n.Data!.ShouldContain("\"inactiveDays\":4");
        hub.Clients.Received(1).User(StudentSub);
    }
}
