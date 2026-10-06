using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #396 — server-side time limit on real PostgreSQL. An overdue Started instance (StartTime + MaxDurationSeconds +
/// 30 s passed) takes no answer (409 TestNotInProgress) and is marked Expired at request time. The Expired write is a
/// conditional UPDATE on the same row SaveAnswer/EndTest lock (#367), so parallel requests expire it exactly once and
/// none of them writes an answer or an outbox event.
/// </summary>
public class TestTimeLimitPostgresTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const int Rounds = 10;
    private const int Limit = 600;

    private static int NewUserId() => 1_000_000 + Random.Shared.Next(0, 900_000_000);

    private sealed record Seed(int UserId, int InstanceId, int TiqId, int CorrectAnswerId, int StudentId, int WorksheetId, int GradeId);

    private static TimeSpan Overdue => TimeSpan.FromSeconds(Limit) + TestTimeLimit.Tolerance + TimeSpan.FromSeconds(5);

    private Task<Seed> SeedAsync(TimeSpan startedAgo) => WithDbAsync(async db =>
    {
        var userId = NewUserId();
        var grade = new Grade { Name = "5" };
        db.Grades.Add(grade);
        await db.SaveChangesAsync();

        var question = new Question { Text = "1+1?", Point = 10, DifficultyLevel = 1 };
        var worksheet = new Worksheet { Name = "Süreli", Description = "", GradeId = grade.Id, MaxDurationSeconds = Limit };
        var student = new Student { UserId = userId, StudentNumber = $"S{userId}", SchoolName = "Okul", GradeId = grade.Id };
        db.AddRange(question, worksheet, student);
        await db.SaveChangesAsync();

        var correct = new Answer { QuestionId = question.Id, Text = "2", Tag = "A" };
        db.Answers.Add(correct);
        await db.SaveChangesAsync();
        question.CorrectAnswerId = correct.Id;

        var wq = new WorksheetQuestion { TestId = worksheet.Id, QuestionId = question.Id, Order = 1 };
        db.TestQuestions.Add(wq);
        var instance = new WorksheetInstance
        {
            StudentId = student.Id, WorksheetId = worksheet.Id, StartTime = DateTime.UtcNow - startedAgo,
            Status = WorksheetInstanceStatus.Started, MaxDurationSeconds = Limit
        };
        db.TestInstances.Add(instance);
        await db.SaveChangesAsync();

        var tiq = new WorksheetInstanceQuestion { WorksheetInstanceId = instance.Id, WorksheetQuestionId = wq.Id };
        db.TestInstanceQuestions.Add(tiq);
        await db.SaveChangesAsync();
        return new Seed(userId, instance.Id, tiq.Id, correct.Id, student.Id, worksheet.Id, grade.Id);
    });

    private static SaveAnswerDto Dto(Seed seed) => new()
    {
        TestInstanceId = seed.InstanceId, TestQuestionId = seed.TiqId, SelectedAnswerId = seed.CorrectAnswerId, TimeTaken = 5
    };

    private async Task<TestSessionResultDto> SaveAnswerAsync(Seed seed, SemaphoreSlim gate)
    {
        using var scope = Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITestSessionService>();
        await gate.WaitAsync();
        return await service.SaveAnswer(Dto(seed), new UserProfileDto { Id = seed.UserId, KeycloakId = $"kc-{seed.UserId}" });
    }

    private async Task<TestSessionResultDto> EndTestAsync(Seed seed, SemaphoreSlim gate)
    {
        using var scope = Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITestSessionService>();
        await gate.WaitAsync();
        return await service.EndTest(seed.InstanceId, seed.UserId);
    }

    private Task<WorksheetInstance> InstanceAsync(Seed seed) =>
        WithDbAsync(db => db.TestInstances.AsNoTracking().FirstAsync(i => i.Id == seed.InstanceId));

    private Task<WorksheetInstanceQuestion> TiqAsync(Seed seed) =>
        WithDbAsync(db => db.TestInstanceQuestions.AsNoTracking().FirstAsync(t => t.Id == seed.TiqId));

    private Task<int> AnswerEventCountAsync(Seed seed) => WithDbAsync(async db =>
    {
        var type = OutboxEventRegistry.NameFor<AnswerSubmittedEvent>();
        return (await db.OutboxMessages.AsNoTracking().Where(m => m.Type == type).Select(m => m.Content).ToListAsync())
            .Select(c => JsonSerializer.Deserialize<AnswerSubmittedEvent>(c)!)
            .Count(e => e.TestInstanceId == seed.InstanceId);
    });

    private static async Task ShouldBeNotInProgressAsync(HttpResponseMessage response)
    {
        response.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var body = (await response.Content.ReadFromJsonAsync<TestSessionResultDto>(Json))!;
        body.Success.ShouldBeFalse();
        body.ErrorCode.ShouldBe(TestSessionErrorCodes.TestNotInProgress);
        body.Reason.ShouldBe(TestSessionRejectReasons.TimeExpired);
    }

    [Fact]
    public async Task Overdue_save_answer_and_end_test_are_409_and_the_instance_is_expired()
    {
        var seed = await SeedAsync(Overdue);
        var client = await ClientAsAsync(seed.UserId, "Student", $"kc-{seed.UserId}", "Student");

        await ShouldBeNotInProgressAsync(await client.PostAsJsonAsync("/api/worksheet/save-answer", Dto(seed)));

        var instance = await InstanceAsync(seed);
        instance.Status.ShouldBe(WorksheetInstanceStatus.Expired);
        instance.EndTime.ShouldNotBeNull();
        instance.EndTime!.Value.ShouldBe(instance.StartTime.AddSeconds(Limit), TimeSpan.FromMilliseconds(1));
        (await TiqAsync(seed)).SelectedAnswerId.ShouldBeNull();
        (await AnswerEventCountAsync(seed)).ShouldBe(0);

        await ShouldBeNotInProgressAsync(await client.PutAsync($"/api/worksheet/end-test/{seed.InstanceId}", null));

        // start-test: no retake (#367) — alreadyCompleted-like response with the expired instance.
        var start = await client.PostAsync($"/api/worksheet/start-test/{seed.WorksheetId}", null);
        start.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = (await start.Content.ReadFromJsonAsync<TestStartResultDto>(Json))!;
        body.Success.ShouldBeFalse();
        body.InstanceId.ShouldBe(seed.InstanceId);
        (await WithDbAsync(db => db.TestInstances.AsNoTracking()
            .CountAsync(i => i.StudentId == seed.StudentId && i.WorksheetId == seed.WorksheetId))).ShouldBe(1);
    }

    [Fact]
    public async Task Overdue_end_test_via_http_expires_instead_of_completing()
    {
        var seed = await SeedAsync(Overdue);
        var client = await ClientAsAsync(seed.UserId, "Student", $"kc-{seed.UserId}", "Student");

        await ShouldBeNotInProgressAsync(await client.PutAsync($"/api/worksheet/end-test/{seed.InstanceId}", null));
        (await InstanceAsync(seed)).Status.ShouldBe(WorksheetInstanceStatus.Expired);

        // The result page is available for an expired test.
        var result = await client.GetAsync($"/api/worksheet/test-canvas-instance-result/{seed.InstanceId}");
        result.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await result.Content.ReadFromJsonAsync<WorksheetInstanceResultDto>(Json))!.Status.ShouldBe(WorksheetInstanceStatus.Expired);
    }

    [Fact]
    public async Task Get_endpoints_report_expiry_and_remaining_time_without_writing()
    {
        var overdue = await SeedAsync(Overdue);
        var client = await ClientAsAsync(overdue.UserId, "Student", $"kc-{overdue.UserId}", "Student");

        var solve = await client.GetAsync($"/api/worksheet/test-canvas-instance/{overdue.InstanceId}");
        solve.StatusCode.ShouldBe(HttpStatusCode.OK);
        var dto = (await solve.Content.ReadFromJsonAsync<WorksheetInstanceResultDto>(Json))!;
        dto.Status.ShouldBe(WorksheetInstanceStatus.Expired);
        dto.RemainingSeconds.ShouldBe(0);
        (await InstanceAsync(overdue)).Status.ShouldBe(WorksheetInstanceStatus.Started); // GET is pure

        var running = await SeedAsync(TimeSpan.FromSeconds(100));
        var client2 = await ClientAsAsync(running.UserId, "Student", $"kc-{running.UserId}", "Student");
        var dto2 = (await (await client2.GetAsync($"/api/worksheet/test-canvas-instance/{running.InstanceId}"))
            .Content.ReadFromJsonAsync<WorksheetInstanceResultDto>(Json))!;
        dto2.Status.ShouldBe(WorksheetInstanceStatus.Started);
        dto2.MaxDurationSeconds.ShouldBe(Limit);
        dto2.RemainingSeconds!.Value.ShouldBeInRange(Limit - 103, Limit - 97);
    }

    [Fact]
    public async Task Sweeper_expires_an_abandoned_overdue_session_on_postgres()
    {
        var overdue = await SeedAsync(Overdue);
        var running = await SeedAsync(TimeSpan.FromSeconds(Limit + 10)); // inside the tolerance

        using (var scope = Factory.Services.CreateScope())
        {
            var sweeper = scope.ServiceProvider.GetRequiredService<IExpiredTestInstanceSweepJob>();
            (await sweeper.SweepAsync()).ShouldBeGreaterThanOrEqualTo(1);
        }

        var expired = await InstanceAsync(overdue);
        expired.Status.ShouldBe(WorksheetInstanceStatus.Expired);
        expired.EndTime!.Value.ShouldBe(expired.StartTime.AddSeconds(Limit), TimeSpan.FromMilliseconds(1));
        expired.UpdateUserId.ShouldBeNull();
        (await InstanceAsync(running)).Status.ShouldBe(WorksheetInstanceStatus.Started);
    }

    [Fact]
    public async Task Start_test_copies_the_limit_and_a_later_worksheet_change_does_not_shorten_the_session()
    {
        var seed = await SeedAsync(TimeSpan.Zero);
        // Fresh start: drop the seeded instance (soft-delete → outside the unique index), start through the API.
        await WithDbAsync(db => db.TestInstances.Where(i => i.Id == seed.InstanceId)
            .ExecuteUpdateAsync(u => u.SetProperty(i => i.IsDeleted, true)));
        var client = await ClientAsAsync(seed.UserId, "Student", $"kc-{seed.UserId}", "Student");
        var start = (await (await client.PostAsync($"/api/worksheet/start-test/{seed.WorksheetId}", null))
            .Content.ReadFromJsonAsync<TestStartResultDto>(Json))!;
        start.Success.ShouldBeTrue();

        // Teacher shortens the worksheet to 1 s afterwards.
        await WithDbAsync(db => db.Worksheets.Where(w => w.Id == seed.WorksheetId)
            .ExecuteUpdateAsync(u => u.SetProperty(w => w.MaxDurationSeconds, 1)));
        await Task.Delay(TimeSpan.FromSeconds(2));

        var instance = await WithDbAsync(db => db.TestInstances.AsNoTracking().FirstAsync(i => i.Id == start.InstanceId));
        instance.MaxDurationSeconds.ShouldBe(Limit);
        var tiqId = await WithDbAsync(db => db.TestInstanceQuestions.Where(t => t.WorksheetInstanceId == start.InstanceId)
            .Select(t => t.Id).FirstAsync());
        var save = await client.PostAsJsonAsync("/api/worksheet/save-answer", new SaveAnswerDto
        {
            TestInstanceId = start.InstanceId, TestQuestionId = tiqId, SelectedAnswerId = seed.CorrectAnswerId, TimeTaken = 2
        });
        save.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Answer_inside_the_tolerance_is_accepted()
    {
        var seed = await SeedAsync(TimeSpan.FromSeconds(Limit + 10));
        var client = await ClientAsAsync(seed.UserId, "Student", $"kc-{seed.UserId}", "Student");

        var response = await client.PostAsJsonAsync("/api/worksheet/save-answer", Dto(seed));
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await InstanceAsync(seed)).Status.ShouldBe(WorksheetInstanceStatus.Started);
        (await AnswerEventCountAsync(seed)).ShouldBe(1);
    }

    [Fact]
    public async Task Parallel_answer_and_end_test_on_an_overdue_instance_expire_it_once_and_write_nothing()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var seed = await SeedAsync(Overdue);
            using var gate = new SemaphoreSlim(0, 3);

            var save1 = Task.Run(() => SaveAnswerAsync(seed, gate));
            var save2 = Task.Run(() => SaveAnswerAsync(seed, gate));
            var end = Task.Run(() => EndTestAsync(seed, gate));
            gate.Release(3);
            await Task.WhenAll(save1, save2, end);

            foreach (var r in new[] { await save1, await save2, await end })
            {
                r.Success.ShouldBeFalse($"round {round}");
                r.Conflict.ShouldBeTrue($"round {round}");
                r.ErrorCode.ShouldBe(TestSessionErrorCodes.TestNotInProgress, $"round {round}");
            }

            var instance = await InstanceAsync(seed);
            instance.Status.ShouldBe(WorksheetInstanceStatus.Expired, $"round {round}");
            instance.EndTime!.Value.ShouldBe(instance.StartTime.AddSeconds(Limit), TimeSpan.FromMilliseconds(1), $"round {round}");
            var tiq = await TiqAsync(seed);
            tiq.SelectedAnswerId.ShouldBeNull($"round {round}");
            tiq.AnswerRevision.ShouldBe(0, $"round {round}");
            (await AnswerEventCountAsync(seed)).ShouldBe(0, $"round {round}");
        }
    }

    [Fact]
    public async Task An_overdue_answer_waits_for_an_in_flight_completion_and_does_not_overwrite_it()
    {
        // EndTest won the row lock before the deadline check could run (completed in time, committing now):
        // the overdue SaveAnswer waits, then sees Completed → 409, and must not flip the instance to Expired.
        var seed = await SeedAsync(Overdue);

        using var holderScope = Factory.Services.CreateScope();
        var holder = holderScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await holder.Database.BeginTransactionAsync();
        await holder.Database.ExecuteSqlRawAsync(
            """UPDATE "TestInstances" SET "Status" = {0}, "EndTime" = now() WHERE "Id" = {1}""",
            (int)WorksheetInstanceStatus.Completed, seed.InstanceId);

        using var gate = new SemaphoreSlim(1, 1);
        var save = Task.Run(() => SaveAnswerAsync(seed, gate));
        (await Task.WhenAny(save, Task.Delay(TimeSpan.FromMilliseconds(750)))).ShouldNotBe(save);

        await tx.CommitAsync();
        var result = await save.WaitAsync(TimeSpan.FromSeconds(30));

        result.ErrorCode.ShouldBe(TestSessionErrorCodes.TestNotInProgress);
        (await InstanceAsync(seed)).Status.ShouldBe(WorksheetInstanceStatus.Completed);
        (await TiqAsync(seed)).SelectedAnswerId.ShouldBeNull();
        (await AnswerEventCountAsync(seed)).ShouldBe(0);
    }

    // ---- migration backfill (hand-written SQL in AddTestInstanceMaxDurationSnapshot.Up) ----

    private const string BeforeSnapshotMigration = "20261006102622_AddUniqueTestInstancePerStudentWorksheet";
    private const string SnapshotMigration = "AddTestInstanceMaxDurationSnapshot";

    [Fact]
    public async Task Snapshot_migration_backfills_existing_instances_from_the_worksheet()
    {
        var builder = new NpgsqlConnectionStringBuilder(Factory.ConnectionString);
        var tempDb = $"exam_snapshot_migration_{Guid.NewGuid().ToString("N")[..8]}";
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
            await using var ctx = new AppDbContext(options);
            var migrator = ctx.GetService<IMigrator>();
            await migrator.MigrateAsync(BeforeSnapshotMigration);

            // Old schema: raw SQL (the current model has the new column). One CTE chain so only our rows are joined.
            var instanceId = (await ctx.Database.SqlQueryRaw<int>("""
                WITH g AS (INSERT INTO "Grades" ("Name") VALUES ('5') RETURNING "Id"),
                     w AS (INSERT INTO "Worksheets" ("Name", "Description", "GradeId", "MaxDurationSeconds", "IsPracticeTest", "CreateTime", "IsDeleted")
                           SELECT 'W', '', g."Id", 900, FALSE, now(), TRUE FROM g RETURNING "Id"),
                     s AS (INSERT INTO "Students" ("UserId", "StudentNumber", "SchoolName", "CreateTime", "IsDeleted")
                           VALUES (987654, 'S1', 'Okul', now(), FALSE) RETURNING "Id")
                INSERT INTO "TestInstances" ("StudentId", "WorksheetId", "Status", "StartTime", "CreateTime", "IsDeleted")
                SELECT s."Id", w."Id", 0, now(), now(), FALSE FROM s, w
                RETURNING "Id" AS "Value"
                """).ToListAsync()).Single();

            await migrator.MigrateAsync(SnapshotMigration);

            // Retired (soft-deleted) worksheet still provides the limit.
            (await ctx.TestInstances.IgnoreQueryFilters().Where(i => i.Id == instanceId)
                .Select(i => i.MaxDurationSeconds).SingleAsync()).ShouldBe(900);

            await migrator.MigrateAsync(BeforeSnapshotMigration); // Down() drops the column cleanly
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
