using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #570 migrasyonu gerçek PostgreSQL'de: <c>SplitInteractionSolution</c> mevcut planlardaki <c>solution</c> değerini
/// <c>InteractionSolution</c> kolonuna taşır ve plandan siler; geçersiz JSON / nesne olmayan / çözümsüz satırlar değişmez,
/// soft-delete'li satırlar da taşınır. Down çözümü plana geri yazar ve kolonu düşürür.
/// </summary>
public class InteractionSolutionMigrationPostgresTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const string PreviousMigration = "20261023013124_AddWorksheetCommentModerationEvents";
    private const string TargetMigration = "20261026063016_SplitInteractionSolution";

    private const string SolutionJson = """{"placements":[{"zone":"z1","label":"l1"}]}""";

    // EF'in ExecuteSqlRaw'ı string.Format gibi ayrıştırdığı için JSON içeren SQL doğrudan Npgsql ile koşulur.
    private static async Task<int> ScalarIntAsync(AppDbContext ctx, string sql)
    {
        var conn = (NpgsqlConnection)ctx.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        return Convert.ToInt32(await cmd.ExecuteScalarAsync());
    }

    private static async Task ExecuteAsync(AppDbContext ctx, string sql)
    {
        var conn = (NpgsqlConnection)ctx.Database.GetDbConnection();
        if (conn.State != System.Data.ConnectionState.Open) await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(sql, conn);
        await cmd.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task Migration_moves_embedded_solution_to_its_own_column_and_keeps_other_rows_intact_then_down_restores_it()
    {
        var builder = new NpgsqlConnectionStringBuilder(Factory.ConnectionString);
        var tempDb = $"exam_test_migration_{Guid.NewGuid().ToString("N")[..8]}";
        builder.Database = "postgres";
        var adminConnStr = builder.ToString();
        await using (var conn = new NpgsqlConnection(adminConnStr))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{tempDb}\"", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        builder.Database = tempDb;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(builder.ToString()).Options;
        try
        {
            await using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
                await ExecuteAsync(ctx, """
                    INSERT INTO "Grades" ("Id", "Name", "CreateTime", "IsDeleted") VALUES (1, '5', now(), FALSE);
                    INSERT INTO "Subjects" ("Id", "Name", "CreateTime", "IsDeleted") VALUES (1, 'Fen', now(), FALSE);
                    INSERT INTO "Topics" ("Id", "Name", "SubjectId", "GradeId", "CreateTime", "IsDeleted") VALUES (1, 'Kuvvet', 1, 1, now(), FALSE);
                    """);
                await ExecuteAsync(ctx, """
                    INSERT INTO "Questions" ("Id", "Text", "Point", "DifficultyLevel", "IsExample", "ShowPassageFirst", "AnswerColCount",
                        "InteractionType", "InteractionPlan", "IsDeleted", "CreateTime", "SubjectId", "TopicId") VALUES
                        (1, 'embedded', 5, 1, FALSE, FALSE, 3, 'dragDropLabeling',
                            '{"labels":["l1","l2"],"solution":{"placements":[{"zone":"z1","label":"l1"}]}}', FALSE, now(), 1, 1),
                        (2, 'null-solution', 5, 1, FALSE, FALSE, 3, 'dragDropLabeling',
                            '{"labels":["l1"],"solution":null}', FALSE, now(), 1, 1),
                        (3, 'broken-json', 5, 1, FALSE, FALSE, 3, 'dragDropLabeling', '{broken', FALSE, now(), 1, 1),
                        (4, 'array-plan', 5, 1, FALSE, FALSE, 3, 'dragDropLabeling', '[1,2]', FALSE, now(), 1, 1),
                        (5, 'no-solution', 5, 1, FALSE, FALSE, 3, 'dragDropLabeling', '{"labels":["l1"]}', FALSE, now(), 1, 1),
                        (6, 'soft-deleted', 5, 1, FALSE, FALSE, 3, 'dragDropLabeling',
                            '{"labels":["l9"],"solution":{"placements":[{"zone":"z9","label":"l9"}]}}', TRUE, now(), 1, 1),
                        (7, 'null-plan', 5, 1, FALSE, FALSE, 3, 'mcq', NULL, FALSE, now(), 1, 1),
                        (8, 'empty-plan', 5, 1, FALSE, FALSE, 3, 'mcq', '', FALSE, now(), 1, 1),
                        (9, 'broken-with-solution', 5, 1, FALSE, FALSE, 3, 'dragDropLabeling',
                            '{"labels":["l1"],"solution":{"placements":[]}', FALSE, now(), 1, 1);
                    """);
            }

            await using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(TargetMigration);
                (await ctx.Database.GetAppliedMigrationsAsync()).ShouldContain(TargetMigration);

                // Satır sayısı değişmez.
                (await ScalarIntAsync(ctx, """SELECT count(*)::int FROM "Questions" """)).ShouldBe(9);

                // Gömülü çözüm kolona taşındı (jsonb eşitliğiyle) ve plandan silindi.
                (await ScalarIntAsync(ctx, $$"""
                    SELECT count(*)::int FROM "Questions"
                    WHERE "Id" = 1 AND "InteractionSolution"::jsonb = '{{SolutionJson}}'::jsonb
                      AND NOT ("InteractionPlan"::jsonb ? 'solution')
                      AND "InteractionPlan"::jsonb = '{"labels":["l1","l2"]}'::jsonb
                    """)).ShouldBe(1);

                // "solution": null → kolon NULL, anahtar plandan silinir.
                (await ScalarIntAsync(ctx, """
                    SELECT count(*)::int FROM "Questions"
                    WHERE "Id" = 2 AND "InteractionSolution" IS NULL AND NOT ("InteractionPlan"::jsonb ? 'solution')
                    """)).ShouldBe(1);

                // Geçersiz JSON, dizi plan, çözümsüz plan, NULL ve boş plan olduğu gibi kalır; kolon NULL.
                (await ScalarIntAsync(ctx, """
                    SELECT count(*)::int FROM "Questions"
                    WHERE "InteractionSolution" IS NULL AND (
                        "Id" = 3 AND "InteractionPlan" = '{broken' OR
                        "Id" = 4 AND "InteractionPlan" = '[1,2]' OR
                        "Id" = 5 AND "InteractionPlan"::jsonb = '{"labels":["l1"]}'::jsonb OR
                        "Id" = 7 AND "InteractionPlan" IS NULL OR
                        "Id" = 8 AND "InteractionPlan" = '')
                    """)).ShouldBe(5);

                // Ayrıştırılamayan ama "solution" geçen plan (kapalı başarısızlık): plan NULL, çözüm NULL; metin kalmaz.
                (await ScalarIntAsync(ctx, """
                    SELECT count(*)::int FROM "Questions"
                    WHERE "Id" = 9 AND "InteractionPlan" IS NULL AND "InteractionSolution" IS NULL
                    """)).ShouldBe(1);

                // Soft-delete'li satır da taşınır.
                (await ScalarIntAsync(ctx, """
                    SELECT count(*)::int FROM "Questions"
                    WHERE "Id" = 6 AND "IsDeleted" AND "InteractionSolution"::jsonb = '{"placements":[{"zone":"z9","label":"l9"}]}'::jsonb
                      AND NOT ("InteractionPlan"::jsonb ? 'solution')
                    """)).ShouldBe(1);

                // Down: kolon düşer, çözüm plana "solution" olarak geri yazılır; çözümü olmayan satır değişmez.
                await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
                (await ctx.Database.GetAppliedMigrationsAsync()).ShouldNotContain(TargetMigration);

                (await ScalarIntAsync(ctx, """
                    SELECT count(*)::int FROM information_schema.columns
                    WHERE table_name = 'Questions' AND column_name = 'InteractionSolution'
                    """)).ShouldBe(0);

                (await ScalarIntAsync(ctx, $$"""
                    SELECT count(*)::int FROM "Questions"
                    WHERE "Id" = 1 AND "InteractionPlan"::jsonb = '{"labels":["l1","l2"],"solution":{{SolutionJson}}}'::jsonb
                    """)).ShouldBe(1);

                (await ScalarIntAsync(ctx, """
                    SELECT count(*)::int FROM "Questions"
                    WHERE "Id" IN (2, 5) AND NOT ("InteractionPlan"::jsonb ? 'solution')
                    """)).ShouldBe(2);

                (await ScalarIntAsync(ctx, """
                    SELECT count(*)::int FROM "Questions"
                    WHERE "Id" = 3 AND "InteractionPlan" = '{broken'
                    """)).ShouldBe(1);
            }
        }
        finally
        {
            NpgsqlConnection.ClearAllPools();
            await using var conn = new NpgsqlConnection(adminConnStr);
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{tempDb}\" WITH (FORCE)", conn);
            await cmd.ExecuteNonQueryAsync();
        }
    }
}
