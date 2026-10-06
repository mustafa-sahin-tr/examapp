using System.Text.Json;
using BadgeService;
using BadgeService.Consumers;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Services;
using BadgeService.Tests.Support;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Localization;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging.Abstractions;

namespace BadgeService.Tests;

/// <summary>
/// Issue #106 (dilim b) — DirectMessageSentConsumer / DirectMessageReportedConsumer: Notification + SignalR (Keycloak sub /
/// admin grubu), idempotency (aynı EventId iki kez), aynı konuşmada okunmamış bildirim birleştirme, sub çözümü ve hata yolu,
/// yerelleştirme, mesaj gövdesinin hiçbir yerde bulunmaması.
/// </summary>
public class DirectMessageConsumersTests : IDisposable
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
        hub.Clients.Group(Arg.Any<string>()).SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        return hub;
    }

    private static INotificationTextFactory Texts()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "Services", "BadgeService", "Resources")))
            root = root.Parent;
        using var files = new PhysicalFileProvider(Path.Combine(root!.FullName, "Services", "BadgeService", "Resources"));
        var store = JsonResourceStore.Load(files, "", throwOnDuplicateKeys: false, NullLogger.Instance);
        return new NotificationTextFactory(store, NullLogger<NotificationTextFactory>.Instance);
    }

    private DirectMessageSentConsumer NewSent(IHubContext<BadgeNotificationHub> hub) =>
        new(_db.NewContext(), hub, new UserLocaleResolver(_db.NewContext()), Texts(),
            NullLogger<DirectMessageSentConsumer>.Instance);

    private DirectMessageReportedConsumer NewReported(IHubContext<BadgeNotificationHub> hub) =>
        new(_db.NewContext(), hub, Texts(), NullLogger<DirectMessageReportedConsumer>.Instance);

    private static DirectMessageSentEvent Sent(Guid? id = null, string role = "Student", string name = "Ayşe K.",
        int recipient = 10, string sub = "kc-t1", int conversation = 5, int message = 7) => new()
    {
        EventId = id ?? Guid.NewGuid(), ConversationId = conversation, MessageId = message, SenderRole = role,
        SenderDisplayName = name, RecipientUserId = recipient, RecipientKeycloakId = sub, CreatedAtUtc = DateTime.UtcNow
    };

    private static DirectMessageReportedEvent Reported(Guid? id = null, string role = "Student", int report = 3, int conversation = 5) => new()
    {
        EventId = id ?? Guid.NewGuid(), ReportId = report, ConversationId = conversation, ReporterRole = role,
        CreatedAtUtc = DateTime.UtcNow
    };

    private static List<(string Method, string Json)> UserPushes(IHubContext<BadgeNotificationHub> hub, string sub) =>
        hub.Clients.User(sub).ReceivedCalls()
            .Where(c => c.GetMethodInfo().Name == "SendCoreAsync")
            .Select(c => ((string)c.GetArguments()[0]!, JsonSerializer.Serialize(((object?[])c.GetArguments()[1]!)[0])))
            .ToList();

    // ---- Sent -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Sent_student_to_teacher_writes_notification_and_pushes_to_the_keycloak_sub()
    {
        var hub = NewHub();
        var e = Sent();

        await NewSent(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe("DirectMessageReceived");
        n.UserId.ShouldBe(10);
        n.UserKeycloakId.ShouldBe("kc-t1");
        n.SourceEventId.ShouldBe(e.EventId);
        n.IsRead.ShouldBeFalse();
        n.CoalescedCount.ShouldBe(1);
        n.Title.ShouldContain("Ayşe K.");
        using var data = JsonDocument.Parse(n.Data!);
        data.RootElement.GetProperty("conversationId").GetInt32().ShouldBe(5);
        data.RootElement.GetProperty("messageId").GetInt32().ShouldBe(7);
        data.RootElement.GetProperty("senderRole").GetString().ShouldBe("Student");

        hub.Clients.Received(1).User("kc-t1");
        hub.Clients.DidNotReceive().User("kc-other");
        var push = UserPushes(hub, "kc-t1").Single();
        push.Method.ShouldBe("DirectMessageReceived");
        using var pushed = JsonDocument.Parse(push.Json);
        pushed.RootElement.GetProperty("notificationId").GetInt32().ShouldBe(n.Id);
        pushed.RootElement.GetProperty("conversationId").GetInt32().ShouldBe(5);
        pushed.RootElement.GetProperty("senderRole").GetString().ShouldBe("Student");
        pushed.RootElement.GetProperty("coalescedCount").GetInt32().ShouldBe(1);
    }

    [Fact]
    public async Task Sent_teacher_to_student_targets_the_student_sub_with_teacher_text()
    {
        var hub = NewHub();

        await NewSent(hub).Consume(Context(Sent(role: "Teacher", name: "Selin A.", recipient: 20, sub: "kc-s1")));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.UserId.ShouldBe(20);
        n.UserKeycloakId.ShouldBe("kc-s1");
        using var data = JsonDocument.Parse(n.Data!);
        data.RootElement.GetProperty("senderRole").GetString().ShouldBe("Teacher");
        hub.Clients.Received(1).User("kc-s1");
        hub.Clients.DidNotReceive().User("kc-t1"); // yalnız alıcının sub'ı hedeflenir
        UserPushes(hub, "kc-s1").Single().Method.ShouldBe("DirectMessageReceived");
    }

    [Fact]
    public async Task Sent_empty_name_falls_back_to_localized_default()
    {
        await NewSent(NewHub()).Consume(Context(Sent(name: "  ")));
        await using var check = _db.NewContext();
        (await check.Notifications.SingleAsync()).Title.ShouldContain("Bir öğrenci");
    }

    [Fact]
    public async Task Sent_same_event_id_twice_is_idempotent_and_pushes_once()
    {
        var e = Sent();
        var hub1 = NewHub();
        await NewSent(hub1).Consume(Context(e));
        var hub2 = NewHub();
        await NewSent(hub2).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(1);
        (await check.Notifications.SingleAsync()).CoalescedCount.ShouldBe(1); // tekrar teslim sayacı artırmaz
        UserPushes(hub1, "kc-t1").Count.ShouldBe(1);
        UserPushes(hub2, "kc-t1").ShouldBeEmpty();
    }

    [Fact]
    public async Task Sent_second_message_in_the_same_conversation_coalesces_into_the_unread_notification()
    {
        var hub = NewHub();
        await NewSent(hub).Consume(Context(Sent(message: 7)));
        await NewSent(hub).Consume(Context(Sent(message: 8)));
        await NewSent(hub).Consume(Context(Sent(message: 9)));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.CoalescedCount.ShouldBe(3);
        n.LatestCommentId.ShouldBe(9);
        n.Title.ShouldContain("3");
        n.Title.ShouldContain("Ayşe K."); // tek karşı taraf: ad kalır
        using var data = JsonDocument.Parse(n.Data!);
        data.RootElement.GetProperty("messageId").GetInt32().ShouldBe(9);

        var pushes = UserPushes(hub, "kc-t1");
        pushes.Count.ShouldBe(3); // her teslim push eder (UI sayaç tazeler); bildirim satırı tek
        using var last = JsonDocument.Parse(pushes[^1].Json);
        last.RootElement.GetProperty("coalescedCount").GetInt32().ShouldBe(3);
    }

    [Fact]
    public async Task Sent_different_conversations_or_recipients_or_read_notification_do_not_coalesce()
    {
        var hub = NewHub();
        await NewSent(hub).Consume(Context(Sent(conversation: 5)));
        await NewSent(hub).Consume(Context(Sent(conversation: 6)));                            // başka konuşma
        await NewSent(hub).Consume(Context(Sent(conversation: 5, recipient: 11, sub: "kc-t2"))); // başka alıcı

        await using (var ctx = _db.NewContext())
        {
            (await ctx.Notifications.CountAsync()).ShouldBe(3);
            // Konuşma 5 / alıcı 10 bildirimi okundu -> sonraki mesaj yeni satır açar.
            await ctx.Notifications.Where(n => n.UserId == 10 && n.RootCommentId == 5)
                .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));
        }

        await NewSent(hub).Consume(Context(Sent(conversation: 5, message: 20)));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(4);
        (await check.Notifications.CountAsync(n => n.UserId == 10 && n.RootCommentId == 5 && !n.IsRead)).ShouldBe(1);
    }

    [Fact]
    public async Task Sent_blank_sub_is_resolved_from_badge_data()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.UserLocalePreferences.Add(new UserLocalePreference { UserId = 10, KeycloakId = "kc-from-locale", Locale = "tr" });
            await ctx.SaveChangesAsync();
        }
        var hub = NewHub();

        await NewSent(hub).Consume(Context(Sent(sub: "")));

        await using var check = _db.NewContext();
        (await check.Notifications.SingleAsync()).UserKeycloakId.ShouldBe("kc-from-locale");
        UserPushes(hub, "kc-from-locale").Count.ShouldBe(1);
    }

    [Fact]
    public async Task Sent_unresolvable_sub_throws_for_retry_and_dead_letter_and_writes_nothing()
    {
        var hub = NewHub();

        await Should.ThrowAsync<InvalidOperationException>(
            () => NewSent(hub).Consume(Context(Sent(sub: "", recipient: 999))));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(0);
        hub.Clients.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Sent_notification_and_push_never_contain_message_body_fields()
    {
        var hub = NewHub();
        await NewSent(hub).Consume(Context(Sent()));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        var all = string.Join(' ', n.Title, n.Body, n.Data, UserPushes(hub, "kc-t1").Single().Json).ToLowerInvariant();
        all.ShouldNotContain("\"body\":\"gizli");
        JsonDocument.Parse(n.Data!).RootElement.TryGetProperty("body", out _).ShouldBeFalse();
    }

    // ---- Reported ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task Reported_creates_one_admin_notification_and_pushes_to_the_admin_group()
    {
        var hub = NewHub();
        var e = Reported(role: "Teacher");

        await NewReported(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe("DirectMessageReported");
        n.UserId.ShouldBe(0);
        n.UserKeycloakId.ShouldBeNull();
        n.SourceEventId.ShouldBe(e.EventId);
        n.Body.ShouldContain("öğretmen");
        using var data = JsonDocument.Parse(n.Data!);
        data.RootElement.GetProperty("reportId").GetInt32().ShouldBe(3);
        data.RootElement.GetProperty("conversationId").GetInt32().ShouldBe(5);

        await hub.Clients.Group(BadgeNotificationHub.AdminGroup).Received(1).SendCoreAsync(
            "DirectMessageReported", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
        hub.Clients.User(Arg.Any<string>()).ReceivedCalls().ShouldBeEmpty(); // kullanıcıya değil yalnız admin grubuna
    }

    [Fact]
    public async Task Reported_same_event_twice_is_idempotent_and_pushes_once()
    {
        var e = Reported();
        var hub1 = NewHub();
        await NewReported(hub1).Consume(Context(e));
        var hub2 = NewHub();
        await NewReported(hub2).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync(n => n.Type == "DirectMessageReported")).ShouldBe(1);
        await hub1.Clients.Group(BadgeNotificationHub.AdminGroup).Received(1).SendCoreAsync(
            "DirectMessageReported", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
        await hub2.Clients.Group(BadgeNotificationHub.AdminGroup).DidNotReceive().SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Reported_distinct_reports_each_get_their_own_notification()
    {
        await NewReported(NewHub()).Consume(Context(Reported(report: 1)));
        await NewReported(NewHub()).Consume(Context(Reported(report: 2)));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync(n => n.Type == "DirectMessageReported")).ShouldBe(2);
    }

    public void Dispose() => _db.Dispose();
}
