using BadgeService;
using BadgeService.Consumers;
using BadgeService.Services;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using static BadgeService.Tests.WorksheetCommentCoalescingTests;

namespace BadgeService.Tests;

/// <summary>
/// Issue #305 (dilim B) — birleştirmenin EŞZAMANLILIK garantileri gerçek PostgreSQL'de (SQLite tek bağlantıda yarış
/// üretemez): farklı EventId'lerin paralel birleştirmesinde sayaç kaybolmaz, ilk-yorum yarışı tek okunmamış satırla biter,
/// aynı EventId'nin paralel teslimi sayacı bir kez artırır.
/// </summary>
[Trait("Category", "Integration")]
public class WorksheetCommentCoalescingPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _pg = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public async ValueTask InitializeAsync()
    {
        await _pg.StartAsync();
        await using var db = NewDb();
        await db.Database.EnsureCreatedAsync();
    }

    public async ValueTask DisposeAsync() => await _pg.DisposeAsync();

    private BadgeDbContext NewDb() =>
        new(new DbContextOptionsBuilder<BadgeDbContext>().UseNpgsql(_pg.GetConnectionString()).Options);

    [Fact]
    public async Task Parallel_distinct_events_on_the_same_root_lose_no_counter_increments()
    {
        const int n = 8;
        var hub = NewHub();

        await Task.WhenAll(Enumerable.Range(0, n).Select(i => Task.Run(async () =>
        {
            await using var db = NewDb();
            await NewCreated(db, hub).Consume(Context(Created(100 + i)));
        })));

        await using var check = NewDb();
        var rows = await check.Notifications.ToListAsync();
        rows.Count.ShouldBe(1);
        rows[0].CoalescedCount.ShouldBe(n);
        rows[0].Title.ShouldBe($"{n} yeni yorum: Kesirler"); // son artıran son metni yazar
        (await check.NotificationEventLogs.CountAsync()).ShouldBe(n);
    }

    [Fact]
    public async Task Parallel_redelivery_of_the_same_event_increments_the_counter_once()
    {
        await using (var seed = NewDb())
            await NewCreated(seed, NewHub()).Consume(Context(Created(7)));
        var dup = Created(8);

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            await using var db = NewDb();
            await NewCreated(db, NewHub()).Consume(Context(dup));
        })));

        await using var check = NewDb();
        (await check.Notifications.SingleAsync()).CoalescedCount.ShouldBe(2);
        (await check.NotificationEventLogs.CountAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task Parallel_redelivery_of_the_first_event_creates_a_single_row()
    {
        var first = Created(7);

        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(async () =>
        {
            await using var db = NewDb();
            await NewCreated(db, NewHub()).Consume(Context(first));
        })));

        await using var check = NewDb();
        var row = await check.Notifications.SingleAsync();
        row.CoalescedCount.ShouldBe(1);
    }

    [Fact]
    public async Task Parallel_hide_and_merge_never_leave_the_author_name_or_overwrite_the_merged_text()
    {
        for (var round = 0; round < 5; round++)
        {
            var root = 500 + round;
            var first = 1000 + round * 10;
            await using (var seed = NewDb())
                await NewCreated(seed, NewHub()).Consume(Context(Created(first, root: root)));

            await Task.WhenAll(
                Task.Run(async () =>
                {
                    await using var db = NewDb();
                    await NewCreated(db, NewHub()).Consume(Context(Created(first + 1, root: root)));
                }),
                Task.Run(async () =>
                {
                    await using var db = NewDb();
                    var hide = new ExamApp.Foundation.Contracts.WorksheetCommentHiddenEvent
                    {
                        EventId = Guid.NewGuid(), CommentId = first, RootCommentId = root, WorksheetId = 100
                    };
                    await new WorksheetCommentHiddenConsumer(db, new UserLocaleResolver(db), Texts(),
                        Microsoft.Extensions.Logging.Abstractions.NullLogger<WorksheetCommentHiddenConsumer>.Instance)
                        .Consume(Context(hide));
                }));

            await using var check = NewDb();
            var row = await check.Notifications.SingleAsync(n => n.RootCommentId == root);
            row.CoalescedCount.ShouldBe(2);
            row.Title.ShouldBe("2 yeni yorum: Kesirler"); // koşullu nötrleştirme "N yeni" metnini ezmez
            row.Body.ShouldNotContain("Ayşe");
        }
    }
}
