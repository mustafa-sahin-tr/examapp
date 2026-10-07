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

/// <summary>
/// issue #423 (epic #407 V5): veli bildirimi consumer'ları — bağlantı kuruldu/kaldırıldı, gecikmiş ödev, test tamamlandı.
/// Alıcı kararı (yalnız Active bağlantılar) producer'dadır; burada tek-alıcılı event'in doğru alıcıya, doğru metinle,
/// idempotent biçimde yazıldığı ve sub çözülemediğinde sessizce kaybolmak yerine fırlatıldığı doğrulanır.
/// </summary>
public class ParentNotificationConsumersTests : IDisposable
{
    private const int ParentUser = 9101;
    private const int StudentUser = 9001;
    private const string ParentSub = "kc-parent-1";
    private const string StudentSub = "kc-student-1";

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

    private ParentLinkChangedConsumer LinkConsumer(IHubContext<BadgeNotificationHub> hub) => new(
        _db.NewContext(), hub, FallbackUserLocaleResolver.Instance, FallbackNotificationTextFactory.Instance,
        NullLogger<ParentLinkChangedConsumer>.Instance);

    private ParentHomeworkOverdueConsumer OverdueConsumer(IHubContext<BadgeNotificationHub> hub) => new(
        _db.NewContext(), hub, FallbackUserLocaleResolver.Instance, FallbackNotificationTextFactory.Instance,
        NullLogger<ParentHomeworkOverdueConsumer>.Instance);

    private ParentChildTestCompletedConsumer CompletedConsumer(IHubContext<BadgeNotificationHub> hub) => new(
        _db.NewContext(), hub, FallbackUserLocaleResolver.Instance, FallbackNotificationTextFactory.Instance,
        NullLogger<ParentChildTestCompletedConsumer>.Instance);

    private static ParentLinkedEvent Linked(string parentSub = ParentSub, string studentSub = StudentSub) => new()
    {
        EventId = Guid.NewGuid(),
        LinkId = 5,
        ParentId = 11,
        ParentUserId = ParentUser,
        StudentId = 21,
        StudentUserId = StudentUser,
        ParentKeycloakId = parentSub,
        StudentKeycloakId = studentSub,
        ParentDisplayName = "Fatma Y.",
        StudentDisplayName = "Ayşe K.",
        LinkedAtUtc = DateTime.UtcNow
    };

    private static ParentUnlinkedEvent Unlinked(string revokedBy) => new()
    {
        EventId = Guid.NewGuid(),
        LinkId = 5,
        ParentId = 11,
        ParentUserId = ParentUser,
        StudentId = 21,
        StudentUserId = StudentUser,
        ParentKeycloakId = ParentSub,
        StudentKeycloakId = StudentSub,
        ParentDisplayName = "Fatma Y.",
        StudentDisplayName = "Ayşe K.",
        RevokedByRole = revokedBy,
        RevokedByUserId = revokedBy == "Student" ? StudentUser : ParentUser,
        RevokedAtUtc = DateTime.UtcNow
    };

    private static ParentHomeworkOverdueEvent Overdue(string parentSub = ParentSub, string studentName = "Ayşe K.") => new()
    {
        EventId = Guid.NewGuid(),
        AssignmentId = 7,
        WorksheetId = 70,
        WorksheetName = "Kesirler Testi",
        StudentId = 21,
        StudentDisplayName = studentName,
        ParentId = 11,
        ParentUserId = ParentUser,
        ParentKeycloakId = parentSub,
        DueAtUtc = DateTime.UtcNow.AddHours(-1)
    };

    private static ParentChildTestCompletedEvent Completed(int parentUser = ParentUser, string parentSub = ParentSub) => new()
    {
        EventId = Guid.NewGuid(),
        TestInstanceId = 300,
        WorksheetId = 70,
        WorksheetName = "Kesirler Testi",
        StudentId = 21,
        StudentDisplayName = "Ayşe K.",
        ParentId = 11,
        ParentUserId = parentUser,
        ParentKeycloakId = parentSub,
        CorrectAnswers = 8,
        TotalQuestions = 10,
        Score = 80,
        CompletedAtUtc = DateTime.UtcNow
    };

    // NSubstitute: Clients.User(Arg.Any) tek alt-substitute paylaşır; hedeflemeyi User(sub) çağrılarıyla doğrula (alt-substitute çağrı listesiyle değil).
    private static Task Sent(IHubContext<BadgeNotificationHub> hub, string sub, int times)
    {
        hub.Clients.Received(times).User(sub);
        return Task.CompletedTask;
    }

