using BadgeService;
using BadgeService.Data;
using BadgeService.Entities;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace BadgeService.Tests;

/// <summary>One Postgres container shared by the class; each test migrates its own fresh database.</summary>
public sealed class BadgeIconBackfillPostgresFixture : IAsyncLifetime
{
    public PostgreSqlContainer Container { get; } = new PostgreSqlBuilder().WithImage("postgres:16-alpine").Build();

    public ValueTask InitializeAsync() => new(Container.StartAsync());

    public ValueTask DisposeAsync() => Container.DisposeAsync();
}

/// <summary>
/// Issue #149: the one-time <c>BackfillBadgeDefinitionIconsFromSeed</c> data migration, run for real on
/// PostgreSQL (its hand-written <c>UPDATE ... FROM (VALUES ...)</c> SQL). The DB is migrated up to
/// <c>AddBadgeDefinitionIcon</c> (column exists, empty), pre-#149 rows are inserted, then the backfill runs.
/// </summary>
[Trait("Category", "Integration")]
public class BadgeIconBackfillMigrationTests : IClassFixture<BadgeIconBackfillPostgresFixture>, IAsyncLifetime
{
    private const string SchemaMigration = "AddBadgeDefinitionIcon";
    private const string BackfillMigration = "BackfillBadgeDefinitionIconsFromSeed";

    private readonly string _connectionString;

    public BadgeIconBackfillMigrationTests(BadgeIconBackfillPostgresFixture fixture)
    {
        // Unique database per test on the shared container — MigrateAsync creates it.
        _connectionString = new Npgsql.NpgsqlConnectionStringBuilder(fixture.Container.GetConnectionString())
        {
            Database = $"badge_icon_{Guid.NewGuid():N}",
        }.ConnectionString;
    }

    public async ValueTask InitializeAsync()
    {
        await using var db = NewDb();
        await db.Database.MigrateAsync(SchemaMigration);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private BadgeDbContext NewDb() =>
        new(new DbContextOptionsBuilder<BadgeDbContext>().UseNpgsql(_connectionString).Options);

    private static BadgeDefinition Row(string code, string? iconUrl, string? icon = null) => new()
    {
        Id = Guid.NewGuid(),
        Code = code,
        Name = code,
        Description = "d",
        Category = "c",
        RuleType = "AnswerCount",
        RuleConfigJson = "{\"target\":1}",
        IconUrl = iconUrl,
        Icon = icon,
        IsActive = true,
        CreatedAtUtc = DateTime.UtcNow,
    };

    [Fact]
    public async Task Backfill_sets_the_seeder_icon_on_every_untouched_seed_row()
    {
        var desired = BadgeSeeder.GetDesiredIconsForTesting();
        desired.Count.ShouldBe(37);

        await using (var db = NewDb())
        {
            // Pre-#149 state: every seed row with its original IconUrl and no Icon.
            db.BadgeDefinitions.AddRange(desired.Select(d => Row(d.Code, d.IconUrl)));
            await db.SaveChangesAsync();
        }

        await using (var db = NewDb())
        {
            await db.Database.MigrateAsync(BackfillMigration);
        }

        await using var read = NewDb();
        var byCode = await read.BadgeDefinitions.AsNoTracking().ToDictionaryAsync(x => x.Code, x => x.Icon);
        foreach (var d in desired)
        {
            byCode[d.Code].ShouldBe(d.Icon, $"{d.Code}: migration map seeder ile uyumsuz");
        }
    }

    [Fact]
    public async Task Backfill_leaves_admin_edited_rows_and_unknown_codes_alone()
    {
        await using (var db = NewDb())
        {
            db.BadgeDefinitions.AddRange(
                Row("first-answer", "achievements/disabled-dark.0085b3.svg"),          // untouched seed row → flag
                Row("question-hunter-1", "achievements/disabled-dark.21b1cf.svg"),     // admin changed IconUrl
                Row("question-hunter-2", null),                                        // admin cleared IconUrl
                Row("streak-1", "achievements/disabled-dark.148553.svg", icon: "star"), // already has an Icon
                Row("admin-custom", "achievements/disabled-dark.0085b3.svg"));          // not a seed Code
            await db.SaveChangesAsync();
        }

        await using (var db = NewDb())
        {
            await db.Database.MigrateAsync(BackfillMigration);
        }

        await using (var read = NewDb())
        {
            var icons = await read.BadgeDefinitions.AsNoTracking().ToDictionaryAsync(x => x.Code, x => x.Icon);
            icons["first-answer"].ShouldBe("flag");
            icons["question-hunter-1"].ShouldBeNull();
            icons["question-hunter-2"].ShouldBeNull();
            icons["streak-1"].ShouldBe("star");
            icons["admin-custom"].ShouldBeNull();
        }

        // Down() reverts only what Up() wrote.
        await using (var db = NewDb())
        {
            await db.Database.MigrateAsync(SchemaMigration);
        }

        await using var afterDown = NewDb();
        var reverted = await afterDown.BadgeDefinitions.AsNoTracking().ToDictionaryAsync(x => x.Code, x => x.Icon);
        reverted["first-answer"].ShouldBeNull();
        reverted["streak-1"].ShouldBe("star");
    }
}
