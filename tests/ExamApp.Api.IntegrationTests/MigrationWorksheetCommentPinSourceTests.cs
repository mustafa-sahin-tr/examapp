using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Migrations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #326: AddWorksheetCommentPinSourceAndClearCrossSchoolOwnerPins gerçek Postgres'te —
/// (1) sabitli eski satırlara kaynak (Assignment / Owner / CopyOwner) tahmini; (2) AuthorSchoolId'si NULL legacy satırda yazarın
/// güncel okulu sahibinkiyle aynıysa yazar okulu sabitlenir ve sabit korunur; (3) okul dışı / okulsuz / belirsiz okullu sahibe
/// sabitlenmiş satırlarda sabit + kaynak NULL; atama kaynaklı sabitlere dokunulmaz; ikinci çalıştırma hiçbir satırı
/// değiştirmez. Ayrı geçici veritabanı (#259 / #305 deseni).
/// </summary>
[Collection(IntegrationCollection.Name)]
public class MigrationWorksheetCommentPinSourceTests : IntegrationTestBase
{
    private const string PreviousMigration = "20260930141459_BackfillWorksheetCommentTeacherAuthorSchool";
    private const string TargetMigration = "20260930183625_AddWorksheetCommentPinSourceAndClearCrossSchoolOwnerPins";

    public MigrationWorksheetCommentPinSourceTests(IntegrationApiFactory factory) : base(factory)
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

    private sealed record Row(int? Pin, ResponsibleTeacherSource? Source, int? AuthorSchoolId);

    private static async Task<Dictionary<int, Row>> RowsAsync(AppDbContext ctx) =>
        (await ctx.WorksheetComments.IgnoreQueryFilters().AsNoTracking()
            .Select(c => new { c.Id, c.ResponsibleTeacherUserId, c.ResponsibleTeacherSource, c.AuthorSchoolId })
            .ToListAsync())
        .ToDictionary(c => c.Id, c => new Row(c.ResponsibleTeacherUserId, c.ResponsibleTeacherSource, c.AuthorSchoolId));

    [Fact]
    public async Task Backfills_the_pin_source_and_clears_only_owner_pins_without_a_same_school_link()
    {
        var tempConnStr = CreateTemporaryDatabase();
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(tempConnStr).Options;

            using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            }