    // ---- ParentLinked ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Linked_NotifiesParentAndStudent_EachWithOwnTypeAndSub()
    {
        var hub = NewHub();
        var e = Linked();

        await LinkConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var rows = await check.Notifications.OrderBy(n => n.Type).ToListAsync();
        rows.Count.ShouldBe(2);

        var toParent = rows.Single(n => n.Type == ParentLinkChangedConsumer.LinkedToParentType);
        toParent.UserId.ShouldBe(ParentUser);
        toParent.UserKeycloakId.ShouldBe(ParentSub);
        toParent.SourceEventId.ShouldBe(e.EventId);
        toParent.Body.ShouldBe("Ayşe K. sizi veli olarak onayladı.");
        toParent.Data!.ShouldContain("\"studentId\":21");

        var toStudent = rows.Single(n => n.Type == ParentLinkChangedConsumer.LinkedToStudentType);
        toStudent.UserId.ShouldBe(StudentUser);
        toStudent.UserKeycloakId.ShouldBe(StudentSub);
        toStudent.Body.ShouldBe("Velinle bağlantın kuruldu.");

        await Sent(hub, ParentSub, 1);
        await Sent(hub, StudentSub, 1);
    }

    [Fact]
    public async Task Linked_RedeliveredEvent_IsIdempotent()
    {
        var hub = NewHub();
        var e = Linked();

        await LinkConsumer(hub).Consume(Context(e));
        await LinkConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(2);
        await Sent(hub, ParentSub, 1);
        await Sent(hub, StudentSub, 1);
    }

    [Fact]
    public async Task Linked_MissingStudentSub_ThrowsAndRetryWritesOnlyTheMissingRecipient()
    {
        var hub = NewHub();
        var e = Linked(studentSub: "");

        // Çözülemeyen sub: sessizce sub'sız satır yazılmaz, fırlatılır (retry → badge-service_error).
        await Should.ThrowAsync<InvalidOperationException>(() => LinkConsumer(hub).Consume(Context(e)));
        await using (var mid = _db.NewContext())
        {
            var only = await mid.Notifications.SingleAsync();
            only.Type.ShouldBe(ParentLinkChangedConsumer.LinkedToParentType); // veliye yazılan kalır
        }

        // Retry: sub artık BadgeService verisinden çözülebiliyor (dil tercihi kaydı).
        await using (var seed = _db.NewContext())
        {
            seed.UserLocalePreferences.Add(new UserLocalePreference { UserId = StudentUser, KeycloakId = StudentSub, UpdatedAtUtc = DateTime.UtcNow });
            await seed.SaveChangesAsync();
        }
        await LinkConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(2);
        (await check.Notifications.SingleAsync(n => n.Type == ParentLinkChangedConsumer.LinkedToStudentType))
            .UserKeycloakId.ShouldBe(StudentSub);
        await Sent(hub, ParentSub, 1); // veliye push tekrarlanmadı
        await Sent(hub, StudentSub, 1);
    }

