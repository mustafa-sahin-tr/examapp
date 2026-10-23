using ExamApp.Api.Data;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// issue #335: yorum moderasyon geçmişinin saklama süresi — gizle/aç olayları <c>RetentionDays</c>'ten, reddedilen denemeler
/// <c>DeniedRetentionDays</c>'ten eski olunca parti parti silinir; yeni satırlar kalır
/// (<see cref="ParentAccessAuditRetentionJobTests"/> deseni).
/// </summary>
public class WorksheetCommentModerationHistoryRetentionJobTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 23, 4, 0, 0, TimeSpan.Zero);
    private readonly TestDb _db = TestDb.Create();

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Monitor(CommentModerationHistoryOptions value) : IOptionsMonitor<CommentModerationHistoryOptions>
    {
        public CommentModerationHistoryOptions CurrentValue => value;
        public CommentModerationHistoryOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<CommentModerationHistoryOptions, string?> listener) => null;
    }

    private WorksheetCommentModerationHistoryRetentionJob NewJob(AppDbContext ctx, int retentionDays = 730,
        int deniedRetentionDays = 90, int batchSize = 5000)
        => new(ctx, new Monitor(new CommentModerationHistoryOptions
        {
            RetentionDays = retentionDays, DeniedRetentionDays = deniedRetentionDays, DeleteBatchSize = batchSize
        }), new FixedClock(Now));

    private static WorksheetCommentModerationEvent Row(WorksheetCommentModerationAction action, DateTime at) => new()
    {
        Action = action,
        WorksheetCommentId = 1,
        WorksheetId = 1,
        ActorUserId = 7,
        ActorRole = WorksheetCommentModerationActorRoles.Teacher,
        Reason = action == WorksheetCommentModerationAction.DeniedNotModerator ? null : "neden",
        CreatedAt = at
    };

    private async Task SeedAsync(params WorksheetCommentModerationEvent[] rows)
    {
        await using var seed = _db.NewContext();
        seed.WorksheetCommentModerationEvents.AddRange(rows);
        await seed.SaveChangesAsync();
    }

    [Fact]
    public async Task Deletes_each_action_type_with_its_own_cutoff_and_keeps_newer_rows()
    {
        var now = Now.UtcDateTime;
        var moderationCutoff = now.AddDays(-730);
        var deniedCutoff = now.AddDays(-90);
        await SeedAsync(
            Row(WorksheetCommentModerationAction.Hide, moderationCutoff.AddDays(-1)),        // silinir
            Row(WorksheetCommentModerationAction.Unhide, moderationCutoff.AddSeconds(-1)),   // silinir
            Row(WorksheetCommentModerationAction.Hide, moderationCutoff),                    // tam sınır: kalır
            Row(WorksheetCommentModerationAction.Unhide, deniedCutoff.AddDays(-30)),         // gizle/aç 2 yıl: kalır
            Row(WorksheetCommentModerationAction.DeniedNotModerator, deniedCutoff.AddDays(-1)),  // silinir
            Row(WorksheetCommentModerationAction.DeniedNotModerator, moderationCutoff.AddDays(-5)), // silinir
            Row(WorksheetCommentModerationAction.DeniedNotModerator, deniedCutoff),          // tam sınır: kalır
            Row(WorksheetCommentModerationAction.DeniedNotModerator, now.AddMinutes(-5)));   // kalır

        await using (var ctx = _db.NewContext())
            (await NewJob(ctx).PurgeExpiredAsync()).ShouldBe(4);

        await using var read = _db.NewContext();
        var remaining = await read.WorksheetCommentModerationEvents.AsNoTracking().OrderBy(m => m.Id)
            .Select(m => new { m.Action, m.CreatedAt }).ToListAsync();
        remaining.Count.ShouldBe(4);
        remaining.Where(r => r.Action != WorksheetCommentModerationAction.DeniedNotModerator)
            .ShouldAllBe(r => r.CreatedAt >= moderationCutoff);
        remaining.Where(r => r.Action == WorksheetCommentModerationAction.DeniedNotModerator)
            .ShouldAllBe(r => r.CreatedAt >= deniedCutoff);
    }

    [Fact]
    public async Task Deletes_in_batches_until_nothing_old_is_left()
    {
        var old = Now.UtcDateTime.AddDays(-800);
        var rows = new List<WorksheetCommentModerationEvent>();
        for (var i = 0; i < 4; i++)
            rows.Add(Row(WorksheetCommentModerationAction.Hide, old.AddMinutes(i)));
        for (var i = 0; i < 5; i++)
            rows.Add(Row(WorksheetCommentModerationAction.DeniedNotModerator, old.AddMinutes(i)));
        rows.Add(Row(WorksheetCommentModerationAction.Hide, Now.UtcDateTime.AddDays(-1)));
        await SeedAsync(rows.ToArray());

        await using (var ctx = _db.NewContext())
            (await NewJob(ctx, batchSize: 2).PurgeExpiredAsync()).ShouldBe(9);

        await using var read = _db.NewContext();
        (await read.WorksheetCommentModerationEvents.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Retention_days_come_from_configuration()
    {
        var now = Now.UtcDateTime;
        await SeedAsync(
            Row(WorksheetCommentModerationAction.Hide, now.AddDays(-31)),
            Row(WorksheetCommentModerationAction.Hide, now.AddDays(-29)),
            Row(WorksheetCommentModerationAction.DeniedNotModerator, now.AddDays(-8)),
            Row(WorksheetCommentModerationAction.DeniedNotModerator, now.AddDays(-6)));

        await using (var ctx = _db.NewContext())
            (await NewJob(ctx, retentionDays: 30, deniedRetentionDays: 7).PurgeExpiredAsync()).ShouldBe(2);
    }

    [Fact]
    public async Task Empty_table_is_a_no_op()
    {
        await using var ctx = _db.NewContext();
        (await NewJob(ctx).PurgeExpiredAsync()).ShouldBe(0);
    }

    [Fact]
    public void Defaults_are_two_years_ninety_days_daily()
    {
        var options = new CommentModerationHistoryOptions();
        options.RetentionDays.ShouldBe(730);
        options.DeniedRetentionDays.ShouldBe(90);
        options.DeleteBatchSize.ShouldBe(5000);
        options.Cron.ShouldBe("0 4 * * *");
        CommentModerationHistoryOptions.SectionName.ShouldBe("CommentModerationHistory");
    }

    public void Dispose() => _db.Dispose();
}
