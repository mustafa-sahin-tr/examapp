using ExamApp.Api.Data;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #424 (epic #407 V6): veli erişim kayıtlarının KVKK saklama süresi — <c>ParentAccessAudit:RetentionDays</c>'ten eski
/// satırlar parti parti silinir, sınırdaki ve yeni satırlar kalır (<see cref="AdminDataAccessLogRetentionJobTests"/> deseni).
/// </summary>
public class ParentAccessAuditRetentionJobTests : IDisposable
{
    private static readonly DateTimeOffset Now = new(2026, 10, 7, 3, 45, 0, TimeSpan.Zero);
    private readonly TestDb _db = TestDb.Create();

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class Monitor(ParentAccessAuditOptions value) : IOptionsMonitor<ParentAccessAuditOptions>
    {
        public ParentAccessAuditOptions CurrentValue => value;
        public ParentAccessAuditOptions Get(string? name) => value;
        public IDisposable? OnChange(Action<ParentAccessAuditOptions, string?> listener) => null;
    }

    private ParentAccessAuditRetentionJob NewJob(AppDbContext ctx, int retentionDays = 180, int batchSize = 5000)
        => new(ctx, new Monitor(new ParentAccessAuditOptions { RetentionDays = retentionDays, DeleteBatchSize = batchSize }),
            new FixedClock(Now));

    private static ParentAccessAudit Row(DateTime at, string endpoint = ParentAccessEndpoints.ChildSummary) => new()
    {
        ParentId = 1,
        StudentId = 2,
        Endpoint = endpoint,
        At = at
    };

    [Fact]
    public async Task Deletes_only_rows_older_than_the_retention_period()
    {
        var cutoff = Now.UtcDateTime.AddDays(-180);
        await using (var seed = _db.NewContext())
        {
            seed.ParentAccessAudits.AddRange(
                Row(cutoff.AddDays(-400)),
                Row(cutoff.AddSeconds(-1), ParentAccessEndpoints.ChildTestResult),
                Row(cutoff.AddDays(-1), ParentAccessEndpoints.ChildSchedule),
                Row(cutoff),                    // tam sınır: kalır
                Row(cutoff.AddSeconds(1)),
                Row(Now.UtcDateTime.AddMinutes(-5)));
            await seed.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
            (await NewJob(ctx).PurgeExpiredAsync()).ShouldBe(3);

        await using var read = _db.NewContext();
        var remaining = await read.ParentAccessAudits.AsNoTracking().Select(r => r.At).ToListAsync();
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
                seed.ParentAccessAudits.Add(Row(old.AddMinutes(i)));
            seed.ParentAccessAudits.Add(Row(Now.UtcDateTime.AddDays(-10)));
            await seed.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
            (await NewJob(ctx, batchSize: 3).PurgeExpiredAsync()).ShouldBe(7); // 3 + 3 + 1

        await using var read = _db.NewContext();
        (await read.ParentAccessAudits.CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Retention_days_come_from_configuration()
    {
        await using (var seed = _db.NewContext())
        {
            seed.ParentAccessAudits.AddRange(Row(Now.UtcDateTime.AddDays(-31)), Row(Now.UtcDateTime.AddDays(-29)));
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
    public void Defaults_are_six_months_daily()
    {
        var options = new ParentAccessAuditOptions();
        options.RetentionDays.ShouldBe(180);
        options.Cron.ShouldBe("45 3 * * *");
        ParentAccessAuditOptions.SectionName.ShouldBe("ParentAccessAudit");
    }

    public void Dispose() => _db.Dispose();
}