            using (var ctx = new AppDbContext(options))
            {
                // Belirsiz okul senaryosu için (index'siz ortam): U16'nın iki canlı öğretmen satırı.
                await ctx.Database.ExecuteSqlRawAsync("""
                    DROP INDEX "IX_Teachers_UserId";
                    INSERT INTO "Grades" ("Id", "Name") VALUES (1, '8');
                    INSERT INTO "Schools" ("Id", "Name", "CreateTime", "IsDeleted") VALUES
                        (100, 'School A', now(), FALSE),
                        (101, 'School B', now(), FALSE);
                    INSERT INTO "Teachers" ("UserId", "SchoolId", "IsIndependentTutor", "IsDeleted", "CreateTime") VALUES
                        (10, 100, FALSE, FALSE, now()),   -- sahip, A
                        (11, NULL, TRUE, FALSE, now()),   -- bağımsız sahip
                        (12, 101, FALSE, FALSE, now()),   -- sahip, B
                        (13, 100, FALSE, FALSE, now()),   -- atayan (sahip değil), A
                        (15, 100, FALSE, TRUE, now()),    -- sahip: silinmiş A satırı ...
                        (15, 101, FALSE, FALSE, now()),   -- ... + canlı B satırı
                        (16, 100, FALSE, FALSE, now()),   -- sahip: İKİ canlı satır (A ...
                        (16, 101, FALSE, FALSE, now());   -- ... ve B) → belirsiz → okulsuz
                    INSERT INTO "Students" ("Id", "UserId", "StudentNumber", "SchoolId", "GradeId", "IsDeleted", "CreateTime") VALUES
                        (1, 20, 'a', 100, 1, FALSE, now()),
                        (2, 21, 'b', 101, 1, FALSE, now()),
                        (3, 22, 'c', 101, 1, FALSE, now()),
                        (4, 23, 'd', NULL, 1, FALSE, now()),
                        (5, 24, 'e', 101, 1, TRUE, now());  -- yalnız silinmiş satır (atama bağı sayılmaz)
                    INSERT INTO "Worksheets" ("Id", "Name", "Description", "GradeId", "MaxDurationSeconds", "IsPracticeTest", "CreateUserId", "SourceWorksheetId") VALUES
                        (1, 'W1', '', 1, 0, FALSE, 10, NULL),
                        (2, 'W2', '', 1, 0, FALSE, 11, NULL),
                        (3, 'W3', '', 1, 0, FALSE, 12, NULL),
                        (4, 'W4', '', 1, 0, FALSE, 15, NULL),
                        (5, 'W5 kopya', '', 1, 0, FALSE, 10, 1),
                        (6, 'W6', '', 1, 0, FALSE, 16, NULL);
                    INSERT INTO "WorksheetAssignments"
                        ("Id", "WorksheetId", "StudentId", "GradeId", "SchoolId", "StartAt", "EndAt", "CreateTime", "CreateUserId", "IsDeleted", "DeleteTime") VALUES
                        (1, 2, 2, NULL, NULL, now() - interval '9 days', now() - interval '1 day', now(), 11, FALSE, NULL),  -- 1 gün önce bitti
                        (2, 1, NULL, 1, 101, now() - interval '1 day', NULL, now(), 10, FALSE, NULL),                        -- B okulu sınıf ataması, 1 gün önce açıldı
                        (3, 3, 5, NULL, NULL, now() - interval '1 day', NULL, now(), 12, FALSE, NULL),                       -- silinmiş öğrenci satırına
                        (4, 2, 3, NULL, NULL, now() - interval '9 days', NULL, now(), 11, TRUE, now() - interval '2 days'); -- 2 gün önce silindi
                    INSERT INTO "WorksheetComments"
                        ("Id", "WorksheetId", "ParentCommentId", "AuthorUserId", "AuthorKeycloakId", "AuthorRole", "Body", "CreateTime", "IsDeleted",
                         "AuthorSchoolId", "ResponsibleTeacherUserId") VALUES
                        (1, 1, NULL, 20, 'kc-20', 'Student', 'aynı okul', now(), FALSE, 100, 10),
                        (2, 3, NULL, 20, 'kc-20', 'Student', 'okul dışı sahip', now(), FALSE, 100, 12),
                        (3, 2, NULL, 20, 'kc-20', 'Student', 'okulsuz sahip', now(), FALSE, 100, 11),
                        (4, 2, NULL, 21, 'kc-21', 'Student', 'okulsuz sahip; yorum anında aktif (sonra bitmiş) atama', now() - interval '5 days', FALSE, 101, 11),
                        (5, 3, NULL, 20, 'kc-20', 'Student', 'sahip olmayan (atayan) sabit', now(), FALSE, 100, 13),
                        (6, 1, NULL, 22, 'kc-22', 'Student', 'okul dışı sahip ama sınıf ataması', now(), FALSE, 101, 10),
                        (7, 1, NULL, 21, 'kc-21', 'Student', 'sabitsiz', now(), FALSE, 101, NULL),
                        (8, 4, NULL, 20, 'kc-20', 'Student', 'sahibin silinmiş satırı aynı okulda, canlı satırı değil', now(), FALSE, 100, 15),
                        (9, 1, NULL, 23, 'kc-23', 'Student', 'okulsuz öğrenci', now(), FALSE, NULL, 10),
                        (10, 3, NULL, 20, 'kc-20', 'Student', 'silinmiş yorum, okul dışı', now(), TRUE, 100, 12),
                        (11, 3, NULL, 21, 'kc-21', 'Student', 'B sahibi, B öğrencisi', now(), FALSE, 101, 12),
                        (12, 1, NULL, 20, 'kc-20', 'Student', 'legacy NULL okul, yazar sahiple aynı okulda', now(), FALSE, NULL, 10),
                        (13, 3, NULL, 20, 'kc-20', 'Student', 'legacy NULL okul, yazar sahipten farklı okulda', now(), FALSE, NULL, 12),
                        (14, 3, NULL, 12, 'kc-12', 'Teacher', 'sahibin duyurusu', now(), FALSE, 101, NULL),
                        (15, 3, 14, 20, 'kc-20', 'Student', 'duyuruya okul dışı reply (sahibe sabit)', now(), FALSE, 100, 12),
                        (16, 3, 14, 21, 'kc-21', 'Student', 'duyuruya aynı okul reply', now(), FALSE, 101, 12),
                        (17, 5, NULL, 20, 'kc-20', 'Student', 'kopya, kopyalayan aynı okulda', now(), FALSE, 100, 10),
                        (18, 6, NULL, 20, 'kc-20', 'Student', 'sahibin okulu belirsiz', now(), FALSE, 100, 16),
                        (19, 3, NULL, 24, 'kc-24', 'Student', 'yalnız silinmiş öğrenci satırını hedefleyen atama', now(), FALSE, 100, 12),
                        (20, 2, NULL, 21, 'kc-21', 'Student', 'atama yorumdan ÖNCE bitmiş', now(), FALSE, 101, 11),
                        (21, 1, NULL, 22, 'kc-22', 'Student', 'atama yorumdan SONRA açılmış', now() - interval '3 days', FALSE, 101, 10),
                        (22, 2, NULL, 22, 'kc-22', 'Student', 'atama yorumdan ÖNCE silinmiş', now() - interval '1 day', FALSE, 101, 11),
                        (23, 2, NULL, 22, 'kc-22', 'Student', 'yorum anında aktif, sonra silinmiş atama', now() - interval '5 days', FALSE, 101, 11);
                    """);
            }

