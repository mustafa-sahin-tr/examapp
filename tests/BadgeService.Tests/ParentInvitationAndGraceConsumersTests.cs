using BadgeService.Consumers;
using BadgeService.Hubs;
using BadgeService.Services;
using BadgeService.Tests.Support;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Security;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BadgeService.Tests;

/// <summary>issue #440 (veli daveti e-postası + mevcut veli bildirimi) ve #441 (öğrenci ek süre hatırlatması) consumer testleri.</summary>
public class ParentInvitationAndGraceConsumersTests : IDisposable
{
    private const string ParentSub = "kc-parent-1";
    private const string StudentSub = "kc-student-1";
    private const string SecretUrl = "https://hedefokul.test/app/activate?token=SECRET-TOKEN-123";

    private static readonly ActivationLinkProtector Protector = new(Convert.FromBase64String(ActivationLinkProtector.DevKeyBase64));

    private readonly BadgeTestDb _db = BadgeTestDb.Create();

    public void Dispose() => _db.Dispose();

    private sealed class CapturingEmailSender : IEmailSender
    {
        public List<EmailMessage> Sent { get; } = new();

        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            Sent.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }

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

    private ParentInvitationRequestedConsumer InviteConsumer(IHubContext<BadgeNotificationHub> hub, IEmailSender email) => new(
        _db.NewContext(), hub, FallbackUserLocaleResolver.Instance, FallbackNotificationTextFactory.Instance, email, Protector,
        NullLogger<ParentInvitationRequestedConsumer>.Instance);

    private StudentParentGraceReminderConsumer GraceConsumer(IHubContext<BadgeNotificationHub> hub) => new(
        _db.NewContext(), hub, FallbackUserLocaleResolver.Instance, FallbackNotificationTextFactory.Instance,
        NullLogger<StudentParentGraceReminderConsumer>.Instance);

    private static ParentInvitationRequestedEvent Invite(string kind, Guid? id = null) => new()
    {
        EventId = Guid.NewGuid(),
        InvitationId = id ?? Guid.NewGuid(),
        SendCount = 1,
        StudentId = 21,
        StudentUserId = StudentSub,
        StudentFirstName = "Ayşe",
        ParentUserId = ParentSub,
        ParentEmail = "veli@example.com",
        ParentFullName = "Fatma Yılmaz",
        Kind = kind,
        ProtectedActivationUrl = kind == "NewParentActivation" ? Protector.Protect(SecretUrl) : null,
        ExpiresAt = DateTime.UtcNow.AddDays(7),
        OccurredAt = DateTime.UtcNow
    };

    // ---- #440 ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task NewParentActivation_SendsEmailWithLink_AndNoInAppNotification()
    {
        var email = new CapturingEmailSender();
        var hub = NewHub();

        await InviteConsumer(hub, email).Consume(Context(Invite("NewParentActivation")));

        email.Sent.Count.ShouldBe(1);
        email.Sent[0].To.ShouldBe("veli@example.com");
        email.Sent[0].TextBody.ShouldContain(SecretUrl);
        email.Sent[0].TextBody.ShouldContain("Hedef Okul");
        email.Sent[0].TextBody.ShouldContain("7 gün");
        email.Sent[0].HtmlBody!.ShouldContain("Hesabımı etkinleştir");
        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(0);
        hub.Clients.DidNotReceive().User(Arg.Any<string>());
    }

    [Fact]
    public async Task NewParentActivation_Redelivery_DoesNotSendTwice()
    {
        var email = new CapturingEmailSender();
        var e = Invite("NewParentActivation");

        await InviteConsumer(NewHub(), email).Consume(Context(e));
        await InviteConsumer(NewHub(), email).Consume(Context(e));

        email.Sent.Count.ShouldBe(1);
    }

