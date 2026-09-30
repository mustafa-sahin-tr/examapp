using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #305: AddWorksheetCommentModerationAndSchoolScope gerçek Postgres'te — mevcut öğrenci yorumlarının
/// <c>AuthorSchoolId</c>'si yazarın canlı Students satırından doldurulur; okulsuz / yalnız silinmiş satırı olan / Students'sız
/// yazar ve öğretmen yorumları null kalır; SQL ikinci kez çalışınca hiçbir şey değişmez. Ayrı geçici veritabanı (#259 deseni).
/// </summary>
[Collection(IntegrationCollection.Name)]
public class MigrationBackfillWorksheetCommentAuthorSchoolTests : IntegrationTestBase
{
    private const string PreviousMigration = "20260930003404_AddWorksheetCommentsAndCommentsToggle";
    private const string TargetMigration = "20260930125904_AddWorksheetCommentModerationAndSchoolScope";
    private const string TeacherBackfillMigration = "20260930141459_BackfillWorksheetCommentTeacherAuthorSchool";

    public MigrationBackfillWorksheetCommentAuthorSchoolTests(IntegrationApiFactory factory) : base(factory)
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

    private static async Task<Dictionary<int, int?>> AuthorSchoolsAsync(AppDbContext ctx) =>
        (await ctx.WorksheetComments.IgnoreQueryFilters().AsNoTracking()
            .Select(c => new { c.Id, c.AuthorSchoolId })
            .ToListAsync())
        .ToDictionary(c => c.Id, c => c.AuthorSchoolId);

    [Fact]
    public async Task Backfill_pins_the_live_student_school_and_leaves_everything_else_null()
    {
        var tempConnStr = CreateTemporaryDatabase();
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(tempConnStr).Options;

            using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            }

            // HAM SQL: bu şema güncel EF modelinden eski (AuthorSchoolId/Hidden* henüz yok).
            using (var ctx = new AppDbContext(options))
            {
                await ctx.Database.ExecuteSqlRawAsync("""
                    INSERT INTO "Grades" ("Id", "Name") VALUES (1, '8');
                    INSERT INTO "Schools" ("Id", "Name", "CreateTime", "IsDeleted") VALUES
                        (100, 'School A', now(), FALSE),
                        (101, 'School B', now(), FALSE);
                    INSERT INTO "Students" ("UserId", "StudentNumber", "SchoolId", "GradeId", "IsDeleted", "CreateTime") VALUES
                        (20, 'a', 100, 1, FALSE, now()),   -- okullu
                        (21, 'b', NULL, 1, FALSE, now()),  -- okulsuz
                        (22, 'c', 101, 1, TRUE, now()),    -- silinmiş eski satır ...
                        (22, 'c', 100, 1, FALSE, now()),   -- ... + canlı satır → 100
                        (23, 'd', 101, 1, TRUE, now());    -- yalnız silinmiş satır → null
                    INSERT INTO "Teachers" ("UserId", "SchoolId", "IsIndependentTutor", "IsDeleted", "CreateTime") VALUES
                        (30, 101, FALSE, FALSE, now()),    -- okullu öğretmen
                        (31, NULL, TRUE, FALSE, now()),    -- bağımsız tutor
                        (32, 100, FALSE, TRUE, now());     -- yalnız silinmiş satır
                    INSERT INTO "Worksheets" ("Id", "Name", "Description", "GradeId", "MaxDurationSeconds", "IsPracticeTest")
                        VALUES (1, 'W', '', 1, 0, FALSE);
                    INSERT INTO "WorksheetComments"
                        ("Id", "WorksheetId", "AuthorUserId", "AuthorKeycloakId", "AuthorRole", "Body", "CreateTime", "IsDeleted") VALUES
                        (1, 1, 20, 'kc-20', 'Student', 'a', now(), FALSE),
                        (2, 1, 21, 'kc-21', 'Student', 'b', now(), FALSE),
                        (3, 1, 22, 'kc-22', 'Student', 'c', now(), FALSE),
                        (4, 1, 23, 'kc-23', 'Student', 'd', now(), FALSE),
                        (5, 1, 20, 'kc-20', 'Teacher', 'öğretmen rolü', now(), FALSE),
                        (6, 1, 99, 'kc-99', 'Student', 'Students satırı yok', now(), FALSE),
                        (7, 1, 20, 'kc-20', 'Student', 'silinmiş yorum', now(), TRUE),
                        (8, 1, 30, 'kc-30', 'Teacher', 'okullu öğretmen', now(), FALSE),
                        (9, 1, 31, 'kc-31', 'Teacher', 'bağımsız', now(), FALSE),
                        (10, 1, 32, 'kc-32', 'Teacher', 'silinmiş öğretmen', now(), FALSE);
                    """);
            }

