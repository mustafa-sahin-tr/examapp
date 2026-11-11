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

/// <summary>Issue #1111: "Öğretmene sor" bildirimleri — tür başına alıcı metni, soru linki verisi, varsayılan ad, idempotency, bilinmeyen tür.</summary>
public class AskTeacherNoticeConsumerTests : IDisposable
{
    private const int User = 9501;
    private const string Sub = "kc-ask-9501";

    private readonly BadgeTestDb _db = BadgeTestDb.Create();

    public void Dispose() => _db.Dispose();

    private static ConsumeContext<AskTeacherNoticeEvent> Context(AskTeacherNoticeEvent message)
    {
        var ctx = Substitute.For<ConsumeContext<AskTeacherNoticeEvent>>();
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

    private AskTeacherNoticeConsumer Consumer(IHubContext<BadgeNotificationHub> hub) => new(
        _db.NewContext(), hub, FallbackUserLocaleResolver.Instance, FallbackNotificationTextFactory.Instance,
        NullLogger<AskTeacherNoticeConsumer>.Instance);

    private static AskTeacherNoticeEvent Evt(string kind, string name = "Ayşe K.") => new()
    {
        EventId = Guid.NewGuid(),
        Kind = kind,
        QuestionId = 42,
        CounterpartName = name,
        DueAtUtc = DateTime.UtcNow.AddHours(48),
        RecipientUserId = User,
        RecipientKeycloakId = Sub,
    };

    private async Task<List<Notification>> AllAsync()
    {
        await using var check = _db.NewContext();
        return await check.Notifications.ToListAsync();
    }

    [Theory]
    [InlineData(AskTeacherNoticeEvent.Asked, AskTeacherNoticeConsumer.AskedType)]
    [InlineData(AskTeacherNoticeEvent.Answered, AskTeacherNoticeConsumer.AnsweredType)]
    [InlineData(AskTeacherNoticeEvent.Expired, AskTeacherNoticeConsumer.ExpiredType)]
    public async Task Each_kind_notifies_the_recipient_with_a_link_to_the_question(string kind, string type)
    {
        var hub = NewHub();
        await Consumer(hub).Consume(Context(Evt(kind)));

        var n = (await AllAsync()).Single();
        n.Type.ShouldBe(type);
        n.UserId.ShouldBe(User);
        n.UserKeycloakId.ShouldBe(Sub);
        n.Body.ShouldContain("Ayşe K.");
        n.Body.ShouldNotContain("{");
        n.Data!.ShouldBe("{\"questionId\":42}");
        hub.Clients.Received(1).User(Sub);
    }

    [Fact]
    public async Task New_question_tells_the_teacher_the_hours_left_and_falls_back_to_a_default_name()
    {
        await Consumer(NewHub()).Consume(Context(Evt(AskTeacherNoticeEvent.Asked, name: " ")));

        var n = (await AllAsync()).Single();
        n.Body.ShouldStartWith("Bir öğrenci");
        n.Body.ShouldContain("48 saat");
    }

    [Fact]
    public async Task Redelivered_event_is_idempotent_and_unknown_kind_is_skipped()
    {
        var hub = NewHub();
        var e = Evt(AskTeacherNoticeEvent.Answered);
        await Consumer(hub).Consume(Context(e));
        await Consumer(hub).Consume(Context(e));
        await Consumer(hub).Consume(Context(Evt("Reopened")));

        (await AllAsync()).Count.ShouldBe(1);
        hub.Clients.Received(1).User(Sub);
    }
}
