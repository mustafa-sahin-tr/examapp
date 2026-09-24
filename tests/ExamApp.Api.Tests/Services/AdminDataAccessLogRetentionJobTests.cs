using ExamApp.Api.Data;
using ExamApp.Api.Services.AdminUsers;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #262: KVKK saklama süresi — <c>RetentionDays</c>'ten eski audit satırları (Served + RateLimited) parti parti silinir,
/// sınırdaki ve yeni satırlar kalır.
/// </summary>
public class AdminDataAccessLogRetentionJobTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 3, 30, 0, TimeSpan.Zero);
    private readonly TestDb _db = TestDb.Create();

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Monitor(AdminDataAccessLogOptions value) : IOptionsMonitor<AdminDataAccessLogOptions>
    {
        public AdminDataAccessLogOptions CurrentValue => value;
        public AdminDataAccessLogOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<AdminDataAccessLogOptions, string?> listener) => null;
    }

    private AdminDataAccessLogRetentionJob NewJob(AppDbContext ctx, int retentionDays = 180, int batchSize = 5000)
        => new(ctx, new Monitor(new AdminDataAccessLogOptions { RetentionDays = retentionDays, DeleteBatchSize = batchSize }),
            new FixedClock(Now));

    private static AdminDataAccessLog Row(DateTime occurredAtUtc, AdminDataAccessOutcome outcome = AdminDataAccessOutcome.Served) => new()
    {
        ActorKeycloakId = "kc-admin",
        Resource = AdminDataAccessResource.StudentList,
        Page = 1,
        PageSize = 20,
        Outcome = outcome,
        OccurredAtUtc = occurredAtUtc
    };

    [Fact]
    public async Task Deletes_only_rows_older_than_the_retention_period()
    {
        var cutoff = Now.UtcDateTime.AddDays(-180);
        await using (var seed = _db.NewContext())
        {
            seed.AdminDataAccessLogs.AddRange(
                Row(cutoff.AddDays(-400)),
                Row(cutoff.AddSeconds(-1)),
                Row(cutoff.AddDays(-1), AdminDataAccessOutcome.RateLimited), // 429 satırları da aynı kurala tabi
                Row(cutoff),                                                  // tam sınır: kalır
                Row(cutoff.AddSeconds(1)),
                Row(Now.UtcDateTime.AddMinutes(-5), AdminDataAccessOutcome.RateLimited));
            await seed.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
            (await NewJob(ctx).PurgeExpiredAsync()).ShouldBe(3);

        await using var read = _db.NewContext();
        var remaining = await read.AdminDataAccessLogs.AsNoTracking().Select(r => r.OccurredAtUtc).ToListAsync();
        remaining.Count.ShouldBe(3);
        remaining.ShouldAllBe(t => t >= cutoff);
    }

    [Fact]
    public async Task Deletes_in_batches_until_nothing_old_is_left()
    {
        var old = Now.UtcDateTime.AddDays(-200);
        await using (var seed = _db.NewContext())
        {
            for (var i = 0; i < 7; i++)
                seed.AdminDataAccessLogs.Add(Row(old.AddMinutes(i)));
            seed.AdminDataAccessLogs.Add(Row(Now.UtcDateTime.AddDays(-10)));
            await seed.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
            (await NewJob(ctx, batchSize: 3).PurgeExpiredAsync()).ShouldBe(7); // 3 + 3 + 1

        await using var read = _db.NewContext();
        (await read.AdminDataAccessLogs.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Retention_days_come_from_configuration()
    {
        await using (var seed = _db.NewContext())
        {
            seed.AdminDataAccessLogs.AddRange(Row(Now.UtcDateTime.AddDays(-31)), Row(Now.UtcDateTime.AddDays(-29)));
            await seed.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
            (await NewJob(ctx, retentionDays: 30).PurgeExpiredAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Empty_table_is_a_no_op()
    {
        await using var ctx = _db.NewContext();
        (await NewJob(ctx).PurgeExpiredAsync()).ShouldBe(0);
    }

    [Fact]
    public void Default_retention_is_six_months()
        => new AdminDataAccessLogOptions().RetentionDays.ShouldBe(180);

    public void Dispose() => _db.Dispose();
}