    [Fact]
    public async Task Linked_BlankNames_FallBackToLocalizedDefaults()
    {
        var e = Linked();
        e.StudentDisplayName = "";
        e.ParentDisplayName = "  ";

        await LinkConsumer(NewHub()).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.SingleAsync(n => n.Type == ParentLinkChangedConsumer.LinkedToParentType))
            .Body.ShouldBe("Çocuğunuz sizi veli olarak onayladı.");
    }

    // ---- ParentUnlinked ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Unlinked_ByStudent_NotifiesOnlyTheParent()
    {
        var hub = NewHub();

        await LinkConsumer(hub).Consume(Context(Unlinked("Student")));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe(ParentLinkChangedConsumer.UnlinkedToParentType);
        n.UserId.ShouldBe(ParentUser);
        n.UserKeycloakId.ShouldBe(ParentSub);
        n.Body.ShouldBe("Ayşe K. veli bağlantısını kaldırdı.");
        n.Data!.ShouldContain("\"studentId\":null"); // bağlantı artık yok: derin link çocuk seçmez
        await Sent(hub, ParentSub, 1);
        await Sent(hub, StudentSub, 0);
    }

    [Fact]
    public async Task Unlinked_ByParent_NotifiesOnlyTheStudent()
    {
        var hub = NewHub();

        await LinkConsumer(hub).Consume(Context(Unlinked("Parent")));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe(ParentLinkChangedConsumer.UnlinkedToStudentType);
        n.UserId.ShouldBe(StudentUser);
        n.Body.ShouldBe("Fatma Y. veli bağlantısını kaldırdı.");
        await Sent(hub, StudentSub, 1);
        await Sent(hub, ParentSub, 0);
    }

    [Fact]
    public async Task Unlinked_RedeliveredEvent_IsIdempotent()
    {
        var hub = NewHub();
        var e = Unlinked("Student");

        await LinkConsumer(hub).Consume(Context(e));
        await LinkConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(1);
        await Sent(hub, ParentSub, 1);
    }

    [Fact]
    public async Task Unlinked_UnknownRole_WritesNothingAndDoesNotThrow()
    {
        var hub = NewHub();

        await LinkConsumer(hub).Consume(Context(Unlinked("Admin")));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(0);
    }

    // ---- ParentHomeworkOverdue ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Overdue_CreatesParentNotificationAndPushes()
    {
        var hub = NewHub();
        var e = Overdue();

        await OverdueConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe(ParentHomeworkOverdueConsumer.NotificationType);
        n.UserId.ShouldBe(ParentUser);
        n.UserKeycloakId.ShouldBe(ParentSub);
        n.SourceEventId.ShouldBe(e.EventId);
        n.IsRead.ShouldBeFalse();
        n.Body.ShouldContain("Ayşe K.");
        n.Body.ShouldContain("Kesirler Testi");
        n.Data!.ShouldContain("\"studentId\":21");
        await Sent(hub, ParentSub, 1);
    }

    [Fact]
    public async Task Overdue_RedeliveredEvent_IsIdempotent()
    {
        var hub = NewHub();
        var e = Overdue();

        await OverdueConsumer(hub).Consume(Context(e));
        await OverdueConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(1);
        await Sent(hub, ParentSub, 1);
    }

    [Fact]
    public async Task Overdue_UnresolvableParentSub_Throws()
    {
        await Should.ThrowAsync<InvalidOperationException>(
            () => OverdueConsumer(NewHub()).Consume(Context(Overdue(parentSub: ""))));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Overdue_ExpiredInstance_UsesTimeRanOutText()
    {
        var e = Overdue();
        e.InstanceExpired = true;

        await OverdueConsumer(NewHub()).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe(ParentHomeworkOverdueConsumer.NotificationType);
        n.Title.ShouldBe("Test süresi doldu");
        n.Body.ShouldContain("süresi doldu");
    }

    [Fact]
    public async Task UnresolvableSub_IsADeterministicException_NotRetried()
    {
        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => OverdueConsumer(NewHub()).Consume(Context(Overdue(parentSub: ""))));
        ex.ShouldBeOfType<RecipientSubUnresolvedException>();
    }

    [Fact]
    public async Task Overdue_BlankStudentName_UsesDefaultChild()
    {
        await OverdueConsumer(NewHub()).Consume(Context(Overdue(studentName: "")));

        await using var check = _db.NewContext();
        (await check.Notifications.SingleAsync()).Body.ShouldContain("Çocuğunuz");
    }

    // ---- ParentChildTestCompleted ------------------------------------------------------------------------------------

    [Fact]
    public async Task Completed_CreatesNotificationWithChildTestAndScore()
    {
        var hub = NewHub();
        var e = Completed();

        await CompletedConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe(ParentChildTestCompletedConsumer.NotificationType);
        n.UserId.ShouldBe(ParentUser);
        n.Body.ShouldBe("Ayşe K., \"Kesirler Testi\" testini tamamladı, puan %80.");
        n.Data!.ShouldContain("\"score\":80");
        n.Data!.ShouldContain("\"studentId\":21");
        await Sent(hub, ParentSub, 1);
    }

    [Fact]
    public async Task Completed_RedeliveredEvent_IsIdempotent()
    {
        var hub = NewHub();
        var e = Completed();

        await CompletedConsumer(hub).Consume(Context(e));
        await CompletedConsumer(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(1);
        await Sent(hub, ParentSub, 1);
    }

    [Fact]
    public async Task Completed_TwoParents_GetTheirOwnNotificationsFromSeparateEvents()
    {
        var hub = NewHub();

        await CompletedConsumer(hub).Consume(Context(Completed(ParentUser, "kc-parent-1")));
        await CompletedConsumer(hub).Consume(Context(Completed(ParentUser + 1, "kc-parent-2")));

        await using var check = _db.NewContext();
        var rows = await check.Notifications.ToListAsync();
        rows.Count.ShouldBe(2);
        rows.Select(r => r.UserKeycloakId).OrderBy(s => s).ShouldBe(new[] { "kc-parent-1", "kc-parent-2" });
    }

    [Fact]
    public async Task Completed_SubResolvedFromBadgeServiceDataWhenEventHasNone()
    {
        await using (var seed = _db.NewContext())
        {
            seed.UserLocalePreferences.Add(new UserLocalePreference { UserId = ParentUser, KeycloakId = ParentSub, UpdatedAtUtc = DateTime.UtcNow });
            await seed.SaveChangesAsync();
        }
        var hub = NewHub();

        await CompletedConsumer(hub).Consume(Context(Completed(parentSub: "")));

        await using var check = _db.NewContext();
        (await check.Notifications.SingleAsync()).UserKeycloakId.ShouldBe(ParentSub);
        await Sent(hub, ParentSub, 1);
    }
}