            using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(TargetMigration);
            }

            Dictionary<int, Row> after;
            using (var ctx = new AppDbContext(options))
            {
                after = await RowsAsync(ctx);
            }

            const ResponsibleTeacherSource owner = ResponsibleTeacherSource.Owner;
            const ResponsibleTeacherSource assignment = ResponsibleTeacherSource.Assignment;
            after[1].ShouldBe(new Row(10, owner, 100), "sahip öğrenciyle aynı okulda");
            after[2].ShouldBe(new Row(null, null, 100), "sahip başka okulda, atama yok");
            after[3].ShouldBe(new Row(null, null, 100), "okulsuz sahip (null == null sayılmaz)");
            after[4].ShouldBe(new Row(11, assignment, 101), "yorum anında aktif (sonradan bitmiş) atama → atama kaynaklı, dokunulmaz");
            after[5].ShouldBe(new Row(13, assignment, 100), "sabit sahip değil → yalnız atamadan gelebilir");
            after[6].ShouldBe(new Row(10, assignment, 101), "sahibin öğrencinin okulunu hedefleyen sınıf ataması");
            after[7].ShouldBe(new Row(null, null, 101), "zaten sabitsiz");
            after[8].ShouldBe(new Row(null, null, 100), "sahibin okulu CANLI satırdan (B); silinmiş A satırı sayılmaz");
            after[9].ShouldBe(new Row(null, null, null), "okulsuz öğrenci");
            after[10].ShouldBe(new Row(null, null, 100), "silinmiş yorum da temizlenir (geri açılırsa sızmasın)");
            after[11].ShouldBe(new Row(12, owner, 101), "aynı okul");
            after[12].ShouldBe(new Row(10, owner, 100), "legacy NULL: yazarın güncel okulu (A) sahibinkiyle aynı → okul sabitlenir, sabit korunur");
            after[13].ShouldBe(new Row(null, null, null), "legacy NULL: yazar A, sahip B → temizlenir, yazar okulu değişmez");
            after[14].ShouldBe(new Row(null, null, 101), "öğretmen kökü: sabit yok, dokunulmaz");
            after[15].ShouldBe(new Row(null, null, 100), "duyuruya okul dışı reply'daki sahip sabiti temizlenir");
            after[16].ShouldBe(new Row(12, owner, 101), "duyuruya aynı okul reply'ı korunur");
            after[17].ShouldBe(new Row(10, ResponsibleTeacherSource.CopyOwner, 100), "kopya → CopyOwner, aynı okul");
            after[18].ShouldBe(new Row(null, null, 100), "sahibin iki canlı satırı → okul belirsiz → okulsuz sayılır");
            after[19].ShouldBe(new Row(null, null, 100), "atama bağı yalnız CANLI öğrenci satırıyla kurulur");
            after[20].ShouldBe(new Row(null, null, 101), "yorumdan önce bitmiş atama → Owner (okulsuz sahip) → temizlenir");
            after[21].ShouldBe(new Row(null, null, 101), "yorumdan sonra açılmış atama → Owner (okul dışı sahip) → temizlenir");
            after[22].ShouldBe(new Row(null, null, 101), "yorumdan önce silinmiş atama → Owner → temizlenir");
            after[23].ShouldBe(new Row(11, assignment, 101), "yorum anında aktif, sonradan silinmiş atama → atama kaynaklı");

            // İdempotent: üç UPDATE de ikinci kez hiçbir satırı değiştirmez.
            using (var ctx = new AppDbContext(options))
            {
                (await ctx.Database.ExecuteSqlRawAsync(AddWorksheetCommentPinSourceAndClearCrossSchoolOwnerPins.BackfillSourceSql)).ShouldBe(0);
                (await ctx.Database.ExecuteSqlRawAsync(AddWorksheetCommentPinSourceAndClearCrossSchoolOwnerPins.BackfillLegacyAuthorSchoolSql)).ShouldBe(0);
                (await ctx.Database.ExecuteSqlRawAsync(AddWorksheetCommentPinSourceAndClearCrossSchoolOwnerPins.ClearSql)).ShouldBe(0);
                (await RowsAsync(ctx)).ShouldBe(after);
            }

            // Down: kaynak kolonu düşer; temizlenen sabitler geri gelmez.
            using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
                (await ctx.Database.SqlQueryRaw<int>(
                    """SELECT count(*)::int AS "Value" FROM information_schema.columns WHERE table_name = 'WorksheetComments' AND column_name = 'ResponsibleTeacherSource'""")
                    .SingleAsync()).ShouldBe(0);
                (await ctx.Database.SqlQueryRaw<int>(
                    """SELECT count(*)::int AS "Value" FROM "WorksheetComments" WHERE "ResponsibleTeacherUserId" IS NOT NULL""")
                    .SingleAsync()).ShouldBe(after.Values.Count(r => r.Pin != null));
            }
        }
        finally
        {
            DropTemporaryDatabase(tempConnStr);
        }
    }
}
