using System.Globalization;
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

/// <summary>Issue #1091: "çocuğun planı / denemesi bitiyor" veli bildirimi — metin, bağlantı verisi, idempotency, sub yoksa hata.</summary>
public class ParentChildPlanEndingConsumerTests : IDisposable
{
    private const int ParentUser = 9201;
    private const string ParentSub = "kc-parent-1091";

    private readonly BadgeTestDb _db = BadgeTestDb.Create();

    public void Dispose() => _db.Dispose();

    private static ConsumeContext<ParentChildPlanEndingEvent> Context(ParentChildPlanEndingEvent message)
    {
        var ctx = Substitute.For<ConsumeContext<ParentChildPlanEndingEvent>>();
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

    private ParentChildPlanEndingConsumer Consumer(IHubContext<BadgeNotificationHub> hub) => new(
        _db.NewContext(), hub, FallbackUserLocaleResolver.Instance, FallbackNotificationTextFactory.Instance,
        NullLogger<ParentChildPlanEndingConsumer>.Instance);

    private static ParentChildPlanEndingEvent Ending(bool isTrial = false, string parentSub = ParentSub) => new()
    {
        EventId = Guid.NewGuid(),
        AssignmentId = 47,
        IsTrial = isTrial,
        // 23:30 UTC = ertesi gün İstanbul'da: tarih Türkiye gününe göre yazılır.
        EndsAtUtc = new DateTime(2026, 12, 9, 23, 30, 0, DateTimeKind.Utc),
        StudentId = 21,
        StudentDisplayName = "Ayşe K.",
        ParentId = 11,
        ParentUserId = ParentUser,
        ParentKeycloakId = parentSub,
    };

    [Fact]
    public async Task Paid_plan_ending_notifies_the_parent_with_the_istanbul_date_and_a_child_link()
    {
        var hub = NewHub();
        var e = Ending();

        await Consumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe(ParentChildPlanEndingConsumer.NotificationType);
        n.UserId.ShouldBe(ParentUser);
        n.UserKeycloakId.ShouldBe(ParentSub);
        n.SourceEventId.ShouldBe(e.EventId);
        n.Title.ShouldBe("Plan bitiyor");
        n.Body.ShouldContain("Ayşe K.");
        n.Body.ShouldContain(new DateTime(2026, 12, 10).ToString("d MMMM", CultureInfo.GetCultureInfo("tr-TR")));
        n.Data!.ShouldContain("\"studentId\":21");
        hub.Clients.Received(1).User(ParentSub);
    }

    [Fact]
    public async Task Trial_ending_uses_the_trial_text_and_redelivery_is_idempotent()
    {
        var hub = NewHub();
        var e = Ending(isTrial: true);

        await Consumer(hub).Consume(Context(e));
        await Consumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe(ParentChildPlanEndingConsumer.NotificationType);
        n.Title.ShouldBe("Deneme süresi bitiyor");
        hub.Clients.Received(1).User(ParentSub);
    }

    [Fact]
    public async Task Unresolvable_parent_sub_throws_instead_of_dropping_the_reminder()
    {
        await Should.ThrowAsync<InvalidOperationException>(() => Consumer(NewHub()).Consume(Context(Ending(parentSub: ""))));
        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(0);
    }
}
