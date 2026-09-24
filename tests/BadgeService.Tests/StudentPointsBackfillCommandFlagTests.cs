using BadgeService;
using BadgeService.Commands;
using BadgeService.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace BadgeService.Tests;

/// <summary>
/// Issue #243: <c>backfill-student-points</c> Production guard. See
/// <see cref="StudentPointsBackfillCommand"/>'s XML doc for the two-layer design
/// (<c>--allow-production</c> to run at all, <c>--confirm</c> to actually write).
/// </summary>
public class StudentPointsBackfillCommandFlagTests
{
    private sealed class FakeEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "BadgeService.Tests";
        public string ContentRootPath { get; set; } = ".";
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static IHostEnvironment Env(string name) => new FakeEnvironment { EnvironmentName = name };

    // ---- ParseArgs ----

    [Fact]
    public void ParseArgs_recognizes_all_three_flags_independently()
    {
        var parsed = StudentPointsBackfillCommand.ParseArgs(
            new[] { "backfill-student-points", "--dry-run", "--allow-production", "--confirm" });

        parsed.DryRun.ShouldBeTrue();
        parsed.AllowProduction.ShouldBeTrue();
        parsed.Confirm.ShouldBeTrue();
    }

    [Fact]
    public void ParseArgs_defaults_all_flags_to_false()
    {
        var parsed = StudentPointsBackfillCommand.ParseArgs(new[] { "backfill-student-points" });

        parsed.DryRun.ShouldBeFalse();
        parsed.AllowProduction.ShouldBeFalse();
        parsed.Confirm.ShouldBeFalse();
    }

    [Fact]
    public void ParseArgs_rejects_unknown_flags()
        => Should.Throw<ArgumentException>(() =>
            StudentPointsBackfillCommand.ParseArgs(new[] { "backfill-student-points", "--nuke-everything" }));

    // ---- IsAllowedEnvironment: flag x environment matrix ----

    [Theory]
    [InlineData("Development", false, true)]
    [InlineData("Development", true, true)]
    [InlineData("Staging", false, true)]
    [InlineData("Staging", true, true)]
    [InlineData("Production", false, false)] // refused without --allow-production, same as pre-#243
    [InlineData("Production", true, true)] // allowed only with --allow-production
    public void IsAllowedEnvironment_matrix(string environmentName, bool allowProduction, bool expectedAllowed)
        => StudentPointsBackfillCommand.IsAllowedEnvironment(Env(environmentName), allowProduction)
            .ShouldBe(expectedAllowed);

    // ---- ResolveEffectiveDryRun: Production can never silently write ----

    [Theory]
    [InlineData("Development", false, false, false)] // Dev: flag is honored as-is
    [InlineData("Development", true, false, true)]
    [InlineData("Staging", false, false, false)] // Staging: same as Dev
    [InlineData("Staging", true, true, true)] // even --confirm doesn't force a write outside Production
    [InlineData("Production", false, false, true)] // Production, no --confirm → forced dry-run
    [InlineData("Production", false, true, false)] // Production + --confirm, no explicit --dry-run → real write
    [InlineData("Production", true, true, true)] // Production, explicit --dry-run always wins over --confirm
    public void ResolveEffectiveDryRun_matrix(
        string environmentName, bool dryRunRequested, bool confirm, bool expectedDryRun)
        => StudentPointsBackfillCommand.ResolveEffectiveDryRun(Env(environmentName), dryRunRequested, confirm)
            .ShouldBe(expectedDryRun);

    // ---- Dry-run never writes, regardless of environment ----

    [Fact]
    public async Task Dry_run_reports_the_pending_count_but_writes_nothing()
    {
        using var db = BadgeTestDb.Create();
        await using (var seed = db.NewContext())
        {
            seed.StudentQuestionAggregates.AddRange(
                new BadgeService.Entities.StudentQuestionAggregate { Id = Guid.NewGuid(), UserId = 1, TotalPoints = 10, LastUpdatedUtc = DateTime.UtcNow },
                new BadgeService.Entities.StudentQuestionAggregate { Id = Guid.NewGuid(), UserId = 2, TotalPoints = 20, LastUpdatedUtc = DateTime.UtcNow });
            await seed.SaveChangesAsync();
        }

        await using (var ctx = db.NewContext())
        {
            var reported = await StudentPointsBackfillCommand.RunAsync(ctx, NullLogger.Instance, dryRun: true);
            reported.ShouldBe(2); // still reports how many WOULD be updated
        }

        await using var check = db.NewContext();
        (await check.OutboxMessages.AnyAsync()).ShouldBeFalse(); // but nothing was actually queued
    }

    [Fact]
    public async Task A_real_run_after_a_dry_run_writes_the_same_count_it_reported()
    {
        using var db = BadgeTestDb.Create();
        await using (var seed = db.NewContext())
        {
            seed.StudentQuestionAggregates.Add(
                new BadgeService.Entities.StudentQuestionAggregate { Id = Guid.NewGuid(), UserId = 1, TotalPoints = 10, LastUpdatedUtc = DateTime.UtcNow });
            await seed.SaveChangesAsync();
        }

        int dryRunReported;
        await using (var ctx = db.NewContext())
            dryRunReported = await StudentPointsBackfillCommand.RunAsync(ctx, NullLogger.Instance, dryRun: true);

        int realRunReported;
        await using (var ctx = db.NewContext())
            realRunReported = await StudentPointsBackfillCommand.RunAsync(ctx, NullLogger.Instance, dryRun: false);

        dryRunReported.ShouldBe(realRunReported);

        await using var check = db.NewContext();
        (await check.OutboxMessages.CountAsync()).ShouldBe(1);
    }
}