    [Fact]
    public async Task Resend_SameInvitationId_NewEventId_SendsAgainWithNewLink()
    {
        var email = new CapturingEmailSender();
        var first = Invite("NewParentActivation");
        var resend = Invite("NewParentActivation", first.InvitationId);
        resend.SendCount = 2;
        resend.ProtectedActivationUrl = Protector.Protect("https://hedefokul.test/app/parent-activate#token=NEW");

        await InviteConsumer(NewHub(), email).Consume(Context(first));
        await InviteConsumer(NewHub(), email).Consume(Context(resend));
        await InviteConsumer(NewHub(), email).Consume(Context(resend)); // aynı EventId tekrar teslimi

        email.Sent.Count.ShouldBe(2);
        email.Sent[1].TextBody.ShouldContain("#token=NEW");
    }

    [Fact]
    public async Task ExistingParentApproval_Resend_NotifiesAgain()
    {
        var email = new CapturingEmailSender();
        var first = Invite("ExistingParentApproval");
        var resend = Invite("ExistingParentApproval", first.InvitationId);

        await InviteConsumer(NewHub(), email).Consume(Context(first));
        await InviteConsumer(NewHub(), email).Consume(Context(resend));

        email.Sent.Count.ShouldBe(2);
        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task LegacyEventWithoutEventId_FallsBackToInvitationId()
    {
        var email = new CapturingEmailSender();
        var e = Invite("ExistingParentApproval");
        e.EventId = Guid.Empty;

        await InviteConsumer(NewHub(), email).Consume(Context(e));
        await InviteConsumer(NewHub(), email).Consume(Context(e));

        email.Sent.Count.ShouldBe(1);
    }

    [Fact]
    public async Task UndecryptableLink_ThrowsNonRetryable_AndSendsNothing()
    {
        var email = new CapturingEmailSender();
        var e = Invite("NewParentActivation");
        e.ProtectedActivationUrl = "v1.AAAAAAAAAAAAAAAA.AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";

        await Should.ThrowAsync<InvalidParentInvitationException>(
            () => InviteConsumer(NewHub(), email).Consume(Context(e)));
        email.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task ExistingParentApproval_SendsEmailAndNotifiesParentOnce()
    {
        var email = new CapturingEmailSender();
        var hub = NewHub();
        var e = Invite("ExistingParentApproval");

        await InviteConsumer(hub, email).Consume(Context(e));
        await InviteConsumer(hub, email).Consume(Context(e)); // tekrar teslim

        email.Sent.Count.ShouldBe(1);
        email.Sent[0].TextBody.ShouldContain("Çocuklarım");
        email.Sent[0].TextBody.ShouldNotContain("http");
        await using var check = _db.NewContext();
        var rows = await check.Notifications.ToListAsync();
        rows.Count.ShouldBe(1);
        rows[0].Type.ShouldBe(ParentInvitationRequestedConsumer.InvitationReceivedType);
        rows[0].UserKeycloakId.ShouldBe(ParentSub);
        rows[0].SourceEventId.ShouldBe(e.EventId);
        rows[0].Body.ShouldContain("Ayşe");
        rows[0].Data!.ShouldContain("\"studentId\":21");
        hub.Clients.Received(1).User(ParentSub);
    }

    [Fact]
    public async Task NewParentActivation_WithoutUrl_ThrowsNonRetryableAndSendsNothing()
    {
        var email = new CapturingEmailSender();
        var e = Invite("NewParentActivation");
        e.ProtectedActivationUrl = null;

        await Should.ThrowAsync<InvalidParentInvitationException>(
            () => InviteConsumer(NewHub(), email).Consume(Context(e)));
        email.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task UnknownKind_ThrowsNonRetryable()
    {
        await Should.ThrowAsync<InvalidParentInvitationException>(
            () => InviteConsumer(NewHub(), new CapturingEmailSender()).Consume(Context(Invite("Bogus"))));
    }

    [Fact]
    public async Task ExistingParentApproval_WithoutParentSub_ThrowsSubUnresolved_AndSendsNoEmail()
    {
        var email = new CapturingEmailSender();
        var e = Invite("ExistingParentApproval");
        e.ParentUserId = "";

        await Should.ThrowAsync<RecipientSubUnresolvedException>(
            () => InviteConsumer(NewHub(), email).Consume(Context(e)));
        email.Sent.ShouldBeEmpty();
    }

    [Fact]
    public async Task LoggingEmailSender_Development_LogsFullMessageIncludingUrl()
    {
        var logger = new ListLogger<LoggingEmailSender>();
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(Environments.Development);

        await new LoggingEmailSender(env, logger).SendAsync(
            new EmailMessage("veli@example.com", "Konu", $"Bağlantı: {SecretUrl}"));

        logger.Entries.ShouldContain(x => x.Level == LogLevel.Information && x.Message.Contains(SecretUrl));
    }

    [Fact]
    public async Task LoggingEmailSender_NonDevelopment_WarnsWithoutUrlOrRecipient()
    {
        var logger = new ListLogger<LoggingEmailSender>();
        var env = Substitute.For<IHostEnvironment>();
        env.EnvironmentName.Returns(Environments.Production);

        await new LoggingEmailSender(env, logger).SendAsync(
            new EmailMessage("veli@example.com", "Konu", $"Bağlantı: {SecretUrl}"));

        logger.Entries.ShouldContain(x => x.Level == LogLevel.Warning && x.Message.Contains("No email provider configured"));
        logger.Entries.ShouldNotContain(x => x.Message.Contains("SECRET-TOKEN") || x.Message.Contains("veli@example.com"));
    }

    [Fact]
    public async Task Consumer_NeverLogsActivationUrl()
    {
        var logger = new ListLogger<ParentInvitationRequestedConsumer>();
        var consumer = new ParentInvitationRequestedConsumer(
            _db.NewContext(), NewHub(), FallbackUserLocaleResolver.Instance, FallbackNotificationTextFactory.Instance,
            new CapturingEmailSender(), Protector, logger);

        await consumer.Consume(Context(Invite("NewParentActivation")));

        logger.Entries.ShouldNotContain(x => x.Message.Contains("SECRET-TOKEN") || x.Message.Contains("veli@example.com"));
    }

    // ---- #441 ------------------------------------------------------------------------------------------------------

    private static StudentParentGraceReminderEvent Grace(int daysLeft, DateTime? ends = null) => new()
    {
        StudentId = 21,
        StudentUserId = StudentSub,
        DaysLeft = daysLeft,
        GraceEndsAt = ends ?? new DateTime(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc),
        OccurredAt = DateTime.UtcNow
    };

    [Fact]
    public async Task GraceReminder_NotifiesStudentWithParentRequiredLink()
    {
        var hub = NewHub();

        await GraceConsumer(hub).Consume(Context(Grace(7)));

        await using var check = _db.NewContext();
        var row = await check.Notifications.SingleAsync();
        row.Type.ShouldBe(StudentParentGraceReminderConsumer.NotificationType);
        row.UserKeycloakId.ShouldBe(StudentSub);
        row.Body.ShouldContain("7 gün");
        row.Data!.ShouldContain("/parent-required");
        hub.Clients.Received(1).User(StudentSub);
    }

    [Fact]
    public async Task GraceReminder_Redelivery_IsIdempotent_ButDifferentDaysLeftIsSeparate()
    {
        var hub = NewHub();
        var seven = Grace(7);

        await GraceConsumer(hub).Consume(Context(seven));
        await GraceConsumer(hub).Consume(Context(seven));
        await GraceConsumer(hub).Consume(Context(Grace(1)));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(2);
        hub.Clients.Received(2).User(StudentSub);
    }

    [Fact]
    public async Task GraceReminder_WithoutStudentSub_ThrowsSubUnresolved()
    {
        var e = Grace(1);
        e.StudentUserId = "";

        await Should.ThrowAsync<RecipientSubUnresolvedException>(
            () => GraceConsumer(NewHub()).Consume(Context(e)));
    }
}
