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
/// Issue #105 (dilim 2) — WorksheetCommentCreatedConsumer / WorksheetCommentRepliedConsumer: Notification + SignalR,
/// idempotency (aynı EventId iki kez), sub'ı boş alıcı atlanır, yerelleştirme.
/// </summary>
public class WorksheetCommentConsumersTests : IDisposable
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

    private static INotificationTextFactory Texts()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "Services", "BadgeService", "Resources")))
            root = root.Parent;
        using var files = new PhysicalFileProvider(Path.Combine(root!.FullName, "Services", "BadgeService", "Resources"));
        var store = JsonResourceStore.Load(files, "", throwOnDuplicateKeys: false, NullLogger.Instance);
        return new NotificationTextFactory(store, NullLogger<NotificationTextFactory>.Instance);
    }

    private WorksheetCommentCreatedConsumer NewCreated(IHubContext<BadgeNotificationHub> hub) =>
        new(_db.NewContext(), hub, new UserLocaleResolver(_db.NewContext()), Texts(),
            NullLogger<WorksheetCommentCreatedConsumer>.Instance);

    private WorksheetCommentRepliedConsumer NewReplied(IHubContext<BadgeNotificationHub> hub) =>
        new(_db.NewContext(), hub, new UserLocaleResolver(_db.NewContext()), Texts(),
            NullLogger<WorksheetCommentRepliedConsumer>.Instance);

    private static WorksheetCommentCreatedEvent Created(Guid? id = null, string sub = "kc-t1", int? questionId = null,
        string author = "Ayşe K.", string title = "Kesirler") => new()
    {
        EventId = id ?? Guid.NewGuid(), CommentId = 7, RootCommentId = 5, WorksheetId = 100, QuestionId = questionId,
        WorksheetTitle = title, AuthorRole = "Student", AuthorDisplayName = author,
        RecipientUserId = 10, RecipientKeycloakId = sub
    };

    private static WorksheetCommentRepliedEvent Replied(Guid? id = null, string sub = "kc-s1", string role = "Teacher",
        string author = "") => new()
    {
        EventId = id ?? Guid.NewGuid(), CommentId = 8, RootCommentId = 5, WorksheetId = 100, QuestionId = 3,
        WorksheetTitle = "Kesirler", AuthorRole = role, AuthorDisplayName = author,
        RecipientUserId = 20, RecipientKeycloakId = sub
    };

    private static string? PushedJson(IHubContext<BadgeNotificationHub> hub, string sub, string method)
    {
        var call = hub.Clients.User(sub).ReceivedCalls()
            .SingleOrDefault(c => c.GetMethodInfo().Name == "SendCoreAsync" && (string)c.GetArguments()[0]! == method);
        return call == null ? null : JsonSerializer.Serialize(((object?[])call.GetArguments()[1]!)[0]);
    }

    // ---- Created ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Created_writes_a_teacher_notification_and_pushes_it_to_the_keycloak_sub()
    {
        var hub = NewHub();
        var e = Created();

        await NewCreated(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe("WorksheetCommentCreated");
        n.UserId.ShouldBe(10);
        n.UserKeycloakId.ShouldBe("kc-t1");
        n.SourceEventId.ShouldBe(e.EventId);
        n.IsRead.ShouldBeFalse();
        n.Title.ShouldBe("Yeni yorum: Kesirler");
        n.Body.ShouldContain("Ayşe K.");

        using var data = JsonDocument.Parse(n.Data!);
        data.RootElement.GetProperty("worksheetId").GetInt32().ShouldBe(100);
        data.RootElement.GetProperty("commentId").GetInt32().ShouldBe(7);
        data.RootElement.GetProperty("rootCommentId").GetInt32().ShouldBe(5);
        data.RootElement.GetProperty("questionId").ValueKind.ShouldBe(JsonValueKind.Null);

        var pushed = PushedJson(hub, "kc-t1", "WorksheetCommentCreated");
        pushed.ShouldNotBeNull();
        using var push = JsonDocument.Parse(pushed);
        push.RootElement.GetProperty("notificationId").GetInt32().ShouldBe(n.Id);
        push.RootElement.GetProperty("worksheetTitle").GetString().ShouldBe("Kesirler");
        push.RootElement.GetProperty("commentId").GetInt32().ShouldBe(7);
    }

    [Fact]
    public async Task Created_question_thread_uses_the_question_body_and_carries_the_question_id()
    {
        await NewCreated(NewHub()).Consume(Context(Created(questionId: 3)));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Body.ShouldContain("soru");
        using var data = JsonDocument.Parse(n.Data!);
        data.RootElement.GetProperty("questionId").GetInt32().ShouldBe(3);
    }

    [Fact]
    public async Task Created_same_event_id_twice_is_idempotent_and_pushes_once()
    {
        var e = Created();
        var hub1 = NewHub();
        await NewCreated(hub1).Consume(Context(e));
        var hub2 = NewHub();
        await NewCreated(hub2).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(1);
        PushedJson(hub1, "kc-t1", "WorksheetCommentCreated").ShouldNotBeNull();
        hub2.Clients.User("kc-t1").ReceivedCalls().Count(c => c.GetMethodInfo().Name == "SendCoreAsync").ShouldBe(0);
    }

    [Fact]
    public async Task Created_different_event_ids_for_the_same_comment_make_separate_notifications()
    {
        await NewCreated(NewHub()).Consume(Context(Created()));
        await NewCreated(NewHub()).Consume(Context(Created()));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(2);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    public async Task Created_without_a_resolvable_sub_throws_so_retry_and_dead_letter_apply(string sub)
    {
        var hub = NewHub();

        await Should.ThrowAsync<InvalidOperationException>(
            () => NewCreated(hub).Consume(Context(Created(sub: sub))));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(0);
        hub.Clients.User(Arg.Any<string>()).ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Created_without_sub_resolves_it_from_the_locale_preference()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.UserLocalePreferences.Add(new UserLocalePreference
            {
                UserId = 10, KeycloakId = "kc-from-pref", Locale = "tr", UpdatedAtUtc = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }

        var hub = NewHub();
        await NewCreated(hub).Consume(Context(Created(sub: "")));

        await using var check = _db.NewContext();
        (await check.Notifications.SingleAsync()).UserKeycloakId.ShouldBe("kc-from-pref");
        PushedJson(hub, "kc-from-pref", "WorksheetCommentCreated").ShouldNotBeNull();
    }

    [Fact]
    public async Task Created_without_sub_resolves_it_from_an_earlier_notification_of_the_same_user()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.Notifications.Add(new Notification
            {
                UserId = 10, UserKeycloakId = "kc-from-notif", Type = "X", Title = "t", Body = "b"
            });
            await ctx.SaveChangesAsync();
        }

        await NewCreated(NewHub()).Consume(Context(Created(sub: "")));

        await using var check = _db.NewContext();
        (await check.Notifications.SingleAsync(n => n.Type == "WorksheetCommentCreated")).UserKeycloakId.ShouldBe("kc-from-notif");
    }

    [Fact]
    public async Task Duplicate_delivery_without_a_resolvable_sub_does_not_throw_once_processed()
    {
        var e = Created(sub: "kc-t1");
        await NewCreated(NewHub()).Consume(Context(e));

        e.RecipientKeycloakId = "";
        await NewCreated(NewHub()).Consume(Context(e)); // idempotency kontrolü sub çözümünden önce

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Created_sanitizes_and_truncates_author_and_worksheet_title()
    {
        var evil = "Ali‮​\u0000 Veli" + new string('x', 400);

        await NewCreated(NewHub()).Consume(Context(Created(author: evil, title: "Kes‮ir​ler\n" + new string('y', 500))));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        foreach (var text in new[] { n.Title, n.Body })
        {
            text.ShouldNotContain("‮");
            text.ShouldNotContain("​");
            text.ShouldNotContain("\u0000");
            text.ShouldNotContain("\n");
        }

        n.Title.Length.ShouldBeLessThanOrEqualTo(200);
        n.Body.Length.ShouldBeLessThanOrEqualTo(500);
        n.Title.ShouldStartWith("Yeni yorum: Kesirler");
        n.Body.ShouldStartWith("Ali Veli");
    }

    [Fact]
    public async Task Created_uses_the_recipients_english_locale_and_generic_fallbacks()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.UserLocalePreferences.Add(new UserLocalePreference
            {
                UserId = 10, KeycloakId = "kc-t1", Locale = "en", UpdatedAtUtc = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }

        await NewCreated(NewHub()).Consume(Context(Created(author: "", title: "")));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Title.ShouldStartWith("New comment");
        n.Body.ShouldContain("wrote");
    }

    [Fact]
    public async Task Created_without_locale_preference_defaults_to_turkish_with_fallback_names()
    {
        await NewCreated(NewHub()).Consume(Context(Created(author: "", title: "")));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Title.ShouldBe("Yeni yorum: bir sınav");
        n.Body.ShouldContain("Bir öğrenci");
    }

    // ---- Replied ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Replied_by_teacher_writes_a_student_notification_and_pushes_it()
    {
        var hub = NewHub();
        var e = Replied();

        await NewReplied(hub).Consume(Context(e));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe("WorksheetCommentReplied");
        n.UserId.ShouldBe(20);
        n.UserKeycloakId.ShouldBe("kc-s1");
        n.SourceEventId.ShouldBe(e.EventId);
        n.Title.ShouldBe("Öğretmenin cevap verdi: Kesirler");
        using var data = JsonDocument.Parse(n.Data!);
        data.RootElement.GetProperty("questionId").GetInt32().ShouldBe(3);
        data.RootElement.GetProperty("rootCommentId").GetInt32().ShouldBe(5);

        PushedJson(hub, "kc-s1", "WorksheetCommentReplied").ShouldNotBeNull();
    }

    [Fact]
    public async Task Replied_by_another_student_uses_the_student_text_with_the_short_name()
    {
        await NewReplied(NewHub()).Consume(Context(Replied(role: "Student", author: "Burak İ.")));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Type.ShouldBe("WorksheetCommentReplied");
        n.Body.ShouldContain("Burak İ.");
        n.Title.ShouldContain("Kesirler");
    }

    [Fact]
    public async Task Replied_same_event_id_twice_is_idempotent_and_pushes_once()
    {
        var e = Replied();
        await NewReplied(NewHub()).Consume(Context(e));
        var hub2 = NewHub();
        await NewReplied(hub2).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(1);
        hub2.Clients.User("kc-s1").ReceivedCalls().Count(c => c.GetMethodInfo().Name == "SendCoreAsync").ShouldBe(0);
    }

    [Fact]
    public async Task Replied_without_a_resolvable_sub_throws()
    {
        var hub = NewHub();

        await Should.ThrowAsync<InvalidOperationException>(
            () => NewReplied(hub).Consume(Context(Replied(sub: ""))));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(0);
        hub.Clients.User(Arg.Any<string>()).ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Replied_sanitizes_student_author_name_and_title()
    {
        await NewReplied(NewHub()).Consume(Context(Replied(role: "Student", author: "Bu‮rak​ İ.")));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Body.ShouldContain("Burak İ.");
        n.Body.ShouldNotContain("‮");
        n.Title.Length.ShouldBeLessThanOrEqualTo(200);
    }

    [Fact]
    public async Task Replied_with_english_locale_is_english()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.UserLocalePreferences.Add(new UserLocalePreference
            {
                UserId = 20, KeycloakId = "kc-s1", Locale = "en", UpdatedAtUtc = DateTime.UtcNow
            });
            await ctx.SaveChangesAsync();
        }

        await NewReplied(NewHub()).Consume(Context(Replied()));

        await using var check = _db.NewContext();
        (await check.Notifications.SingleAsync()).Title.ShouldStartWith("Your teacher replied");
    }

    [Fact]
    public async Task Created_and_replied_events_with_the_same_event_id_do_not_collide()
    {
        var id = Guid.NewGuid();
        await NewCreated(NewHub()).Consume(Context(Created(id)));
        await NewReplied(NewHub()).Consume(Context(Replied(id)));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(2);
    }

    public void Dispose() => _db.Dispose();
}
