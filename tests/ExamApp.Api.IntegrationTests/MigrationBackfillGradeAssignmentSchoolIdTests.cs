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

    private static async Task<Dictionary<int, (int? SchoolId, DateTime? UpdateTime, int? UpdateUserId)>> AssignmentsAsync(AppDbContext ctx)
        => await ctx.WorksheetAssignments.IgnoreQueryFilters().AsNoTracking()
            .ToDictionaryAsync(a => a.Id, a => (a.SchoolId, a.UpdateTime, a.UpdateUserId));

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

            int schoolA, schoolB;
            using (var ctx = new AppDbContext(options))
            {
                var grade = new Grade { Id = 1, Name = "8" };
                var a = new School { Name = "School A" };
                var b = new School { Name = "School B" };
                ctx.AddRange(grade, a, b);
                await ctx.SaveChangesAsync();
                schoolA = a.Id;
                schoolB = b.Id;

                // Bu şemada (unique index henüz yok) çift canlı Teachers satırı yazılabilir — eski veri böyle.
                ctx.Teachers.AddRange(
                    new Teacher { UserId = 10, SchoolId = schoolA },                              // okullu, tek aktif satır
                    new Teacher { UserId = 11, SchoolId = null, IsIndependentTutor = true },      // bağımsız
                    new Teacher { UserId = 12, SchoolId = schoolA },                              // belirsiz: iki aktif satır
                    new Teacher { UserId = 12, SchoolId = schoolB },
                    new Teacher { UserId = 13, SchoolId = schoolB, IsDeleted = true },            // silinmiş satır sayılmaz
                    new Teacher { UserId = 13, SchoolId = schoolA });
                var student = new Student { UserId = 20, StudentNumber = "s", SchoolId = schoolA, GradeId = 1 };
                ctx.Students.Add(student);
                ctx.Worksheets.Add(new Worksheet { Id = 1, Name = "W", Description = "", GradeId = 1 });
                await ctx.SaveChangesAsync();

                var start = DateTime.UtcNow.AddDays(-1);
                ctx.WorksheetAssignments.AddRange(
                    new WorksheetAssignment { Id = 1, WorksheetId = 1, GradeId = 1, StartAt = start },                    // U10 → A
                    new WorksheetAssignment { Id = 2, WorksheetId = 1, GradeId = 1, StartAt = start },                    // U11 → null
                    new WorksheetAssignment { Id = 3, WorksheetId = 1, GradeId = 1, StartAt = start },                    // U12 → null (belirsiz)
                    new WorksheetAssignment { Id = 4, WorksheetId = 1, GradeId = 1, StartAt = start },                    // U13 → A
                    new WorksheetAssignment { Id = 5, WorksheetId = 1, GradeId = 1, StartAt = start },                    // admin (99) → null
                    new WorksheetAssignment { Id = 6, WorksheetId = 1, StudentId = student.Id, StartAt = start },         // öğrenci hedefli → null
                    new WorksheetAssignment { Id = 7, WorksheetId = 1, GradeId = 1, SchoolId = schoolB, StartAt = start },// zaten okullu → B
                    new WorksheetAssignment { Id = 8, WorksheetId = 1, GradeId = 1, StartAt = start, IsDeleted = true }); // silinmiş, U10 → A
                await ctx.SaveChangesAsync();

                // Audit interceptor CreateUserId'yi o anki kullanıcıyla ezer; senaryo sahiplerini SQL ile sabitle.
                await ctx.Database.ExecuteSqlRawAsync("""
                    UPDATE "WorksheetAssignments" SET "CreateUserId" = CASE "Id"
                        WHEN 1 THEN 10 WHEN 2 THEN 11 WHEN 3 THEN 12 WHEN 4 THEN 13 WHEN 5 THEN 99
                        WHEN 6 THEN 10 WHEN 7 THEN 10 WHEN 8 THEN 10 END
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
                // Silinmiş satırın yanında canlı satır serbest (U13), ikinci canlı satır reddedilir.
                ctx.Teachers.Add(new Teacher { UserId = 10, SchoolId = schoolB });
                var dup = await Should.ThrowAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
                dup.InnerException.ShouldBeOfType<PostgresException>().SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);
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
