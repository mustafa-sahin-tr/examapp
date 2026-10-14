using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// Issue #439 migrasyonu gerçek PostgreSQL'de: <c>AddBookingParentApproval</c> mevcut Pending/Approved/Rejected talepleri
/// değiştirmez (yalnız nullable kolon ekler ve benzersiz slot indeksine AwaitingParent'i ekler); Down indeksi eski haline döndürür.
/// </summary>
public class BookingParentApprovalMigrationPostgresTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const string PreviousMigration = "20261008110946_AddParentConsentsAndStudentParentManaged";
    private const string TargetMigration = "20261014080424_AddBookingParentApproval";

    [Fact]
    public async Task Migration_keeps_existing_bookings_untouched_and_keeps_the_one_open_request_per_slot_rule()
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
                await ctx.Database.ExecuteSqlRawAsync("""
                    INSERT INTO "Teachers" ("Id", "UserId", "IsIndependentTutor", "IsDeleted", "CreateTime") VALUES (1, 1, TRUE, FALSE, now());
                    INSERT INTO "Students" ("Id", "UserId", "StudentNumber", "SchoolName", "ParentManaged", "IsDeleted", "CreateTime") VALUES
                        (1, 2, 'S1', '', FALSE, FALSE, now());
                    INSERT INTO "TeacherAvailabilitySlots" ("Id", "TeacherId", "Date", "StartTime", "EndTime", "CreatedAt", "CreateTime", "IsDeleted") VALUES
                        (1, 1, DATE '2026-11-02', TIME '10:00', TIME '11:00', now(), now(), FALSE),
                        (2, 1, DATE '2026-11-02', TIME '12:00', TIME '13:00', now(), now(), FALSE),
                        (3, 1, DATE '2026-11-02', TIME '14:00', TIME '15:00', now(), now(), FALSE);
                    INSERT INTO "Bookings" ("Id", "TeacherId", "StudentId", "AvailabilitySlotId", "Status", "CreatedAt", "DecisionAt", "RejectionReason", "CreateTime", "IsDeleted") VALUES
                        (1, 1, 1, 1, 0, now(), NULL, NULL, now(), FALSE),
                        (2, 1, 1, 2, 1, now(), now(), NULL, now(), FALSE),
                        (3, 1, 1, 3, 2, now(), now(), 'Sınav', now(), FALSE);
                    """);
            }

            await using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(TargetMigration);
                (await ctx.Database.GetAppliedMigrationsAsync()).ShouldContain(TargetMigration);

                // Mevcut satırlar: aynı sayı, aynı durum (0/1/2), yeni kolonlar boş.
                (await ctx.Database.SqlQueryRaw<int>("""SELECT count(*)::int AS "Value" FROM "Bookings" """).SingleAsync()).ShouldBe(3);
                (await ctx.Database.SqlQueryRaw<int>("""
                    SELECT count(*)::int AS "Value" FROM "Bookings"
                    WHERE "Id" = 1 AND "Status" = 0 AND "ParentApprovedAt" IS NULL AND "ParentApprovedBy" IS NULL AND "ParentRejectedBy" IS NULL
                       OR "Id" = 2 AND "Status" = 1 AND "ParentApprovedAt" IS NULL AND "ParentApprovedBy" IS NULL AND "ParentRejectedBy" IS NULL
                       OR "Id" = 3 AND "Status" = 2 AND "ParentApprovedAt" IS NULL AND "ParentRejectedBy" IS NULL AND "RejectionReason" = 'Sınav'
                    """).SingleAsync()).ShouldBe(3);

                // Benzersiz kural: mevcut Pending slotu tutmaya devam eder; AwaitingParent de aynı slotu tutar.
                var duplicate = await Should.ThrowAsync<PostgresException>(() => ctx.Database.ExecuteSqlRawAsync(
                    """INSERT INTO "Bookings" ("Id", "TeacherId", "StudentId", "AvailabilitySlotId", "Status", "CreatedAt", "CreateTime", "IsDeleted") VALUES (101, 1, 1, 1, 3, now(), now(), FALSE)"""));
                duplicate.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);

                // Yeni durumlar (Expired/ParentRejected) slotu tutmaz.
                await ctx.Database.ExecuteSqlRawAsync("""
                    INSERT INTO "Bookings" ("Id", "TeacherId", "StudentId", "AvailabilitySlotId", "Status", "CreatedAt", "CreateTime", "IsDeleted") VALUES
                        (102, 1, 1, 2, 4, now(), now(), FALSE),
                        (103, 1, 1, 2, 5, now(), now(), FALSE),
                        (104, 1, 1, 3, 3, now(), now(), FALSE);
                    """);

                // Down (issue #439 review): yeni durumlar (3/4/5) eski kodun tanıdığı Rejected(2)'ye çekilir, eski indeks (Status IN (0,1))
                // geri gelir; önceden var olan satırlar değişmez.
                await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
                (await ctx.Database.GetAppliedMigrationsAsync()).ShouldNotContain(TargetMigration);
                (await ctx.Database.SqlQueryRaw<int>("""SELECT count(*)::int AS "Value" FROM "Bookings" WHERE "Status" IN (3, 4, 5)""").SingleAsync())
                    .ShouldBe(0);
                (await ctx.Database.SqlQueryRaw<int>("""SELECT count(*)::int AS "Value" FROM "Bookings" WHERE "Id" IN (102, 103, 104) AND "Status" = 2""").SingleAsync())
                    .ShouldBe(3);
                (await ctx.Database.SqlQueryRaw<int>("""
                    SELECT count(*)::int AS "Value" FROM "Bookings"
                    WHERE "Id" = 1 AND "Status" = 0 OR "Id" = 2 AND "Status" = 1 OR "Id" = 3 AND "Status" = 2
                    """).SingleAsync()).ShouldBe(3);
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
