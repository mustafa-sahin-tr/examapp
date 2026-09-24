using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #259: gerçek Postgres üzerinde
/// (1) BackfillGradeAssignmentSchoolIdFromCreatorTeacher — okullu tek aktif Teachers satırı olan oluşturanın null okul
///     sınıf atamaları okula bağlanır; bağımsız / çift satırlı / Teachers'sız (admin) oluşturanınkiler null kalır;
///     SQL'in ikinci kez çalışması hiçbir şeyi değiştirmez; Down no-op.
/// (2) AddUniqueActiveUserIdToTeachersAndStudents — canlı çift UserId varsa açık hatayla durur (şema değişmez, veri
///     silinmez); çift giderilince filtreli unique index kurulur, backfill tekrar çalışıp önceden atlanan atamayı
///     düzeltir ve index canlı çift satırı reddeder.
/// Diğer testleri etkilememek için ayrı geçici veritabanı kullanır (#236 migration testiyle aynı desen).
/// </summary>
[Collection(IntegrationCollection.Name)]
public class MigrationBackfillGradeAssignmentSchoolIdTests : IntegrationTestBase
{
    private const string PreviousMigration = "20260923201502_AddAdminUserActionLog";
    private const string BackfillMigration = "20260923231352_BackfillGradeAssignmentSchoolIdFromCreatorTeacher";
    private const string UniqueIndexMigration = "20260923231622_AddUniqueActiveUserIdToTeachersAndStudents";

    public MigrationBackfillGradeAssignmentSchoolIdTests(IntegrationApiFactory factory) : base(factory)
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

    /// <summary>
    /// Yalnızca bu migration'ların bildiği kolonları okuyan projeksiyon — tam entity SELECT'i güncel modelin sonradan
    /// eklenen kolonlarını (ör. IsPlatformWide) da isterdi ve eski şemada düşerdi.
    /// </summary>
    private static async Task<Dictionary<int, (int? SchoolId, DateTime? UpdateTime, int? UpdateUserId)>> AssignmentsAsync(AppDbContext ctx)
        => (await ctx.WorksheetAssignments.IgnoreQueryFilters().AsNoTracking()
                .Select(a => new { a.Id, a.SchoolId, a.UpdateTime, a.UpdateUserId })
                .ToListAsync())
            .ToDictionary(a => a.Id, a => (a.SchoolId, a.UpdateTime, a.UpdateUserId));

    [Fact]
    public async Task Backfill_scopes_unambiguous_school_teacher_assignments_is_idempotent_and_unique_index_guards_duplicates()
    {
        var tempConnStr = CreateTemporaryDatabase();
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(tempConnStr).Options;

            using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            }

            // Seed HAM SQL ile: bu şema (PreviousMigration) güncel EF modelinden eski — sonradan eklenen kolonlar
            // (#287 Teachers.AccountApprovedAt, #277 Teachers.LastRejectedAt / WorksheetAssignments.IsPlatformWide) burada yok;
            // EF entity'si ile INSERT/SELECT bu kolonları da yazmaya/okumaya çalışıp düşerdi.
            const int schoolA = 100, schoolB = 101;
            using (var ctx = new AppDbContext(options))
            {
                await ctx.Database.ExecuteSqlRawAsync("""
                    INSERT INTO "Grades" ("Id", "Name") VALUES (1, '8');
                    INSERT INTO "Schools" ("Id", "Name", "CreateTime", "IsDeleted") VALUES
                        (100, 'School A', now(), FALSE),
                        (101, 'School B', now(), FALSE);
                    -- Bu şemada (unique index henüz yok) çift canlı Teachers satırı yazılabilir — eski veri böyle.
                    -- Id identity'den gelir (sonradaki çift-satır INSERT'i PK yerine UserId index'ine takılsın).
                    INSERT INTO "Teachers" ("UserId", "SchoolId", "IsIndependentTutor", "IsDeleted", "CreateTime") VALUES
                        (10, 100, FALSE, FALSE, now()),   -- okullu, tek aktif satır
                        (11, NULL, TRUE, FALSE, now()),   -- bağımsız
                        (12, 100, FALSE, FALSE, now()),   -- belirsiz: iki aktif satır
                        (12, 101, FALSE, FALSE, now()),
                        (13, 101, FALSE, TRUE, now()),    -- silinmiş satır sayılmaz
                        (13, 100, FALSE, FALSE, now());
                    INSERT INTO "Students" ("Id", "UserId", "StudentNumber", "SchoolId", "GradeId") VALUES (1, 20, 's', 100, 1);
                    INSERT INTO "Worksheets" ("Id", "Name", "Description", "GradeId", "MaxDurationSeconds", "IsPracticeTest")
                        VALUES (1, 'W', '', 1, 0, FALSE);
                    INSERT INTO "WorksheetAssignments"
                        ("Id", "WorksheetId", "StudentId", "GradeId", "SchoolId", "StartAt", "CreateTime", "CreateUserId", "IsDeleted") VALUES
                        (1, 1, NULL, 1, NULL, now() - interval '1 day', now(), 10, FALSE),  -- U10 → A
                        (2, 1, NULL, 1, NULL, now() - interval '1 day', now(), 11, FALSE),  -- U11 → null
                        (3, 1, NULL, 1, NULL, now() - interval '1 day', now(), 12, FALSE),  -- U12 → null (belirsiz)
                        (4, 1, NULL, 1, NULL, now() - interval '1 day', now(), 13, FALSE),  -- U13 → A
                        (5, 1, NULL, 1, NULL, now() - interval '1 day', now(), 99, FALSE),  -- admin (99) → null
                        (6, 1, 1, NULL, NULL, now() - interval '1 day', now(), 10, FALSE),  -- öğrenci hedefli → null
                        (7, 1, NULL, 1, 101, now() - interval '1 day', now(), 10, FALSE),   -- zaten okullu → B
                        (8, 1, NULL, 1, NULL, now() - interval '1 day', now(), 10, TRUE);   -- silinmiş, U10 → A
                    """);
            }

