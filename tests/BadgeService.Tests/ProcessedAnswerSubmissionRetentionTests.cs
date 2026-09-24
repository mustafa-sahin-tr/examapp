using BadgeService.Entities;
using BadgeService.Services;
using BadgeService.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BadgeService.Tests;

/// <summary>issue #279, item 3: <c>ProcessedAnswerSubmissions</c> saklama süresi + periyodik temizleme.</summary>
public class ProcessedAnswerSubmissionRetentionTests : IDisposable
{
    private readonly BadgeTestDb _db = BadgeTestDb.Create();

    public void Dispose() => _db.Dispose();

    private static IOptionsMonitor<ProcessedAnswerSubmissionRetentionOptions> Options(
        int retentionDays = 30, int batchSize = 5_000) => new StaticOptionsMonitor(
            new ProcessedAnswerSubmissionRetentionOptions { RetentionDays = retentionDays, DeleteBatchSize = batchSize });

    private sealed class StaticOptionsMonitor(ProcessedAnswerSubmissionRetentionOptions value)
        : IOptionsMonitor<ProcessedAnswerSubmissionRetentionOptions>
    {
        public ProcessedAnswerSubmissionRetentionOptions CurrentValue => value;
        public ProcessedAnswerSubmissionRetentionOptions Get(string? name) => value;
        public IDisposable OnChange(Action<ProcessedAnswerSubmissionRetentionOptions, string> listener) => new NoopDisposable();
        private sealed class NoopDisposable : IDisposable { public void Dispose() { } }
    }

    [Fact]
    public async Task Rows_older_than_the_retention_window_are_deleted()
    {
        var now = DateTime.UtcNow;
        await using (var ctx = _db.NewContext())
        {
            ctx.ProcessedAnswerSubmissions.AddRange(
                new ProcessedAnswerSubmission { EventId = Guid.NewGuid(), UserId = 1, ProcessedAt = now.AddDays(-40) },
                new ProcessedAnswerSubmission { EventId = Guid.NewGuid(), UserId = 1, ProcessedAt = now.AddDays(-31) },
                new ProcessedAnswerSubmission { EventId = Guid.NewGuid(), UserId = 1, ProcessedAt = now.AddDays(-5) });
            await ctx.SaveChangesAsync();
        }

        int deleted;
        await using (var ctx = _db.NewContext())
            deleted = await new ProcessedAnswerSubmissionRetentionJob(ctx, Options(retentionDays: 30)).PurgeExpiredAsync();

        deleted.ShouldBe(2);
        await using var check = _db.NewContext();
        check.ProcessedAnswerSubmissions.Count().ShouldBe(1);
        (await check.ProcessedAnswerSubmissions.SingleAsync()).ProcessedAt.ShouldBe(now.AddDays(-5), TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task Nothing_is_deleted_when_all_rows_are_within_the_window()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.ProcessedAnswerSubmissions.Add(
                new ProcessedAnswerSubmission { EventId = Guid.NewGuid(), UserId = 1, ProcessedAt = DateTime.UtcNow.AddDays(-1) });
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = _db.NewContext();
        (await new ProcessedAnswerSubmissionRetentionJob(ctx2, Options(retentionDays: 30)).PurgeExpiredAsync()).ShouldBe(0);
        ctx2.ProcessedAnswerSubmissions.Count().ShouldBe(1);
    }

    [Fact]
    public async Task Deletion_proceeds_in_batches_when_more_rows_are_expired_than_one_batch()
    {
        var old = DateTime.UtcNow.AddDays(-100);
        await using (var ctx = _db.NewContext())
        {
            for (var i = 0; i < 7; i++)
                ctx.ProcessedAnswerSubmissions.Add(new ProcessedAnswerSubmission { EventId = Guid.NewGuid(), UserId = 1, ProcessedAt = old });
            await ctx.SaveChangesAsync();
        }

        await using var ctx2 = _db.NewContext();
        var deleted = await new ProcessedAnswerSubmissionRetentionJob(ctx2, Options(retentionDays: 30, batchSize: 3)).PurgeExpiredAsync();

        deleted.ShouldBe(7);
        await using var check = _db.NewContext();
        check.ProcessedAnswerSubmissions.Count().ShouldBe(0);
    }

    [Fact]
    public void Default_retention_is_thirty_days()
        => new ProcessedAnswerSubmissionRetentionOptions().RetentionDays.ShouldBe(30);
}
