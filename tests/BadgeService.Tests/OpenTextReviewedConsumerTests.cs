using BadgeService.Consumers;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Services;
using BadgeService.Tests.Support;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BadgeService.Tests;

/// <summary>Issue #614: öğretmen açık uçlu cevabı değerlendirince öğrenciye bildirim — metin, derin link verisi, idempotency, sub çözümü.</summary>
public class OpenTextReviewedConsumerTests : IDisposable
{
    private const int StudentUser = 9401;
    private const string StudentSub = "kc-open-text-9401";

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

    private OpenTextReviewedConsumer Consumer(IHubContext<BadgeNotificationHub> hub) => new(
        _db.NewContext(), hub, FallbackUserLocaleResolver.Instance, FallbackNotificationTextFactory.Instance,
        NullLogger<OpenTextReviewedConsumer>.Instance);

    private static OpenTextReviewedEvent Evt(string sub = StudentSub, string worksheet = "Kesirler etkinliği") => new()
    {
        EventId = Guid.NewGuid(),
        RecipientUserId = StudentUser,
        RecipientKeycloakId = sub,
        TestInstanceId = 77,
        InstanceQuestionId = 501,
        WorksheetId = 12,
        WorksheetName = worksheet,
        ReviewedAtUtc = DateTime.UtcNow
    };

    private async Task<Notification> SingleAsync()
    {
        await using var check = _db.NewContext();
        return await check.Notifications.SingleAsync();
    }

    [Fact]
    public async Task Review_notifies_the_student_with_a_link_to_the_item()
    {
        var hub = NewHub();

        await Consumer(hub).Consume(Context(Evt()));

        var n = await SingleAsync();
        n.Type.ShouldBe(OpenTextReviewedConsumer.NotificationType);
        n.UserId.ShouldBe(StudentUser);
        n.UserKeycloakId.ShouldBe(StudentSub);
        n.Body.ShouldContain("Kesirler etkinliği");
        n.Body.ShouldNotContain("{");
        n.Data!.ShouldContain("\"testInstanceId\":77");
        n.Data!.ShouldContain("\"instanceQuestionId\":501");
        hub.Clients.Received(1).User(StudentSub);
    }

    [Fact]
    public async Task Missing_activity_name_falls_back_to_default_text()
    {
        await Consumer(NewHub()).Consume(Context(Evt(worksheet: "   ")));

        (await SingleAsync()).Body.ShouldStartWith("Etkinlik:");
    }

    [Fact]
    public async Task Redelivered_event_is_idempotent()
    {
        var hub = NewHub();
        var e = Evt();

        await Consumer(hub).Consume(Context(e));
        await Consumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(1);
        hub.Clients.Received(1).User(StudentSub);
    }

    [Fact]
    public async Task Sub_is_resolved_from_badge_service_data_or_the_message_fails()
    {
        await using (var seed = _db.NewContext())
        {
            seed.UserLocalePreferences.Add(new UserLocalePreference { UserId = StudentUser, KeycloakId = StudentSub, UpdatedAtUtc = DateTime.UtcNow });
            await seed.SaveChangesAsync();
        }

        await Consumer(NewHub()).Consume(Context(Evt(sub: "")));
        (await SingleAsync()).UserKeycloakId.ShouldBe(StudentSub);

        var unknown = Evt(sub: "");
        unknown.RecipientUserId = StudentUser + 1;
        (await Should.ThrowAsync<Exception>(() => Consumer(NewHub()).Consume(Context(unknown))))
            .ShouldBeOfType<RecipientSubUnresolvedException>();
    }
}
