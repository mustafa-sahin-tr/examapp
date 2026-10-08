using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #436: <c>AddParentLinkOriginAndPrimary</c> veri adımı gerçek Postgres'te — mevcut tüm bağlantılar <c>LegacyV1</c> olur
/// (Origin CHECK kısıtı veri adımından SONRA eklenir ve Unknown/boşu reddeder);
/// öğrenci başına en eski Active satır (velisi silinmemiş; sıra <c>COALESCE(ActivatedAt, CreatedAt), Id</c>) birincil olur;
/// #419 kuralına göre zaten süresi dolmuş (7 günden eski) Pending satırlar Revoked'a çekilir, daha yeni Pending'ler geçiş
/// dönemi için kalır. Down kolonları düşürür. Ayrı geçici veritabanı (#259/#361 migration testleriyle aynı desen).
/// </summary>
[Collection(IntegrationCollection.Name)]
public class MigrationParentLinkOriginAndPrimaryTests : IntegrationTestBase
{
    private const string PreviousMigration = "20261007080220_AddParentAccessAuditAtIndex";
    private const string TargetMigration = "20261008100645_AddParentLinkOriginAndPrimary";

    public MigrationParentLinkOriginAndPrimaryTests(IntegrationApiFactory factory) : base(factory)
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
    public async Task Existing_links_become_legacy_oldest_active_per_student_becomes_primary_and_stale_pending_is_closed()
    {
        var tempConnStr = CreateTemporaryDatabase();
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(tempConnStr).Options;
            using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            }

            // Ham SQL: önceki şemada Origin/IsPrimary yok — EF entity'si INSERT'te bu kolonları da yazmaya çalışırdı.
            using (var ctx = new AppDbContext(options))
            {
                await ctx.Database.ExecuteSqlRawAsync("""
                    INSERT INTO "Students" ("Id", "UserId", "StudentNumber", "IsDeleted", "CreateTime") VALUES
                        (1, 20, 'a', FALSE, now()),
                        (2, 21, 'b', FALSE, now()),
                        (3, 22, 'c', FALSE, now());
                    INSERT INTO "Parents" ("Id", "UserId", "IsDeleted", "CreateTime") VALUES
                        (10, 30, FALSE, now()),
                        (11, 31, FALSE, now()),
                        (12, 32, TRUE,  now()),   -- silinmiş veli
                        (13, 33, FALSE, now());
                    INSERT INTO "ParentStudentLinks" ("Id", "ParentId", "StudentId", "Status", "CreatedAt", "ActivatedAt") VALUES
                        -- öğrenci 1: iki Active; en eski AKTİFLEŞEN birincil (101: 20 gün önce aktifleşti, 100: 10 gün önce)
                        (100, 10, 1, 'Active',  now() - interval '30 days', now() - interval '10 days'),
                        (101, 11, 1, 'Active',  now() - interval '25 days', now() - interval '20 days'),
                        -- öğrenci 2: en eski Active velisi silinmiş → sonraki birincil; ayrıca geçiş dönemindeki taze Pending
                        (102, 12, 2, 'Active',  now() - interval '40 days', now() - interval '40 days'),
                        (103, 10, 2, 'Active',  now() - interval '5 days',  now() - interval '5 days'),
                        (104, 13, 2, 'Pending', now() - interval '2 days',  NULL),
                        -- öğrenci 3: yalnız V1'de süresi çoktan dolmuş Pending + Revoked geçmiş → birincil yok
                        (105, 11, 3, 'Pending', now() - interval '8 days',  NULL),
                        (106, 13, 3, 'Revoked', now() - interval '9 days',  NULL);
                    """);
            }

            using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(TargetMigration);
            }

            using (var ctx = new AppDbContext(options))
            {
                var rows = await ctx.ParentStudentLinks.AsNoTracking()
                    .Select(l => new { l.Id, l.Status, l.Origin, l.IsPrimary, l.RevokedAt })
                    .ToDictionaryAsync(l => l.Id);

                rows.Values.ShouldAllBe(r => r.Origin == ParentStudentLinkOrigin.LegacyV1);
                rows.Values.Where(r => r.IsPrimary).Select(r => r.Id).OrderBy(id => id).ShouldBe(new[] { 101, 103 });
                rows[104].Status.ShouldBe(ParentStudentLinkStatus.Pending); // geçiş: öğrenci 30 gün içinde onaylayabilir
                rows[105].Status.ShouldBe(ParentStudentLinkStatus.Revoked); // V1'de zaten düşmüştü — diriltilmez
                rows[105].RevokedAt.ShouldNotBeNull();
                rows[106].Status.ShouldBe(ParentStudentLinkStatus.Revoked);
                rows[106].IsPrimary.ShouldBeFalse();

                // Filtreli tekil index: öğrenci başına ikinci Active birincil reddedilir.
                var violation = await Should.ThrowAsync<PostgresException>(() => ctx.Database.ExecuteSqlRawAsync(
                    "UPDATE \"ParentStudentLinks\" SET \"IsPrimary\" = TRUE WHERE \"Id\" = 100"));
                violation.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);

                // Origin CHECK: atanmamış (Unknown) ya da boş değer reddedilir (security MINOR-2).
                foreach (var bad in new[] { "Unknown", "" })
                {
                    var check = await Should.ThrowAsync<PostgresException>(() => ctx.Database.ExecuteSqlRawAsync(
                        "UPDATE \"ParentStudentLinks\" SET \"Origin\" = {0} WHERE \"Id\" = 100", bad));
                    check.SqlState.ShouldBe(PostgresErrorCodes.CheckViolation);
                }
            }

            // Down: kolonlar düşer (önceki şemaya dönülebilir).
            using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
                var columns = await ctx.Database.SqlQueryRaw<string>("""
                    SELECT column_name AS "Value" FROM information_schema.columns
                    WHERE (table_name = 'ParentStudentLinks' AND column_name IN ('Origin', 'IsPrimary'))
                       OR (table_name = 'ParentInviteCodes' AND column_name = 'CreatedByParentId')
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
