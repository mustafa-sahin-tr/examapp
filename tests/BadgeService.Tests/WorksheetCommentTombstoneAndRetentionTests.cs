using BadgeService;
using BadgeService.Consumers;
using BadgeService.Entities;
using BadgeService.Services;
using BadgeService.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using static BadgeService.Tests.WorksheetCommentCoalescingTests;

namespace BadgeService.Tests;

/// <summary>
/// Issue #305 review — D4 sıra bozulması (HiddenCommentTombstone), koşullu nötrleştirme, UserKeycloakId'li birleştirme,
/// NotificationEventLog saklama süresi.
/// </summary>
public class WorksheetCommentTombstoneAndRetentionTests : IDisposable
{
    private readonly BadgeTestDb _db = BadgeTestDb.Create();

    public void Dispose() => _db.Dispose();

    private static WorksheetCommentHiddenEvent Hidden(int comment, int root = 5) => new()
    {
        EventId = Guid.NewGuid(), CommentId = comment, RootCommentId = root, WorksheetId = 100
    };

    private WorksheetCommentHiddenConsumer NewHidden(BadgeDbContext db) =>
        new(db, new UserLocaleResolver(db), Texts(), NullLogger<WorksheetCommentHiddenConsumer>.Instance);

    // ---- Tombstone (D4 sıra bozulması) --------------------------------------------------------------------------

    [Fact]
    public async Task Hide_consumer_writes_a_tombstone_even_when_no_notification_exists_yet_and_is_idempotent()
    {
        var e = Hidden(7);
        await NewHidden(_db.NewContext()).Consume(Context(e));
        await NewHidden(_db.NewContext()).Consume(Context(e));

        await using var check = _db.NewContext();
        (await check.HiddenCommentTombstones.SingleAsync()).CommentId.ShouldBe(7);
    }

    [Fact]
    public async Task Created_after_the_hide_event_writes_the_neutral_text_and_pushes_it()
    {
        await NewHidden(_db.NewContext()).Consume(Context(Hidden(7)));

        var hub = NewHub();
        await NewCreated(_db.NewContext(), hub).Consume(Context(Created(7, author: "Ayşe K.")));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Title.ShouldBe("Bir yorum kaldırıldı");
        n.Body.ShouldNotContain("Ayşe");
        var call = hub.Clients.User("kc-t1").ReceivedCalls().Single(c => c.GetMethodInfo().Name == "SendCoreAsync");
        System.Text.Json.JsonSerializer.Serialize(((object?[])call.GetArguments()[1]!)[0]).ShouldNotContain("Ayşe");
    }

    [Fact]
    public async Task Replied_after_the_hide_event_writes_the_neutral_text()
    {
        await NewHidden(_db.NewContext()).Consume(Context(Hidden(30, root: 9)));

        await NewReplied(_db.NewContext(), NewHub())
            .Consume(Context(Replied(30, root: 9, role: "Student", author: "Burak İ.")));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Title.ShouldBe("Bir yorum kaldırıldı");
        n.Body.ShouldNotContain("Burak");
    }

    [Fact]
    public async Task Created_for_a_comment_that_was_not_hidden_keeps_the_author_name()
    {
        await NewHidden(_db.NewContext()).Consume(Context(Hidden(99)));

        await NewCreated(_db.NewContext(), NewHub()).Consume(Context(Created(7)));

        await using var check = _db.NewContext();
        (await check.Notifications.SingleAsync()).Body.ShouldContain("Ayşe K.");
    }

    // ---- Koşullu nötrleştirme -------------------------------------------------------------------------------------

    [Fact]
    public async Task Neutralisation_does_not_overwrite_a_row_that_was_merged_in_the_meantime()
    {
        await NewCreated(_db.NewContext(), NewHub()).Consume(Context(Created(7)));
        await NewCreated(_db.NewContext(), NewHub()).Consume(Context(Created(8))); // satır artık "2 yeni yorum", Latest=8

        await NewHidden(_db.NewContext()).Consume(Context(Hidden(7)));

        await using var check = _db.NewContext();
        var n = await check.Notifications.SingleAsync();
        n.Title.ShouldBe("2 yeni yorum: Kesirler");
        n.CoalescedCount.ShouldBe(2);
    }

