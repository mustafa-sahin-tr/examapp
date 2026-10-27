using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #609: AddTestInstanceRetakeTracking gerçek Postgres'te — mevcut Completed oturumların <c>FirstCompletedAt</c>'i
/// <c>EndTime</c> ile dolar; Started / Expired / EndTime'sız satırlar null kalır; <c>RetakeCount</c> 0, <c>AttemptStartedAt</c> null.
/// Down kolonları düşürür. Ayrı geçici veritabanı (#259 / #305 deseni).
/// </summary>
[Collection(IntegrationCollection.Name)]
public class MigrationTestInstanceRetakeTrackingTests : IntegrationTestBase
{
    private const string PreviousMigration = "20261027033521_AddLabelingActivityStructure";
    private const string TargetMigration = "20261027124929_AddTestInstanceRetakeTracking";

    public MigrationTestInstanceRetakeTrackingTests(IntegrationApiFactory factory) : base(factory)
    {
    }

    private string CreateTemporaryDatabase()
    {
        var builder = new NpgsqlConnectionStringBuilder(Factory.ConnectionString);
        var tempDbName = $"exam_test_migration_{Guid.NewGuid().ToString("N")[..8]}";
        builder.Database = "postgres";
        using (var conn = new NpgsqlConnection(builder.ToString()))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"CREATE DATABASE \"{tempDbName}\"";
            cmd.ExecuteNonQuery();
        }

        builder.Database = tempDbName;
        return builder.ToString();
    }

    private static void DropTemporaryDatabase(string connStr)
    {
        var builder = new NpgsqlConnectionStringBuilder(connStr);
        var tempDbName = builder.Database;
        builder.Database = "postgres";
        using var conn = new NpgsqlConnection(builder.ToString());
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"DROP DATABASE IF EXISTS \"{tempDbName}\" WITH (FORCE)";
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public async Task Backfills_first_completed_at_only_for_completed_rows()
    {
        var tempConnStr = CreateTemporaryDatabase();
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(tempConnStr).Options;
            using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            }

            // Ham SQL (önceki şemada yeni kolonlar yok). Yalnız oturum satırları önemli: FK'lar bu oturumda devre dışı.
            using (var ctx = new AppDbContext(options))
            {
                await ctx.Database.ExecuteSqlRawAsync("""
                    SET session_replication_role = replica;
                    INSERT INTO "TestInstances" ("Id", "StudentId", "WorksheetId", "StartTime", "EndTime", "Status", "CheckCount", "IsDeleted", "CreateTime") VALUES
                        (1, 1, 1, '2026-01-01T08:00:00Z', '2026-01-01T08:30:00Z', 1, 1, FALSE, now()),  -- Completed → EndTime
                        (2, 2, 1, '2026-01-01T08:00:00Z', NULL,                    0, 0, FALSE, now()),  -- Started → null
                        (3, 3, 1, '2026-01-01T08:00:00Z', '2026-01-01T08:40:00Z', 2, 0, FALSE, now()),  -- Expired → null
                        (4, 4, 1, '2026-01-01T08:00:00Z', NULL,                    1, 0, FALSE, now()); -- Completed, EndTime yok → null
                    SET session_replication_role = DEFAULT;
                    """);
            }

            using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(TargetMigration);
            }

            using (var ctx = new AppDbContext(options))
            {
                var rows = await ctx.TestInstances.IgnoreQueryFilters().AsNoTracking()
                    .Select(t => new { t.Id, t.FirstCompletedAt, t.AttemptStartedAt, t.RetakeCount })
                    .ToDictionaryAsync(t => t.Id);

                rows[1].FirstCompletedAt.ShouldBe(new DateTime(2026, 1, 1, 8, 30, 0, DateTimeKind.Utc));
                rows[2].FirstCompletedAt.ShouldBeNull();
                rows[3].FirstCompletedAt.ShouldBeNull();
                rows[4].FirstCompletedAt.ShouldBeNull();
                rows.Values.ShouldAllBe(r => r.RetakeCount == 0 && r.AttemptStartedAt == null);
            }

            using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
                var columns = await ctx.Database.SqlQueryRaw<string>("""
                    SELECT column_name AS "Value" FROM information_schema.columns
                    WHERE table_name = 'TestInstances'
                      AND column_name IN ('FirstCompletedAt', 'AttemptStartedAt', 'RetakeCount')
                    """).ToListAsync();
                columns.ShouldBeEmpty();
            }
        }
        finally
        {
            DropTemporaryDatabase(tempConnStr);
        }
    }
}
