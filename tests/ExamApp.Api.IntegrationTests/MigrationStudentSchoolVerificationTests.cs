using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #361: <c>AddStudentSchoolVerification</c> veri adımı gerçek Postgres'te — migration anında okulu olan mevcut
/// öğrenciler (silinmiş satırlar dahil) doğrulanmış sayılır (<c>SchoolVerifiedAt</c> dolu, doğrulayan NULL = sistem);
/// okulsuz öğrenci okulsuz kalır; CHECK kısıtı doğrulanmış okulsuz satırı reddeder. Down kolonları düşürür. Ayrı geçici veritabanı (#259 migration testleriyle aynı desen).
/// </summary>
[Collection(IntegrationCollection.Name)]
public class MigrationStudentSchoolVerificationTests : IntegrationTestBase
{
    private const string PreviousMigration = "20261006222344_AddTestInstanceMaxDurationSnapshot";
    private const string TargetMigration = "20261006225554_AddStudentSchoolVerification";

    public MigrationStudentSchoolVerificationTests(IntegrationApiFactory factory) : base(factory)
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
    public async Task Existing_students_with_a_school_become_verified_schoolless_students_stay_unverified()
    {
        var tempConnStr = CreateTemporaryDatabase();
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(tempConnStr).Options;
            using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            }

            // Ham SQL: önceki şemada SchoolVerifiedAt yok — EF entity'si INSERT'te bu kolonu da yazmaya çalışırdı.
            using (var ctx = new AppDbContext(options))
            {
                await ctx.Database.ExecuteSqlRawAsync("""
                    INSERT INTO "Schools" ("Id", "Name", "CreateTime", "IsDeleted") VALUES (100, 'School A', now(), FALSE);
                    INSERT INTO "Students" ("Id", "UserId", "StudentNumber", "SchoolId", "IsDeleted", "CreateTime") VALUES
                        (1, 20, 'a', 100, FALSE, now()),   -- okullu → doğrulanmış
                        (2, 21, 'b', NULL, FALSE, now()),  -- okulsuz → dokunulmaz
                        (3, 22, 'c', 100, TRUE, now());    -- silinmiş ama okullu → doğrulanmış
                    """);
            }

            var before = DateTime.UtcNow.AddMinutes(-1);
            using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(TargetMigration);
            }

            using (var ctx = new AppDbContext(options))
            {
                var rows = await ctx.Students.IgnoreQueryFilters().AsNoTracking()
                    .Select(s => new { s.Id, s.SchoolId, s.SchoolVerifiedAt, s.SchoolVerifiedByUserId })
                    .ToDictionaryAsync(s => s.Id);

                rows[1].SchoolVerifiedAt.ShouldNotBeNull();
                rows[1].SchoolVerifiedAt!.Value.ShouldBeGreaterThan(before);
                rows[1].SchoolVerifiedByUserId.ShouldBeNull();
                rows[1].SchoolId.ShouldBe(100);
                rows[2].SchoolVerifiedAt.ShouldBeNull();
                rows[2].SchoolId.ShouldBeNull();
                rows[3].SchoolVerifiedAt.ShouldNotBeNull();

                // CK_Students_SchoolVerifiedRequiresSchool: doğrulanmış üyelik okulsuz olamaz (security review Low-1).
                var violation = await Should.ThrowAsync<PostgresException>(() => ctx.Database.ExecuteSqlRawAsync(
                    "UPDATE \"Students\" SET \"SchoolId\" = NULL WHERE \"Id\" = 1"));
                violation.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
                // Okul temizlenirken doğrulama da temizlenirse geçerli (ret/kayıt yolları böyle yazar).
                await ctx.Database.ExecuteSqlRawAsync(
                    "UPDATE \"Students\" SET \"SchoolId\" = NULL, \"SchoolVerifiedAt\" = NULL WHERE \"Id\" = 1");
            }

            // Down: kolonlar düşer, veri adımı geri alınır (önceki şemaya dönülebilir).
            using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
                var columns = await ctx.Database.SqlQueryRaw<string>("""
                    SELECT column_name AS "Value" FROM information_schema.columns
                    WHERE table_name = 'Students'
                      AND (column_name LIKE 'SchoolVerified%' OR column_name LIKE 'SchoolRejected%' OR column_name = 'LastRejectedSchoolId')
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
