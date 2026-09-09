using System.Collections.Generic;
using System.Linq;
using ExamApp.Api.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace ExamApp.Api.Tests.Data;

/// <summary>
/// Guards the "program create" wizard seed data (see GitHub issue #136: a step's option
/// pointed to a NextStep id that was never seeded, leaving the wizard stuck). These tests
/// run <see cref="ProgramStepSeed.SeedData"/> against a real EF Core model (built via a
/// throwaway <see cref="DbContext"/> so conventions/data-annotations are applied exactly like
/// production) and verify the resulting HasData rows are internally consistent.
/// </summary>
public class ProgramStepSeedTests
{
    /// <summary>Minimal throwaway context that only exists to host the seed via OnModelCreating.</summary>
    private sealed class SeedOnlyContext : DbContext
    {
        public SeedOnlyContext(DbContextOptions<SeedOnlyContext> options) : base(options) { }

        public DbSet<ProgramStep> ProgramSteps { get; set; }
        public DbSet<ProgramStepOption> ProgramStepOptions { get; set; }
        public DbSet<ProgramStepAction> ProgramStepActions { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<ProgramStep>()
                .HasMany<ProgramStepOption>()
                .WithOne(o => o.ProgramStep)
                .HasForeignKey(o => o.ProgramStepId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ProgramStep>()
                .HasMany<ProgramStepAction>()
                .WithOne(a => a.ProgramStep)
                .HasForeignKey(a => a.ProgramStepId)
                .OnDelete(DeleteBehavior.Cascade);

            ProgramStepSeed.SeedData(modelBuilder);
        }
    }

    private static (IReadOnlyList<IDictionary<string, object?>> Steps, IReadOnlyList<IDictionary<string, object?>> Options)
        LoadSeedRows()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<SeedOnlyContext>()
            .UseSqlite(connection)
            .Options;

        using var ctx = new SeedOnlyContext(options);

        // HasData seed rows only live on the design-time model, not the runtime-optimized one
        // DbContext.Model exposes by default.
        var model = ctx.GetService<IDesignTimeModel>().Model;

        var stepRows = model.FindEntityType(typeof(ProgramStep))!
            .GetSeedData()
            .ToList();

        var optionRows = model.FindEntityType(typeof(ProgramStepOption))!
            .GetSeedData()
            .ToList();

        return (stepRows, optionRows);
    }

    [Fact]
    public void SeedData_ProducesAtLeastOneStepAndOption()
    {
        var (steps, options) = LoadSeedRows();

        Assert.NotEmpty(steps);
        Assert.NotEmpty(options);
    }

    [Fact]
    public void SeedData_EveryOptionNextStep_IsNullOrPointsToExistingStepId()
    {
        var (steps, options) = LoadSeedRows();

        var stepIds = steps.Select(s => (int)s["Id"]!).ToHashSet();

        var danglingOptions = options
            .Where(o => o["NextStep"] is int)
            .Where(o => !stepIds.Contains((int)o["NextStep"]!))
            .Select(o => new { OptionId = (int)o["Id"]!, NextStep = (int)o["NextStep"]! })
            .ToList();

        Assert.True(
            danglingOptions.Count == 0,
            "Found ProgramStepOption row(s) whose NextStep does not reference an existing " +
            "ProgramStep id (dangling reference), which leaves the program-create wizard stuck: " +
            string.Join(", ", danglingOptions.Select(o => $"OptionId={o.OptionId} -> NextStep={o.NextStep}")));
    }

    [Fact]
    public void SeedData_EveryOption_BelongsToASeededStep()
    {
        var (steps, options) = LoadSeedRows();

        var stepIds = steps.Select(s => (int)s["Id"]!).ToHashSet();

        var orphanOptions = options
            .Where(o => !stepIds.Contains((int)o["ProgramStepId"]!))
            .ToList();

        Assert.True(
            orphanOptions.Count == 0,
            "Found ProgramStepOption row(s) whose ProgramStepId does not reference a seeded ProgramStep.");
    }

    [Fact]
    public void SeedData_EveryStep_IsReachableFromTheFirstStep()
    {
        // Guards against unused/unreachable steps re-entering the seed (e.g. the removed dead
        // step id=4 that no option pointed to).
        var (steps, options) = LoadSeedRows();

        var stepIds = steps.Select(s => (int)s["Id"]!).ToHashSet();
        var firstStepId = steps.Min(s => (int)s["Id"]!);

        var optionsByStep = options
            .GroupBy(o => (int)o["ProgramStepId"]!)
            .ToDictionary(g => g.Key, g => g.Select(o => o["NextStep"] as int?).ToList());

        var visited = new HashSet<int> { firstStepId };
        var queue = new Queue<int>();
        queue.Enqueue(firstStepId);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!optionsByStep.TryGetValue(current, out var nextSteps)) continue;

            foreach (var next in nextSteps)
            {
                if (next is int nextId && stepIds.Contains(nextId) && visited.Add(nextId))
                {
                    queue.Enqueue(nextId);
                }
            }
        }

        var unreachable = stepIds.Except(visited).ToList();

        Assert.True(
            unreachable.Count == 0,
            "Found seeded ProgramStep id(s) unreachable from the first step: " +
            string.Join(", ", unreachable));
    }
}
