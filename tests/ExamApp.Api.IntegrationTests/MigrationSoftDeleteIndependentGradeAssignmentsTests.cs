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

            // Seed data with various assignment scenarios
            using (var ctx = new AppDbContext(options))
            {
                var grade = new Grade { Id = 1, Name = "8" };
                var schoolA = new School { Id = 100, Name = "School A" };
                var schoolB = new School { Id = 101, Name = "School B" };
                ctx.AddRange(grade, schoolA, schoolB);

                // (a) Independent teacher - okulsuz
                var indTeacher = new Teacher { UserId = 10, SchoolId = null, IsIndependentTutor = true };
                // (b) Pending approval teacher - okulsuz + RequestedSchoolId
                var pendTeacher = new Teacher { UserId = 11, SchoolId = null, RequestedSchoolId = schoolA.Id };
                // (d) School teacher
                var schoolTeacher = new Teacher { UserId = 12, SchoolId = schoolA.Id };

                ctx.AddRange(indTeacher, pendTeacher, schoolTeacher);

                var studentA = new Student { UserId = 20, StudentNumber = "a", SchoolId = schoolA.Id, GradeId = grade.Id };
                var studentB = new Student { UserId = 21, StudentNumber = "b", SchoolId = schoolB.Id, GradeId = grade.Id };
                ctx.AddRange(studentA, studentB);

                await ctx.SaveChangesAsync();

                var now = DateTime.UtcNow;
                var activeStart = now.AddDays(-1);

                // Create worksheets
                ctx.SetCurrentUser(10);
                var indWs = new Worksheet { Id = 1, Name = "Ind", Description = "", GradeId = grade.Id };
                ctx.Worksheets.Add(indWs);
                await ctx.SaveChangesAsync();

                ctx.SetCurrentUser(11);
                var pendWs = new Worksheet { Id = 2, Name = "Pend", Description = "", GradeId = grade.Id };
                ctx.Worksheets.Add(pendWs);
                await ctx.SaveChangesAsync();

                ctx.SetCurrentUser(99);
                var adminWs = new Worksheet { Id = 3, Name = "Admin", Description = "", GradeId = grade.Id };
                ctx.Worksheets.Add(adminWs);
                await ctx.SaveChangesAsync();

                ctx.SetCurrentUser(12);
                var schoolWs = new Worksheet { Id = 4, Name = "School", Description = "", GradeId = grade.Id };
                ctx.Worksheets.Add(schoolWs);
                await ctx.SaveChangesAsync();

                // (a) Leaked grade assignment
                ctx.WorksheetAssignments.Add(new WorksheetAssignment
                {
                    Id = 1,
                    WorksheetId = 1,
                    GradeId = grade.Id,
                    SchoolId = null,
                    CreateUserId = 10,
                    StartAt = activeStart,
                    CreateTime = now.AddDays(-2)
                });

                // (b) Pending approval teacher leaked assignment
                ctx.WorksheetAssignments.Add(new WorksheetAssignment
                {
                    Id = 2,
                    WorksheetId = 2,
                    GradeId = grade.Id,
                    SchoolId = null,
                    CreateUserId = 11,
                    StartAt = activeStart,
                    CreateTime = now.AddDays(-2)
                });

                // (c) Admin grade assignment - preserved
                ctx.WorksheetAssignments.Add(new WorksheetAssignment
                {
                    Id = 3,
                    WorksheetId = 3,
                    GradeId = grade.Id,
                    SchoolId = null,
                    CreateUserId = 99,
                    StartAt = activeStart,
                    CreateTime = now.AddDays(-2)
                });

                // (d) School-scoped assignment - preserved
                ctx.WorksheetAssignments.Add(new WorksheetAssignment
                {
                    Id = 4,
                    WorksheetId = 4,
                    GradeId = grade.Id,
                    SchoolId = schoolA.Id,
                    CreateUserId = 12,
                    StartAt = activeStart,
                    CreateTime = now.AddDays(-2)
                });

                // (e) Direct student assignment - preserved
                ctx.WorksheetAssignments.Add(new WorksheetAssignment
                {
                    Id = 5,
                    WorksheetId = 1,
                    StudentId = studentA.Id,
                    SchoolId = null,
                    CreateUserId = 10,
                    StartAt = activeStart,
                    CreateTime = now.AddDays(-2)
                });

                await ctx.SaveChangesAsync();

                // (f) Reminders
                ctx.WorksheetReminders.Add(new WorksheetReminder
                {
                    Id = 1,
                    WorksheetId = 1,
                    StudentId = studentB.Id,
                    ScheduledFor = now.AddDays(1),
                    Status = WorksheetReminderStatus.Pending,
                    HangfireJobId = "job-001",
                    CreateTime = now.AddDays(-1)
                });

                ctx.WorksheetReminders.Add(new WorksheetReminder
                {
                    Id = 2,
                    WorksheetId = 1,
                    StudentId = studentA.Id,
                    ScheduledFor = now.AddDays(1),
                    Status = WorksheetReminderStatus.Pending,
                    HangfireJobId = "job-002",
                    CreateTime = now.AddDays(-1)
                });

                await ctx.SaveChangesAsync();

                // Audit interceptor CreateUserId'yi o anki kullanıcıyla ezer; senaryo sahiplerini SQL ile sabitle.
                await ctx.Database.ExecuteSqlRawAsync("""
                    UPDATE "WorksheetAssignments" SET "CreateUserId" = CASE "Id"
                        WHEN 1 THEN 10 WHEN 2 THEN 11 WHEN 3 THEN 99 WHEN 4 THEN 12 ELSE "CreateUserId" END
                    WHERE "Id" IN (1, 2, 3, 4)
                    """);
            }

            // Veri düzeltme migration'ını uygula
            using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(TargetMigration);
            }

            // Verify migration results
            using (var ctx = new AppDbContext(options))
            {
                var a = await ctx.WorksheetAssignments.IgnoreQueryFilters().FirstAsync(x => x.Id == 1);
                a.IsDeleted.ShouldBeTrue("(a) should be soft-deleted");
                a.DeleteUserId.ShouldBe(-236, "(a) DeleteUserId=-236");

                var b = await ctx.WorksheetAssignments.IgnoreQueryFilters().FirstAsync(x => x.Id == 2);
                b.IsDeleted.ShouldBeTrue("(b) should be soft-deleted");
                b.DeleteUserId.ShouldBe(-236, "(b) DeleteUserId=-236");

                var c = await ctx.WorksheetAssignments.FirstAsync(x => x.Id == 3);
                c.IsDeleted.ShouldBeFalse("(c) admin preserved");

                var d = await ctx.WorksheetAssignments.FirstAsync(x => x.Id == 4);
                d.IsDeleted.ShouldBeFalse("(d) school-scoped preserved");

                var e = await ctx.WorksheetAssignments.FirstAsync(x => x.Id == 5);
                e.IsDeleted.ShouldBeFalse("(e) direct student preserved");

                var r1 = await ctx.WorksheetReminders.IgnoreQueryFilters().FirstAsync(x => x.Id == 1);
                r1.Status.ShouldBe(WorksheetReminderStatus.Cancelled, "Reminder (f1) Cancelled");
                r1.HangfireJobId.ShouldBeNull("Reminder (f1) HangfireJobId null");

                var r2 = await ctx.WorksheetReminders.FirstAsync(x => x.Id == 2);
                r2.Status.ShouldBe(WorksheetReminderStatus.Pending, "Reminder (f2) stays Pending");
                r2.HangfireJobId.ShouldBe("job-002", "Reminder (f2) HangfireJobId preserved");
            }

            // Verify idempotency
            using (var ctx = new AppDbContext(options))
            {
                await ctx.Database.MigrateAsync();

                var a = await ctx.WorksheetAssignments.IgnoreQueryFilters().FirstAsync(x => x.Id == 1);
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
                var restored = await ctx.WorksheetAssignments.IgnoreQueryFilters()
                    .Where(x => x.Id == 1 || x.Id == 2).ToListAsync();
                restored.ShouldAllBe(x => !x.IsDeleted && x.DeleteUserId == null && x.DeleteTime == null);
            }
        }
        finally
        {
            DropTemporaryDatabase(tempConnStr);
        }
    }
}