            using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(BackfillMigration);
            }

            Dictionary<int, (int? SchoolId, DateTime? UpdateTime, int? UpdateUserId)> afterFirstRun;
            using (var ctx = new AppDbContext(options))
            {
                afterFirstRun = await AssignmentsAsync(ctx);
            }

            afterFirstRun[1].SchoolId.ShouldBe(schoolA, "okullu tek aktif Teachers satırı → okul yazılır");
            afterFirstRun[1].UpdateUserId.ShouldBe(-259);
            afterFirstRun[2].SchoolId.ShouldBeNull("bağımsız öğretmen → dokunulmaz");
            afterFirstRun[3].SchoolId.ShouldBeNull("iki aktif Teachers satırı → belirsiz, dokunulmaz");
            afterFirstRun[4].SchoolId.ShouldBe(schoolA, "silinmiş Teachers satırı çift sayılmaz");
            afterFirstRun[5].SchoolId.ShouldBeNull("Teachers satırı yok (admin) → dokunulmaz");
            afterFirstRun[6].SchoolId.ShouldBeNull("öğrenci hedefli atama kapsam dışı");
            afterFirstRun[7].SchoolId.ShouldBe(schoolB, "zaten okullu atama değişmez");
            afterFirstRun[7].UpdateUserId.ShouldNotBe(-259);
            afterFirstRun[8].SchoolId.ShouldBe(schoolA, "silinmiş atama da düzeltilir (yeniden açılırsa sızmasın)");

            // İdempotent: aynı migration SQL'i ikinci kez çalıştırılınca hiçbir satır değişmez (UpdateTime dahil).
            using (var ctx = new AppDbContext(options))
            {
                var script = ctx.GetService<IMigrator>().GenerateScript(PreviousMigration, BackfillMigration);
                var backfillSql = script[(script.IndexOf("UPDATE \"WorksheetAssignments\"", StringComparison.Ordinal))..];
                backfillSql = backfillSql[..(backfillSql.IndexOf(';') + 1)];
                (await ctx.Database.ExecuteSqlRawAsync(backfillSql)).ShouldBe(0);
                (await AssignmentsAsync(ctx)).ShouldBe(afterFirstRun);
            }

            // Unique index migration'ı: canlı çift (U12) varken açık hatayla durur, index oluşmaz, veri silinmez.
            using (var ctx = new AppDbContext(options))
            {
                var ex = await Should.ThrowAsync<PostgresException>(() => ctx.GetService<IMigrator>().MigrateAsync(UniqueIndexMigration));
                ex.MessageText.ShouldContain("issue #259");
                ex.MessageText.ShouldContain("UserId: 12");
            }

            using (var ctx = new AppDbContext(options))
            {
                (await ctx.Database.SqlQueryRaw<int>(
                    """SELECT count(*)::int AS "Value" FROM pg_indexes WHERE indexname IN ('IX_Teachers_UserId', 'IX_Students_UserId')""")
                    .SingleAsync()).ShouldBe(0);
                (await ctx.Teachers.CountAsync(t => t.UserId == 12)).ShouldBe(2, "veri silinmez");
                (await ctx.Database.GetAppliedMigrationsAsync()).ShouldNotContain(UniqueIndexMigration);

                // Operatör çifti elle giderir (soft-delete) → migration tekrar çalıştırılınca geçer.
                await ctx.Database.ExecuteSqlRawAsync(
                    """UPDATE "Teachers" SET "IsDeleted" = TRUE WHERE "UserId" = 12 AND "SchoolId" = {0}""", schoolB);
            }

            using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(UniqueIndexMigration);
            }

            // Index kurulduktan sonra backfill tekrar çalıştı: çift yüzünden atlanan U12 ataması artık tek canlı satırın okulunu
            // aldı; diğer satırlar ilk çalıştırmadaki haliyle kaldı (idempotent).
            using (var ctx = new AppDbContext(options))
            {
                var afterIndex = await AssignmentsAsync(ctx);
                afterIndex[3].SchoolId.ShouldBe(schoolA, "çift giderildikten sonra önceden atlanan atama okullu");
                afterIndex[3].UpdateUserId.ShouldBe(-259);
                afterIndex[2].SchoolId.ShouldBeNull();
                afterIndex[5].SchoolId.ShouldBeNull();
                foreach (var id in new[] { 1, 4, 6, 7, 8 })
                    afterIndex[id].ShouldBe(afterFirstRun[id]);
            }

            using (var ctx = new AppDbContext(options))
            {
                // Silinmiş satırın yanında canlı satır serbest (U13), ikinci canlı satır reddedilir. (Ham SQL: eski şema.)
                var dup = await Should.ThrowAsync<PostgresException>(() => ctx.Database.ExecuteSqlRawAsync(
                    """INSERT INTO "Teachers" ("UserId", "SchoolId", "IsDeleted", "CreateTime") VALUES (10, {0}, FALSE, now())""",
                    schoolB));
                dup.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
            }

            // Down: index düşer, backfill geri ALINMAZ (bilinçli no-op).
            using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            }

            using (var ctx = new AppDbContext(options))
            {
                var afterDown = await AssignmentsAsync(ctx);
                afterDown[1].SchoolId.ShouldBe(schoolA);
                afterDown[3].SchoolId.ShouldBe(schoolA);
                afterDown[4].SchoolId.ShouldBe(schoolA);
            }
        }
        finally
        {
            DropTemporaryDatabase(tempConnStr);
        }
    }
}
