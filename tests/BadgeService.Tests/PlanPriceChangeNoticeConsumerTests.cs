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

/// <summary>Issue #1116: fiyat artışı bildirimi — veli / kendi metni ve bağlantı verisi, tek e-posta, idempotency, e-postasız alıcı.</summary>
public class PlanPriceChangeNoticeConsumerTests : IDisposable
{
    private const int ParentUser = 9401;
    private const int TeacherUser = 9402;

    private readonly BadgeTestDb _db = BadgeTestDb.Create();

    public void Dispose() => _db.Dispose();

    private sealed class CapturingEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = [];

        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private static ConsumeContext<PlanPriceChangeNoticeEvent> Context(PlanPriceChangeNoticeEvent message)
    {
        var ctx = Substitute.For<ConsumeContext<PlanPriceChangeNoticeEvent>>();
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

    private PlanPriceChangeNoticeConsumer Consumer(IEmailSender email) => new(
        _db.NewContext(), NewHub(), FallbackUserLocaleResolver.Instance, FallbackNotificationTextFactory.Instance, email,
        NullLogger<PlanPriceChangeNoticeConsumer>.Instance);

    private static PlanPriceChangeNoticeEvent Notice(int recipient, int? studentId, string email) => new()
    {
        EventId = Guid.NewGuid(),
        AssignmentId = 77,
        PlanName = "Öğrenci Pro",
        Duration = "Monthly",
        CurrentPrice = 349,
        NewPrice = 399.5m,
        Currency = "TRY",
        EffectiveFromUtc = new DateTime(2027, 4, 9, 12, 0, 0, DateTimeKind.Utc),
        RenewsAtUtc = new DateTime(2027, 4, 5, 12, 0, 0, DateTimeKind.Utc),
        StudentId = studentId,
        StudentDisplayName = studentId is null ? string.Empty : "Ayşe K.",
        RecipientUserId = recipient,
        RecipientKeycloakId = $"kc-{recipient}",
        RecipientEmail = email,
    };

    [Fact]
    public async Task Parent_gets_one_notification_to_the_child_plan_and_one_email_even_on_redelivery()
    {
        var email = new CapturingEmailSender();
        var e = Notice(ParentUser, 21, "veli@example.com");

        await Consumer(email).Consume(Context(e));
        await Consumer(email).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe(PlanPriceChangeNoticeConsumer.ToParentType);
        n.UserId.ShouldBe(ParentUser);
        n.Title.ShouldBe("Plan fiyatı değişiyor");
        n.Body.ShouldContain("Ayşe K.");
        n.Body.ShouldContain("Öğrenci Pro");
        n.Body.ShouldContain("349 TRY");
        n.Body.ShouldContain("399,50 TRY");
        n.Body.ShouldContain("9 Nisan 2027");
        n.Data!.ShouldContain("\"studentId\":21");

        var mail = email.Sent.ShouldHaveSingleItem();
        mail.To.ShouldBe("veli@example.com");
        mail.Subject.ShouldContain("Plan fiyatı değişiyor");
        mail.TextBody.ShouldContain("399,50 TRY");
    }

    [Fact]
    public async Task Own_plan_links_no_child_and_a_missing_email_sends_only_the_notification()
    {
        var email = new CapturingEmailSender();

        await Consumer(email).Consume(Context(Notice(TeacherUser, null, string.Empty)));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe(PlanPriceChangeNoticeConsumer.ToSelfType);
        n.Data!.ShouldContain("\"studentId\":null");
        n.Body.ShouldContain("Aboneliğim");
        email.Sent.ShouldBeEmpty();
    }
}