    [Fact]
    public async Task Neutralisation_update_is_conditional_on_count_and_latest_comment()
    {
        // Koşul doğrudan: CoalescedCount>1 ya da başka yoruma işaret eden satır nötrleştirme ifadesinden etkilenmez.
        await using (var ctx = _db.NewContext())
        {
            ctx.Notifications.AddRange(
                new Notification { UserId = 10, UserKeycloakId = "kc-t1", Type = "WorksheetCommentCreated", Title = "a", Body = "a", LatestCommentId = 7, CoalescedCount = 3, SourceEventId = Guid.NewGuid() },
                new Notification { UserId = 10, UserKeycloakId = "kc-t1", Type = "WorksheetCommentCreated", Title = "b", Body = "b", LatestCommentId = 8, CoalescedCount = 1, SourceEventId = Guid.NewGuid() });
            await ctx.SaveChangesAsync();
        }

        await using var db = _db.NewContext();
        var n = await CommentHiddenNeutralizer.NeutralizeAsync(db, new UserLocaleResolver(db), Texts(), 7, CancellationToken.None);

        n.ShouldBe(0);
    }

    // ---- UserKeycloakId ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Same_user_id_with_a_different_keycloak_sub_does_not_merge()
    {
        await NewCreated(_db.NewContext(), NewHub()).Consume(Context(Created(7)));
        var other = Created(8);
        other.RecipientKeycloakId = "kc-other";

        await NewCreated(_db.NewContext(), NewHub()).Consume(Context(other));

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(2);
        (await check.Notifications.AllAsync(n => n.CoalescedCount == 1)).ShouldBeTrue();
    }

    // ---- NotificationEventLog retention ---------------------------------------------------------------------------

    private sealed class StaticOptions(NotificationEventLogRetentionOptions value) : IOptionsMonitor<NotificationEventLogRetentionOptions>
    {
        public NotificationEventLogRetentionOptions CurrentValue => value;
        public NotificationEventLogRetentionOptions Get(string? name) => value;
        public IDisposable OnChange(Action<NotificationEventLogRetentionOptions, string> listener) => new Noop();
        private sealed class Noop : IDisposable { public void Dispose() { } }
    }

    private async Task SeedLogsAsync(params int[] ageDays)
    {
        await using var ctx = _db.NewContext();
        foreach (var d in ageDays)
            ctx.NotificationEventLogs.Add(new NotificationEventLog
            {
                Type = "WorksheetCommentCreated", EventId = Guid.NewGuid(), NotificationId = 1, ProcessedAt = DateTime.UtcNow.AddDays(-d)
            });
        await ctx.SaveChangesAsync();
    }

    [Fact]
    public async Task Event_log_rows_older_than_thirty_days_by_default_are_deleted()
    {
        new NotificationEventLogRetentionOptions().RetentionDays.ShouldBe(30);
        await SeedLogsAsync(40, 31, 5);

        int deleted;
        await using (var ctx = _db.NewContext())
            deleted = await new NotificationEventLogRetentionJob(ctx, new StaticOptions(new())).PurgeExpiredAsync();

        deleted.ShouldBe(2);
        await using var check = _db.NewContext();
        (await check.NotificationEventLogs.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Event_log_retention_days_and_batch_size_are_configurable()
    {
        await SeedLogsAsync(10, 9, 8, 1);

        int deleted;
        await using (var ctx = _db.NewContext())
            deleted = await new NotificationEventLogRetentionJob(ctx,
                new StaticOptions(new NotificationEventLogRetentionOptions { RetentionDays = 7, DeleteBatchSize = 2 })).PurgeExpiredAsync();

        deleted.ShouldBe(3); // 2 partide (2 + 1)
        await using var check = _db.NewContext();
        (await check.NotificationEventLogs.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Event_log_retention_leaves_notifications_untouched()
    {
        await NewCreated(_db.NewContext(), NewHub()).Consume(Context(Created(7)));
        await using (var ctx = _db.NewContext())
            await ctx.NotificationEventLogs.ExecuteUpdateAsync(s => s.SetProperty(l => l.ProcessedAt, DateTime.UtcNow.AddDays(-90)));

        await using (var ctx = _db.NewContext())
            (await new NotificationEventLogRetentionJob(ctx, new StaticOptions(new())).PurgeExpiredAsync()).ShouldBe(1);

        await using var check = _db.NewContext();
        (await check.Notifications.CountAsync()).ShouldBe(1);
        // satırı açan event (Type, SourceEventId) ile hâlâ korunur: tekrar teslim no-op
        var first = await check.Notifications.SingleAsync();
        await NewCreated(_db.NewContext(), NewHub()).Consume(Context(Created(7, id: first.SourceEventId)));
        await using var check2 = _db.NewContext();
        (await check2.Notifications.SingleAsync()).CoalescedCount.ShouldBe(1);
    }
}
