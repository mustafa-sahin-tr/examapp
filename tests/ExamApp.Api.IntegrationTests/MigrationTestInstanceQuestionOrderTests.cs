using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #685: AddTestInstanceQuestionOrder gerçek Postgres'te — mevcut oturum satırlarının <c>Order</c>'ı oturum başına
/// eski görüntü sırasıyla (<c>WorksheetQuestion.Order, Id</c>; silinmiş satırlar dahil) 1'den doldurulur. Down kolonu düşürür.
/// Ayrı geçici veritabanı (#259 / #305 deseni).
/// </summary>
[Collection(IntegrationCollection.Name)]
public class MigrationTestInstanceQuestionOrderTests : IntegrationTestBase
{
    private const string PreviousMigration = "20261029190156_AddAdminUserActionAffectedCount";
    private const string TargetMigration = "20261101093025_AddTestInstanceQuestionOrder";

    public MigrationTestInstanceQuestionOrderTests(IntegrationApiFactory factory) : base(factory)
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
    public async Task Backfills_each_sessions_order_from_the_display_order_at_migration_time()
    {
        var tempConnStr = CreateTemporaryDatabase();
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(tempConnStr).Options;
            using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            }

            // Sayfa maddeleri: 10 (Order 5), 11 (Order 1), 12 (Order 1, eşitlikte Id), 13 (Order 3, kaldırılmış #684).
            // Oturum 1 hepsini, oturum 2 yalnız 10 ve 12'yi taşır; satır Id'leri bilerek sıradan farklı. FK'lar devre dışı.
            using (var ctx = new AppDbContext(options))
            {
                await ctx.Database.ExecuteSqlRawAsync("""
                    SET session_replication_role = replica;
                    INSERT INTO "TestQuestions" ("Id", "TestId", "QuestionId", "Order", "IsDeleted", "CreateTime") VALUES
                        (10, 1, 100, 5, FALSE, now()),
                        (11, 1, 101, 1, FALSE, now()),
                        (12, 1, 102, 1, FALSE, now()),
                        (13, 1, 103, 3, TRUE,  now());
                    INSERT INTO "TestInstanceQuestions" ("Id", "WorksheetInstanceId", "WorksheetQuestionId", "IsCorrect", "TimeTaken",
                        "ShowCorrectAnswer", "IsDeleted", "CreateTime", "AnswerRevision", "CorrectCount", "TotalCount", "GradedBy") VALUES
                        (1, 1, 10, FALSE, 0, FALSE, FALSE, now(), 0, 0, 0, 0),
                        (2, 1, 13, FALSE, 0, FALSE, FALSE, now(), 0, 0, 0, 0),
                        (3, 1, 12, FALSE, 0, FALSE, FALSE, now(), 0, 0, 0, 0),
                        (4, 1, 11, FALSE, 0, FALSE, FALSE, now(), 0, 0, 0, 0),
                        (5, 2, 12, FALSE, 0, FALSE, FALSE, now(), 0, 0, 0, 0),
                        (6, 2, 10, FALSE, 0, FALSE, FALSE, now(), 0, 0, 0, 0);
                    SET session_replication_role = DEFAULT;
                    """);
            }

            using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(TargetMigration);
            }

            using (var ctx = new AppDbContext(options))
            {
                var orders = await ctx.TestInstanceQuestions.IgnoreQueryFilters().AsNoTracking()
                    .ToDictionaryAsync(r => r.Id, r => r.Order);

                // Oturum 1: 11 (1, Id 11) → 12 (1, Id 12) → 13 (3) → 10 (5).
                orders[4].ShouldBe(1);
                orders[3].ShouldBe(2);
                orders[2].ShouldBe(3);
                orders[1].ShouldBe(4);
                // Oturum 2: 12 → 10.
                orders[5].ShouldBe(1);
                orders[6].ShouldBe(2);
            }

            using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
                var columns = await ctx.Database.SqlQueryRaw<string>("""
                    SELECT column_name AS "Value" FROM information_schema.columns
                    WHERE table_name = 'TestInstanceQuestions' AND column_name = 'Order'
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
