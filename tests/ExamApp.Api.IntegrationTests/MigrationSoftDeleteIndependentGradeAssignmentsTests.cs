using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #236 (AC1+AC2): Verify migration SoftDeleteIndependentTeacherGradeAssignments
/// soft-deletes legacy independent teacher grade assignments and cancels related reminders.
/// Uses a separate temporary database on PostgreSQL server to avoid test isolation issues.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class MigrationSoftDeleteIndependentGradeAssignmentsTests : IntegrationTestBase
{
    public MigrationSoftDeleteIndependentGradeAssignmentsTests(IntegrationApiFactory factory) : base(factory)
    {
    }

    private string CreateTemporaryDatabase()
    {
        var baseConnStr = Factory.ConnectionString;
        var dbNameSuffix = Guid.NewGuid().ToString("N")[..8];
        var tempDbName = $"exam_test_migration_{dbNameSuffix}";

        var builder = new NpgsqlConnectionStringBuilder(baseConnStr);
        builder.Database = "postgres";
        using (var conn = new NpgsqlConnection(builder.ToString()))
        {
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"CREATE DATABASE \"{tempDbName}\"";
                cmd.ExecuteNonQuery();
            }
        }

        builder.Database = tempDbName;
        return builder.ToString();
    }

    private void DropTemporaryDatabase(string connStr)
    {
        var builder = new NpgsqlConnectionStringBuilder(connStr);
        var tempDbName = builder.Database;
        builder.Database = "postgres";
        using (var conn = new NpgsqlConnection(builder.ToString()))
        {
            conn.Open();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $"DROP DATABASE IF EXISTS \"{tempDbName}\" WITH (FORCE)";
                cmd.ExecuteNonQuery();
            }
        }
    }

    /// <summary>Yalnızca bu migration'ın bildiği kolonlar (tam entity SELECT'i eski şemada olmayan kolonları isterdi).</summary>
    private static async Task<Dictionary<int, (bool IsDeleted, int? DeleteUserId, DateTime? DeleteTime)>> AssignmentsAsync(AppDbContext ctx)
        => (await ctx.WorksheetAssignments.IgnoreQueryFilters().AsNoTracking()
                .Select(a => new { a.Id, a.IsDeleted, a.DeleteUserId, a.DeleteTime })
                .ToListAsync())
            .ToDictionary(a => a.Id, a => (a.IsDeleted, a.DeleteUserId, a.DeleteTime));

    private const string PreviousMigration = "20260923080305_AddTeacherRequestedSchoolId";
    private const string TargetMigration = "20260923103501_SoftDeleteIndependentTeacherGradeAssignments";

    [Fact]
    public async Task Migration_SoftDeletesLeakedAssignments_AndCancelsPendingReminders_Idempotent()
    {
        var tempConnStr = CreateTemporaryDatabase();
        try
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(tempConnStr)
                .Options;

            // Veri düzeltmesinden bir önceki migration'a kadar kur; eski (sızan) veri bu şemaya seed edilir.
            using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            }

            // Seed HAM SQL ile: bu şema (PreviousMigration) güncel EF modelinden eski — sonradan eklenen kolonlar
            // (#287 Teachers.AccountApprovedAt, #277 Teachers.LastRejectedAt / WorksheetAssignments.IsPlatformWide) burada yok;
            // EF entity'si ile INSERT/SELECT bu kolonları da yazmaya/okumaya çalışıp düşerdi.
            using (var ctx = new AppDbContext(options))
            {
                await ctx.Database.ExecuteSqlRawAsync("""
                    INSERT INTO "Grades" ("Id", "Name") VALUES (1, '8');
                    INSERT INTO "Schools" ("Id", "Name", "CreateTime", "IsDeleted") VALUES
                        (100, 'School A', now(), FALSE),
                        (101, 'School B', now(), FALSE);
                    INSERT INTO "Teachers" ("UserId", "SchoolId", "RequestedSchoolId", "IsIndependentTutor", "IsDeleted", "CreateTime") VALUES
                        (10, NULL, NULL, TRUE, FALSE, now()),   -- (a) bağımsız öğretmen — okulsuz
                        (11, NULL, 100, FALSE, FALSE, now()),   -- (b) onay bekleyen — okulsuz + RequestedSchoolId
                        (12, 100, NULL, FALSE, FALSE, now());   -- (d) okul öğretmeni
                    INSERT INTO "Students" ("Id", "UserId", "StudentNumber", "SchoolId", "GradeId") VALUES
                        (1, 20, 'a', 100, 1),
                        (2, 21, 'b', 101, 1);
                    INSERT INTO "Worksheets" ("Id", "Name", "Description", "GradeId", "MaxDurationSeconds", "IsPracticeTest", "CreateUserId") VALUES
                        (1, 'Ind', '', 1, 0, FALSE, 10),
                        (2, 'Pend', '', 1, 0, FALSE, 11),
                        (3, 'Admin', '', 1, 0, FALSE, 99),
                        (4, 'School', '', 1, 0, FALSE, 12);
                    INSERT INTO "WorksheetAssignments"
                        ("Id", "WorksheetId", "StudentId", "GradeId", "SchoolId", "StartAt", "CreateTime", "CreateUserId", "IsDeleted") VALUES
                        (1, 1, NULL, 1, NULL, now() - interval '1 day', now() - interval '2 days', 10, FALSE),  -- (a) sızan sınıf ataması
                        (2, 2, NULL, 1, NULL, now() - interval '1 day', now() - interval '2 days', 11, FALSE),  -- (b) onay bekleyen sızıntı
                        (3, 3, NULL, 1, NULL, now() - interval '1 day', now() - interval '2 days', 99, FALSE),  -- (c) admin — korunur
                        (4, 4, NULL, 1, 100, now() - interval '1 day', now() - interval '2 days', 12, FALSE),   -- (d) okullu — korunur
                        (5, 1, 1, NULL, NULL, now() - interval '1 day', now() - interval '2 days', 10, FALSE);  -- (e) öğrenci — korunur
                    INSERT INTO "WorksheetReminders"
                        ("Id", "WorksheetId", "StudentId", "ScheduledFor", "RemindBeforeMinutes", "Status", "HangfireJobId", "CreateTime", "IsDeleted") VALUES
                        (1, 1, 2, now() + interval '1 day', 30, 0, 'job-001', now() - interval '1 day', FALSE),  -- (f1) öğrenci B
                        (2, 1, 1, now() + interval '1 day', 30, 0, 'job-002', now() - interval '1 day', FALSE);  -- (f2) öğrenci A
                    """);
            }

            // Veri düzeltme migration'ını uygula
            using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(TargetMigration);
            }

            // Verify migration results — yalnızca bu şemada var olan kolonları okuyan projeksiyonlar.
            using (var ctx = new AppDbContext(options))
            {
                var assignments = await AssignmentsAsync(ctx);

                assignments[1].IsDeleted.ShouldBeTrue("(a) should be soft-deleted");
                assignments[1].DeleteUserId.ShouldBe(-236, "(a) DeleteUserId=-236");
                assignments[2].IsDeleted.ShouldBeTrue("(b) should be soft-deleted");
                assignments[2].DeleteUserId.ShouldBe(-236, "(b) DeleteUserId=-236");
                assignments[3].IsDeleted.ShouldBeFalse("(c) admin preserved");
                assignments[4].IsDeleted.ShouldBeFalse("(d) school-scoped preserved");
                assignments[5].IsDeleted.ShouldBeFalse("(e) direct student preserved");

                var reminders = await ctx.WorksheetReminders.IgnoreQueryFilters().AsNoTracking()
                    .Select(r => new { r.Id, r.Status, r.HangfireJobId })
                    .ToDictionaryAsync(r => r.Id);
                reminders[1].Status.ShouldBe(WorksheetReminderStatus.Cancelled, "Reminder (f1) Cancelled");
                reminders[1].HangfireJobId.ShouldBeNull("Reminder (f1) HangfireJobId null");
                reminders[2].Status.ShouldBe(WorksheetReminderStatus.Pending, "Reminder (f2) stays Pending");
                reminders[2].HangfireJobId.ShouldBe("job-002", "Reminder (f2) HangfireJobId preserved");
            }

            // Verify idempotency
            using (var ctx = new AppDbContext(options))
            {
                await ctx.Database.MigrateAsync();

                var a = (await AssignmentsAsync(ctx))[1];
                a.IsDeleted.ShouldBeTrue("Second run: (a) still soft-deleted");
                a.DeleteUserId.ShouldBe(-236);
            }

            // Down: yalnız bu migration'ın sildiği (DeleteUserId=-236) satırlar geri gelir
            using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            }
            using (var ctx = new AppDbContext(options))
            {
                var all = await AssignmentsAsync(ctx);
                new[] { all[1], all[2] }.ShouldAllBe(x => !x.IsDeleted && x.DeleteUserId == null && x.DeleteTime == null);
            }
        }
        finally
        {
            DropTemporaryDatabase(tempConnStr);
        }
    }
}