            using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(TargetMigration);
            }

            Dictionary<int, int?> after;
            using (var ctx = new AppDbContext(options))
            {
                after = await AuthorSchoolsAsync(ctx);
            }

            after[1].ShouldBe(100, "okullu öğrenci → okulu");
            after[2].ShouldBeNull("okulsuz öğrenci → null (yalnız yazarı görür)");
            after[3].ShouldBe(100, "silinmiş eski satır değil, canlı satırın okulu");
            after[4].ShouldBeNull("yalnız silinmiş Students satırı → null (güvenli taraf)");
            after[5].ShouldBeNull("ilk migration öğretmen yorumuna dokunmaz; U20'nin Teachers satırı da yok");
            after[6].ShouldBeNull("Students satırı yok → null");
            after[7].ShouldBe(100, "soft-delete edilmiş yorum da doldurulur (geri açılırsa sızmasın)");
            after[8].ShouldBeNull("öğretmen backfill'i ayrı migration'da");

            // İdempotent: aynı UPDATE ikinci kez hiçbir satırı değiştirmez.
            using (var ctx = new AppDbContext(options))
            {
                var script = ctx.GetService<IMigrator>().GenerateScript(PreviousMigration, TargetMigration);
                var sql = script[script.IndexOf("UPDATE \"WorksheetComments\"", StringComparison.Ordinal)..];
                sql = sql[..(sql.IndexOf(';') + 1)];
                (await ctx.Database.ExecuteSqlRawAsync(sql)).ShouldBe(0);
                (await AuthorSchoolsAsync(ctx)).ShouldBe(after);
            }

            // review Y1: öğretmen yorumları ayrı veri migration'ında doldurulur; öğrenci satırları değişmez.
            using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(TeacherBackfillMigration);
                var withTeachers = await AuthorSchoolsAsync(ctx);
                withTeachers[8].ShouldBe(101, "okullu öğretmen → okulu");
                withTeachers[9].ShouldBeNull("bağımsız tutor → null");
                withTeachers[10].ShouldBeNull("yalnız silinmiş Teachers satırı → null");
                withTeachers[5].ShouldBeNull("Teachers satırı olmayan 'öğretmen' yazar → null");
                foreach (var id in new[] { 1, 2, 3, 4, 6, 7 })
                    withTeachers[id].ShouldBe(after[id]);

                var script = ctx.GetService<IMigrator>().GenerateScript(TargetMigration, TeacherBackfillMigration);
                var sql = script[script.IndexOf("UPDATE \"WorksheetComments\"", StringComparison.Ordinal)..];
                sql = sql[..(sql.IndexOf(';') + 1)];
                (await ctx.Database.ExecuteSqlRawAsync(sql)).ShouldBe(0);
            }

            // Down: kolonlar ve şikayet tablosu düşer.
            using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
                (await ctx.Database.SqlQueryRaw<int>(
                    """SELECT count(*)::int AS "Value" FROM information_schema.columns WHERE table_name = 'WorksheetComments' AND column_name IN ('AuthorSchoolId', 'HiddenAt')""")
                    .SingleAsync()).ShouldBe(0);
                (await ctx.Database.SqlQueryRaw<int>(
                    """SELECT count(*)::int AS "Value" FROM information_schema.tables WHERE table_name = 'WorksheetCommentReports'""")
                    .SingleAsync()).ShouldBe(0);
            }
        }
        finally
        {
            DropTemporaryDatabase(tempConnStr);
        }
    }
}
