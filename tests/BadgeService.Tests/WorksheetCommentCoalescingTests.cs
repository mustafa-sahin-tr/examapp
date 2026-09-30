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
/// Issue #305 (dilim B) — (alıcı, Type, RootCommentId) bazında bildirim birleştirme + issue #326 D4 (gizlenen yorumun adı):
/// okunmamışsa güncelleme, okunmuşsa yeni satır, sayaç/başlık (tr/en), push içeriği, idempotency, nötrleştirme.
/// </summary>
public class WorksheetCommentCoalescingTests : IDisposable
{
    private readonly BadgeTestDb _db = BadgeTestDb.Create();

    internal static ConsumeContext<T> Context<T>(T message) where T : class
    {
        var ctx = Substitute.For<ConsumeContext<T>>();
        ctx.Message.Returns(message);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    internal static IHubContext<BadgeNotificationHub> NewHub()
    {
        var hub = Substitute.For<IHubContext<BadgeNotificationHub>>();
        hub.Clients.User(Arg.Any<string>()).SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);
        return hub;
    }

    internal static INotificationTextFactory Texts()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !Directory.Exists(Path.Combine(root.FullName, "Services", "BadgeService", "Resources")))
            root = root.Parent;
        using var files = new PhysicalFileProvider(Path.Combine(root!.FullName, "Services", "BadgeService", "Resources"));
        var store = JsonResourceStore.Load(files, "", throwOnDuplicateKeys: false, NullLogger.Instance);
        return new NotificationTextFactory(store, NullLogger<NotificationTextFactory>.Instance);
    }

    internal static WorksheetCommentCreatedConsumer NewCreated(BadgeDbContext db, IHubContext<BadgeNotificationHub> hub) =>
        new(db, hub, new UserLocaleResolver(db), Texts(), NullLogger<WorksheetCommentCreatedConsumer>.Instance);

    internal static WorksheetCommentRepliedConsumer NewReplied(BadgeDbContext db, IHubContext<BadgeNotificationHub> hub) =>
        new(db, hub, new UserLocaleResolver(db), Texts(), NullLogger<WorksheetCommentRepliedConsumer>.Instance);

    internal static WorksheetCommentCreatedEvent Created(int comment, Guid? id = null, int root = 5, int? questionOrder = null,
        string author = "Ayşe K.") => new()
    {
        EventId = id ?? Guid.NewGuid(), CommentId = comment, RootCommentId = root, WorksheetId = 100, QuestionId = 3,
        QuestionOrder = questionOrder, WorksheetTitle = "Kesirler", AuthorRole = "Student", AuthorDisplayName = author,
        RecipientUserId = 10, RecipientKeycloakId = "kc-t1"
    };

    internal static WorksheetCommentRepliedEvent Replied(int comment, Guid? id = null, int root = 5, string role = "Teacher",
        string author = "") => new()
    {
        EventId = id ?? Guid.NewGuid(), CommentId = comment, RootCommentId = root, WorksheetId = 100, QuestionId = 3,
        WorksheetTitle = "Kesirler", AuthorRole = role, AuthorDisplayName = author,
        RecipientUserId = 20, RecipientKeycloakId = "kc-s1"
    };

    private async Task SetEnglishAsync(int userId, string sub)
    {
        await using var ctx = _db.NewContext();
        ctx.UserLocalePreferences.Add(new UserLocalePreference
        {
            UserId = userId, KeycloakId = sub, Locale = "en", UpdatedAtUtc = DateTime.UtcNow
        });
        await ctx.SaveChangesAsync();
    }

    private static JsonDocument PushOf(IHubContext<BadgeNotificationHub> hub, string sub, string method)
    {
        var call = hub.Clients.User(sub).ReceivedCalls()
            .Single(c => c.GetMethodInfo().Name == "SendCoreAsync" && (string)c.GetArguments()[0]! == method);
        return JsonDocument.Parse(JsonSerializer.Serialize(((object?[])call.GetArguments()[1]!)[0]));
    }

    // ---- Birleştirme ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Created_second_comment_on_the_same_root_updates_the_unread_row_instead_of_adding_one()
    {
        var first = Created(7, questionOrder: 4);
        await NewCreated(_db.NewContext(), NewHub()).Consume(Context(first));
        DateTime firstCreatedAt;
        await using (var c = _db.NewContext())
            firstCreatedAt = (await c.Notifications.SingleAsync()).CreatedAt;
        await Task.Delay(20);

        var hub = NewHub();
        await NewCreated(_db.NewContext(), hub).Consume(Context(Created(9, questionOrder: 4, author: "Burak İ.")));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.CoalescedCount.ShouldBe(2);
        n.SourceEventId.ShouldBe(first.EventId); // satırı açan ilk event; (Type, SourceEventId) unique index'i bozulmaz
        n.LatestCommentId.ShouldBe(9);
        n.RootCommentId.ShouldBe(5);
        n.IsRead.ShouldBeFalse();
        n.CreatedAt.ShouldBeGreaterThan(firstCreatedAt);
        n.Title.ShouldBe("2 yeni yorum: Kesirler");
        n.Body.ShouldBe("4. soru hakkında 2 yeni yorum yazıldı.");
        n.Body.ShouldNotContain("Ayşe");
        n.Body.ShouldNotContain("Burak");

        using var data = JsonDocument.Parse(n.Data!);
        data.RootElement.GetProperty("commentId").GetInt32().ShouldBe(9);
        data.RootElement.GetProperty("rootCommentId").GetInt32().ShouldBe(5);
        data.RootElement.GetProperty("questionOrder").GetInt32().ShouldBe(4);
        data.RootElement.GetProperty("questionId").GetInt32().ShouldBe(3);

        // push her yeni yorumda gider: aynı notificationId, güncel sayaç ve başlık
        using var push = PushOf(hub, "kc-t1", "WorksheetCommentCreated");
        push.RootElement.GetProperty("notificationId").GetInt32().ShouldBe(n.Id);
        push.RootElement.GetProperty("coalescedCount").GetInt32().ShouldBe(2);
        push.RootElement.GetProperty("title").GetString().ShouldBe("2 yeni yorum: Kesirler");
        push.RootElement.GetProperty("commentId").GetInt32().ShouldBe(9);
    }

    [Fact]
    public async Task Created_counter_and_title_follow_the_number_of_merged_comments()
    {
        for (var i = 0; i < 3; i++)
            await NewCreated(_db.NewContext(), NewHub()).Consume(Context(Created(10 + i)));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.CoalescedCount.ShouldBe(3);
        n.Title.ShouldBe("3 yeni yorum: Kesirler");
        n.Body.ShouldBe("3 yeni yorum veya soru yazıldı.");
        (await check.NotificationEventLogs.CountAsync()).ShouldBe(3);
    }

    [Fact]
    public async Task Created_merged_text_uses_the_recipients_english_locale()
    {
        await SetEnglishAsync(10, "kc-t1");
        await NewCreated(_db.NewContext(), NewHub()).Consume(Context(Created(7)));
        await NewCreated(_db.NewContext(), NewHub()).Consume(Context(Created(8)));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Title.ShouldBe("2 new comments: Kesirler");
        n.Body.ShouldBe("2 new comments or questions were posted.");
    }

    [Fact]
    public async Task Created_after_the_row_was_read_opens_a_new_row()
    {
        await NewCreated(_db.NewContext(), NewHub()).Consume(Context(Created(7)));
        await using (var c = _db.NewContext())
        {
            var row = await c.Notifications.SingleAsync();
            row.IsRead = true;
            await c.SaveChangesAsync();
        }

        await NewCreated(_db.NewContext(), NewHub()).Consume(Context(Created(8)));

        await using var check = _db.NewContext();
        var list = await check.Notifications.OrderBy(n => n.Id).ToListAsync();
        list.Count.ShouldBe(2);
        list[0].IsRead.ShouldBeTrue();
        list[0].CoalescedCount.ShouldBe(1);
        list[0].LatestCommentId.ShouldBe(7); // okunmuş satıra dokunulmaz
        list[1].IsRead.ShouldBeFalse();
        list[1].CoalescedCount.ShouldBe(1);
        list[1].LatestCommentId.ShouldBe(8);
        list[1].Title.ShouldBe("Yeni yorum: Kesirler");
    }

    [Fact]
    public async Task Created_different_roots_and_different_recipients_do_not_merge()
    {
        await NewCreated(_db.NewContext(), NewHub()).Consume(Context(Created(7, root: 5)));
        await NewCreated(_db.NewContext(), NewHub()).Consume(Context(Created(8, root: 6)));
        var other = Created(9, root: 5);
        other.RecipientUserId = 11;
        other.RecipientKeycloakId = "kc-t2";
        await NewCreated(_db.NewContext(), NewHub()).Consume(Context(other));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(3);
        (await check.Notifications.AllAsync(n => n.CoalescedCount == 1)).ShouldBeTrue();
    }

    [Fact]
    public async Task Replied_merges_per_root_with_a_nameless_reply_text_and_en_locale()
    {
        await NewReplied(_db.NewContext(), NewHub()).Consume(Context(Replied(20)));
        await NewReplied(_db.NewContext(), NewHub()).Consume(Context(Replied(21, role: "Student", author: "Burak İ.")));

        await using (var check = _db.NewContext())
        {
            var n = await check.Notifications.SingleAsync();
            n.Type.ShouldBe("WorksheetCommentReplied");
            n.CoalescedCount.ShouldBe(2);
            n.Title.ShouldBe("2 yeni cevap: Kesirler");
            n.Body.ShouldNotContain("Burak");
            n.LatestCommentId.ShouldBe(21);
        }

        await SetEnglishAsync(20, "kc-s1");
        await NewReplied(_db.NewContext(), NewHub()).Consume(Context(Replied(22)));
        await using var check2 = _db.NewContext();
        (await check2.Notifications.SingleAsync()).Title.ShouldBe("3 new replies: Kesirler");
    }

    [Fact]
    public async Task Created_and_replied_for_the_same_root_are_separate_notification_types()
    {
        await NewCreated(_db.NewContext(), NewHub()).Consume(Context(Created(7)));
        var sameRecipientReplied = Replied(8);
        sameRecipientReplied.RecipientUserId = 10;
        sameRecipientReplied.RecipientKeycloakId = "kc-t1";
        await NewReplied(_db.NewContext(), NewHub()).Consume(Context(sameRecipientReplied));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(2);
    }

    // ---- Idempotency ------------------------------------------------------------------------------------------

    [Fact]
    public async Task Redelivery_of_a_merged_event_does_not_increment_the_counter_or_push_again()
    {
        await NewCreated(_db.NewContext(), NewHub()).Consume(Context(Created(7)));
        var second = Created(8);
        await NewCreated(_db.NewContext(), NewHub()).Consume(Context(second));

        var hub = NewHub();
        await NewCreated(_db.NewContext(), hub).Consume(Context(second)); // aynı EventId, birleştirilmiş event

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.CoalescedCount.ShouldBe(2);
        (await check.NotificationEventLogs.CountAsync()).ShouldBe(2);
        hub.Clients.User(Arg.Any<string>()).ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public async Task Redelivery_of_the_row_opening_event_after_merging_is_a_no_op()
    {
        var first = Created(7);
        await NewCreated(_db.NewContext(), NewHub()).Consume(Context(first));
        await NewCreated(_db.NewContext(), NewHub()).Consume(Context(Created(8)));

        await NewCreated(_db.NewContext(), NewHub()).Consume(Context(first));

        await using var check = _db.NewContext();
        (await check.Notifications.SingleAsync()).CoalescedCount.ShouldBe(2);
    }

    [Fact]
    public async Task Redelivery_of_a_replied_event_is_a_no_op()
    {
        await NewReplied(_db.NewContext(), NewHub()).Consume(Context(Replied(20)));
        var second = Replied(21);
        await NewReplied(_db.NewContext(), NewHub()).Consume(Context(second));
        await NewReplied(_db.NewContext(), NewHub()).Consume(Context(second));

        await using var check = _db.NewContext();
        (await check.Notifications.SingleAsync()).CoalescedCount.ShouldBe(2);
    }

    [Fact]
    public async Task A_second_unread_row_for_the_same_recipient_type_and_root_is_rejected_by_the_unique_index()
    {
        // Eşzamanlı ilk-yorum yarışının DB güvencesi: iki okunmamış satır olamaz (ikincisi retry'da birleştirir).
        await NewCreated(_db.NewContext(), NewHub()).Consume(Context(Created(7)));

        await using var ctx = _db.NewContext();
        ctx.Notifications.Add(new Notification
        {
            UserId = 10, UserKeycloakId = "kc-t1", Type = "WorksheetCommentCreated", Title = "t", Body = "b", RootCommentId = 5, SourceEventId = Guid.NewGuid()
        });
        await Should.ThrowAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    // ---- Dilimden önceki satırlar ------------------------------------------------------------------------------

    [Fact]
    public async Task Rows_without_root_comment_id_never_merge()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.Notifications.Add(new Notification
            {
                UserId = 10, UserKeycloakId = "kc-t1", Type = "WorksheetCommentCreated", Title = "eski", Body = "eski",
                SourceEventId = Guid.NewGuid()
            });
            await ctx.SaveChangesAsync();
        }

        await NewCreated(_db.NewContext(), NewHub()).Consume(Context(Created(7)));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(2);
    }

    // ---- D4: gizlenen yorum -------------------------------------------------------------------------------------

    private WorksheetCommentHiddenConsumer NewHidden(BadgeDbContext db) =>
        new(db, new UserLocaleResolver(db), Texts(), NullLogger<WorksheetCommentHiddenConsumer>.Instance);

    private static WorksheetCommentHiddenEvent Hidden(int comment, int root = 5) => new()
    {
        EventId = Guid.NewGuid(), CommentId = comment, RootCommentId = root, WorksheetId = 100
    };

    [Fact]
    public async Task Hidden_comment_neutralises_the_author_name_in_the_notification_for_read_and_unread_rows()
    {
        await NewCreated(_db.NewContext(), NewHub()).Consume(Context(Created(7, root: 5, author: "Ayşe K.")));
        await NewCreated(_db.NewContext(), NewHub()).Consume(Context(Created(8, root: 6, author: "Ayşe K.")));
        await using (var c = _db.NewContext())
        {
            var read = await c.Notifications.SingleAsync(n => n.RootCommentId == 6);
            read.IsRead = true;
            await c.SaveChangesAsync();
        }

        await NewHidden(_db.NewContext()).Consume(Context(Hidden(7, 5)));
        await NewHidden(_db.NewContext()).Consume(Context(Hidden(8, 6)));

        await using var check = _db.NewContext();
        foreach (var n in await check.Notifications.ToListAsync())
        {
            n.Title.ShouldBe("Bir yorum kaldırıldı");
            n.Body.ShouldNotContain("Ayşe");
            n.Body.ShouldBe("Bu bildirimle ilgili yorum moderatör tarafından kaldırıldı.");
        }
    }

    [Fact]
    public async Task Hidden_uses_the_recipients_english_locale_and_is_idempotent()
    {
        await SetEnglishAsync(10, "kc-t1");
        await NewCreated(_db.NewContext(), NewHub()).Consume(Context(Created(7)));
        var hidden = Hidden(7);

        await NewHidden(_db.NewContext()).Consume(Context(hidden));
        await NewHidden(_db.NewContext()).Consume(Context(hidden));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Title.ShouldBe("A comment was removed");
        n.Body.ShouldBe("The comment related to this notification was removed by a moderator.");
    }

    [Fact]
    public async Task Hidden_only_touches_notifications_pointing_at_that_comment()
    {
        await NewCreated(_db.NewContext(), NewHub()).Consume(Context(Created(7, root: 5)));
        await NewCreated(_db.NewContext(), NewHub()).Consume(Context(Created(8, root: 6)));

        await NewHidden(_db.NewContext()).Consume(Context(Hidden(7, 5)));

        await using var check = _db.NewContext();
        (await check.Notifications.SingleAsync(n => n.RootCommentId == 6)).Body.ShouldContain("Ayşe K.");
        (await check.Notifications.SingleAsync(n => n.RootCommentId == 5)).Title.ShouldBe("Bir yorum kaldırıldı");
    }

    [Fact]
    public async Task Hidden_leaves_merged_rows_alone_because_their_text_has_no_author_name_and_reply_rows_are_neutralised()
    {
        await NewCreated(_db.NewContext(), NewHub()).Consume(Context(Created(7)));
        await NewCreated(_db.NewContext(), NewHub()).Consume(Context(Created(8)));
        await NewReplied(_db.NewContext(), NewHub()).Consume(Context(Replied(30, root: 9, role: "Student", author: "Burak İ.")));

        await NewHidden(_db.NewContext()).Consume(Context(Hidden(8, 5))); // birleşik satırın en son yorumu
        await NewHidden(_db.NewContext()).Consume(Context(Hidden(30, 9)));

        await using var check = _db.NewContext();
        var merged = await check.Notifications.SingleAsync(n => n.RootCommentId == 5);
        merged.Title.ShouldBe("2 yeni yorum: Kesirler");
        merged.Body.ShouldNotContain("Ayşe");
        var reply = await check.Notifications.SingleAsync(n => n.RootCommentId == 9);
        reply.Title.ShouldBe("Bir yorum kaldırıldı");
        reply.Body.ShouldNotContain("Burak");
    }

    [Fact]
    public async Task Hidden_finds_pre_migration_rows_through_data_comment_id()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.Notifications.Add(new Notification
            {
                UserId = 10, UserKeycloakId = "kc-t1", Type = "WorksheetCommentCreated", Title = "Yeni yorum: Kesirler",
                Body = "Ayşe K. bir yorum veya soru yazdı.",
                Data = JsonSerializer.Serialize(new { worksheetId = 100, questionId = (int?)null, questionOrder = (int?)null, commentId = 7, rootCommentId = 5 }),
                SourceEventId = Guid.NewGuid()
            });
            await ctx.SaveChangesAsync();
        }

        await NewHidden(_db.NewContext()).Consume(Context(Hidden(7)));

        await using var check = _db.NewContext();
        (await check.Notifications.SingleAsync()).Body.ShouldNotContain("Ayşe");
    }

    public void Dispose() => _db.Dispose();
}
