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
/// issue #367 — tamamlanmış teste cevap yazılamaz (gerçek PostgreSQL). <c>save-answer</c> instance satırını koşullu UPDATE ile
/// kilitleyip durumu aynı transaction'da doğrular; <c>end-test</c>'in koşullu UPDATE'i aynı satırı kilitler → ikisi serileşir.
/// Hangi taraf önce gelirse gelsin tamamlanmış teste cevap/outbox event'i yazılmaz.
/// </summary>
public class TestCompletionAnswerLockPostgresTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const int Rounds = 12;

    private static int NewUserId() => 1_000_000 + Random.Shared.Next(0, 900_000_000);

    private sealed record Seed(int UserId, int InstanceId, int TiqId, int CorrectAnswerId, int WrongAnswerId, int StudentId, int WorksheetId, int GradeId);

    private Task<Seed> SeedAsync() => WithDbAsync(async db =>
    {
        var userId = NewUserId();
        var grade = new Grade { Name = "5" };
        db.Grades.Add(grade);
        await db.SaveChangesAsync();

        var question = new Question { Text = "1+1?", Point = 10, DifficultyLevel = 1 };
        var worksheet = new Worksheet { Name = "Deneme", Description = "", GradeId = grade.Id };
        var student = new Student { UserId = userId, StudentNumber = $"S{userId}", SchoolName = "Okul", GradeId = grade.Id };
        db.AddRange(question, worksheet, student);
        await db.SaveChangesAsync();

        var correct = new Answer { QuestionId = question.Id, Text = "2", Tag = "A" };
        var wrong = new Answer { QuestionId = question.Id, Text = "3", Tag = "B" };
        db.Answers.AddRange(correct, wrong);
        await db.SaveChangesAsync();
        question.CorrectAnswerId = correct.Id;

        var wq = new WorksheetQuestion { TestId = worksheet.Id, QuestionId = question.Id, Order = 1 };
        db.TestQuestions.Add(wq);
        var instance = new WorksheetInstance
        {
            StudentId = student.Id, WorksheetId = worksheet.Id, StartTime = DateTime.UtcNow,
            Status = WorksheetInstanceStatus.Started
        };
        db.TestInstances.Add(instance);
        await db.SaveChangesAsync();

        var tiq = new WorksheetInstanceQuestion { WorksheetInstanceId = instance.Id, WorksheetQuestionId = wq.Id };
        db.TestInstanceQuestions.Add(tiq);
        await db.SaveChangesAsync();
        return new Seed(userId, instance.Id, tiq.Id, correct.Id, wrong.Id, student.Id, worksheet.Id, grade.Id);
    });

    private static SaveAnswerDto Dto(Seed seed, int selected) => new()
    {
        TestInstanceId = seed.InstanceId, TestQuestionId = seed.TiqId, SelectedAnswerId = selected, TimeTaken = 5
    };

    private async Task<TestSessionResultDto> SaveAnswerAsync(Seed seed, int selected, SemaphoreSlim? gate = null)
    {
        using var scope = Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITestSessionService>();
        if (gate != null)
            await gate.WaitAsync();
        return await service.SaveAnswer(Dto(seed, selected), new UserProfileDto { Id = seed.UserId, KeycloakId = $"kc-{seed.UserId}" });
    }

    private async Task<TestSessionResultDto> EndTestAsync(Seed seed, SemaphoreSlim? gate = null)
    {
        using var scope = Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITestSessionService>();
        if (gate != null)
            await gate.WaitAsync();
        return await service.EndTest(seed.InstanceId, seed.UserId);
    }

    private Task<List<AnswerSubmittedEvent>> AnswerEventsAsync(Seed seed) => WithDbAsync(async db =>
    {
        var type = OutboxEventRegistry.NameFor<AnswerSubmittedEvent>();
        return (await db.OutboxMessages.AsNoTracking().Where(m => m.Type == type).Select(m => m.Content).ToListAsync())
            .Select(c => JsonSerializer.Deserialize<AnswerSubmittedEvent>(c)!)
            .Where(e => e.TestInstanceId == seed.InstanceId)
            .ToList();
    });

    private Task<WorksheetInstanceQuestion> TiqAsync(Seed seed) =>
        WithDbAsync(db => db.TestInstanceQuestions.AsNoTracking().FirstAsync(t => t.Id == seed.TiqId));

    private static async Task ShouldStillBeWaitingAsync(Task task)
        => (await Task.WhenAny(task, Task.Delay(TimeSpan.FromMilliseconds(750)))).ShouldNotBe(task);

    [Fact]
    public async Task Answer_after_end_test_is_409_and_neither_the_answer_nor_the_score_changes()
    {
        var seed = await SeedAsync();
        var client = await ClientAsAsync(seed.UserId, "Student", $"kc-{seed.UserId}", "Student");

        var before = await client.PostAsJsonAsync("/api/worksheet/save-answer", Dto(seed, seed.WrongAnswerId));
        before.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await before.Content.ReadFromJsonAsync<TestSessionResultDto>(Json))!.Success.ShouldBeTrue();

        (await client.PutAsync($"/api/worksheet/end-test/{seed.InstanceId}", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        // Doğru cevabı gördükten sonra yanlışı doğruya çevirme denemesi.
        var after = await client.PostAsJsonAsync("/api/worksheet/save-answer", Dto(seed, seed.CorrectAnswerId));
        after.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        var body = (await after.Content.ReadFromJsonAsync<TestSessionResultDto>(Json))!;
        body.Success.ShouldBeFalse();
        body.Conflict.ShouldBeTrue();
        body.ErrorCode.ShouldBe(TestSessionErrorCodes.TestNotInProgress);
        body.Message.ShouldNotBeNullOrWhiteSpace();

        var tiq = await TiqAsync(seed);
        tiq.SelectedAnswerId.ShouldBe(seed.WrongAnswerId);
        tiq.IsCorrect.ShouldBeFalse();
        tiq.AnswerRevision.ShouldBe(1);
        var events = await AnswerEventsAsync(seed);
        events.Count.ShouldBe(1);
        events[0].IsCorrect.ShouldBeFalse();

        // end-test tekrarı idempotent: 200 + success.
        var again = await client.PutAsync($"/api/worksheet/end-test/{seed.InstanceId}", null);
        again.StatusCode.ShouldBe(HttpStatusCode.OK);
        (await again.Content.ReadFromJsonAsync<TestSessionResultDto>(Json))!.Success.ShouldBeTrue();
    }

    [Fact]
    public async Task SaveAnswer_waits_for_an_uncommitted_completion_and_is_then_refused()
    {
        var seed = await SeedAsync();

        // end-test transaction'ı instance satırını Completed'a çekti ama henüz commit etmedi.
        using var holderScope = Factory.Services.CreateScope();
        var holder = holderScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await holder.Database.BeginTransactionAsync();
        await holder.Database.ExecuteSqlRawAsync(
            """UPDATE "TestInstances" SET "Status" = {0}, "EndTime" = now() WHERE "Id" = {1}""",
            (int)WorksheetInstanceStatus.Completed, seed.InstanceId);

        var save = Task.Run(() => SaveAnswerAsync(seed, seed.CorrectAnswerId));

        // Kilitsiz bir durum okuması burada hâlâ Started görüp cevabı yazardı.
        await ShouldStillBeWaitingAsync(save);

        await tx.CommitAsync();
        var result = await save.WaitAsync(TimeSpan.FromSeconds(30));

        result.Conflict.ShouldBeTrue();
        result.ErrorCode.ShouldBe(TestSessionErrorCodes.TestNotInProgress);
        (await TiqAsync(seed)).SelectedAnswerId.ShouldBeNull();
        (await AnswerEventsAsync(seed)).ShouldBeEmpty();
    }

    [Fact]
    public async Task EndTest_waits_for_an_in_flight_answer_transaction_and_the_answer_survives_completion()
    {
        var seed = await SeedAsync();

        // save-answer transaction'ı instance kilidini aldı (koşullu no-op UPDATE), henüz commit etmedi.
        using var holderScope = Factory.Services.CreateScope();
        var holder = holderScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await using var tx = await holder.Database.BeginTransactionAsync();
        var locked = await holder.Database.ExecuteSqlRawAsync(
            """UPDATE "TestInstances" SET "Status" = "Status" WHERE "Id" = {0} AND "Status" = {1}""",
            seed.InstanceId, (int)WorksheetInstanceStatus.Started);
        locked.ShouldBe(1);
        // ...ve (SaveAnswer gibi) aynı transaction'da cevabı yazdı.
        await holder.Database.ExecuteSqlRawAsync(
            """UPDATE "TestInstanceQuestions" SET "SelectedAnswerId" = {0}, "IsCorrect" = TRUE, "AnswerRevision" = "AnswerRevision" + 1 WHERE "Id" = {1}""",
            seed.CorrectAnswerId, seed.TiqId);

        var end = Task.Run(() => EndTestAsync(seed));
        await ShouldStillBeWaitingAsync(end);

        await tx.CommitAsync();
        (await end.WaitAsync(TimeSpan.FromSeconds(30))).Success.ShouldBeTrue();
        (await WithDbAsync(db => db.TestInstances.AsNoTracking().FirstAsync(i => i.Id == seed.InstanceId)))
            .Status.ShouldBe(WorksheetInstanceStatus.Completed);
        var tiq = await TiqAsync(seed);
        tiq.SelectedAnswerId.ShouldBe(seed.CorrectAnswerId);
        tiq.IsCorrect.ShouldBeTrue();
        tiq.AnswerRevision.ShouldBe(1);
    }

    [Fact]
    public async Task Parallel_answer_and_completion_never_write_an_answer_to_a_completed_test()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var seed = await SeedAsync();
            using var gate = new SemaphoreSlim(0, 2);

            var save = Task.Run(() => SaveAnswerAsync(seed, seed.CorrectAnswerId, gate));
            var end = Task.Run(() => EndTestAsync(seed, gate));
            gate.Release(2);
            await Task.WhenAll(save, end);

            (await end).Success.ShouldBeTrue($"round {round}");
            var saved = await save;
            var tiq = await TiqAsync(seed);
            var events = await AnswerEventsAsync(seed);

            if (saved.Success)
            {
                // Cevap kilidi önce aldı → tamamlanmadan önce yazıldı ve tek event üretti.
                tiq.SelectedAnswerId.ShouldBe(seed.CorrectAnswerId, $"round {round}");
                events.Count.ShouldBe(1, $"round {round}");
            }
            else
            {
                saved.ErrorCode.ShouldBe(TestSessionErrorCodes.TestNotInProgress, $"round {round}");
                tiq.SelectedAnswerId.ShouldBeNull($"round {round}");
                tiq.AnswerRevision.ShouldBe(0, $"round {round}");
                events.ShouldBeEmpty($"round {round}");
            }
        }
    }

    [Fact]
    public async Task Parallel_end_test_calls_both_succeed_and_complete_once()
    {
        var seed = await SeedAsync();
        using var gate = new SemaphoreSlim(0, 2);

        var first = Task.Run(() => EndTestAsync(seed, gate));
        var second = Task.Run(() => EndTestAsync(seed, gate));
        gate.Release(2);
        await Task.WhenAll(first, second);

        (await first).Success.ShouldBeTrue();
        (await second).Success.ShouldBeTrue();
        var instance = await WithDbAsync(db => db.TestInstances.AsNoTracking().FirstAsync(i => i.Id == seed.InstanceId));
        instance.Status.ShouldBe(WorksheetInstanceStatus.Completed);
        instance.EndTime.ShouldNotBeNull();
    }
    // ---- issue #367 (security review): no retakes ----

    private Task<int> InstanceCountAsync(Seed seed) => WithDbAsync(db =>
        db.TestInstances.AsNoTracking().CountAsync(i => i.StudentId == seed.StudentId && i.WorksheetId == seed.WorksheetId));

    [Fact]
    public async Task Start_test_after_completion_returns_alreadyCompleted_and_opens_no_new_instance()
    {
        var seed = await SeedAsync();
        var client = await ClientAsAsync(seed.UserId, "Student", $"kc-{seed.UserId}", "Student");

        (await client.PutAsync($"/api/worksheet/end-test/{seed.InstanceId}", null)).StatusCode.ShouldBe(HttpStatusCode.OK);

        var retake = await client.PostAsync($"/api/worksheet/start-test/{seed.WorksheetId}", null);
        retake.StatusCode.ShouldBe(HttpStatusCode.OK);
        var body = (await retake.Content.ReadFromJsonAsync<TestStartResultDto>(Json))!;
        body.Success.ShouldBeFalse();
        body.InstanceId.ShouldBe(seed.InstanceId);
        (await InstanceCountAsync(seed)).ShouldBe(1);
    }

    [Fact]
    public async Task Parallel_start_test_calls_open_a_single_instance()
    {
        for (var round = 0; round < Rounds; round++)
        {
            var seed = await SeedAsync();
            // Seed açık bir instance ile gelir; ilk start yarışı için onu sil (soft-delete → index dışı).
            await WithDbAsync(db => db.TestInstances.Where(i => i.Id == seed.InstanceId)
                .ExecuteUpdateAsync(u => u.SetProperty(i => i.IsDeleted, true)));

            using var gate = new SemaphoreSlim(0, 2);
            async Task<TestStartResultDto> StartAsync()
            {
                using var scope = Factory.Services.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<ITestSessionService>();
                await gate.WaitAsync();
                return await service.StartTestAsync(seed.WorksheetId, new StudentProfileDto { Id = seed.StudentId, GradeId = seed.GradeId });
            }

            var a = Task.Run(StartAsync);
            var b = Task.Run(StartAsync);
            gate.Release(2);
            await Task.WhenAll(a, b);

            (await a).Success.ShouldBeTrue($"round {round}");
            (await b).Success.ShouldBeTrue($"round {round}");
            (await a).InstanceId.ShouldBe((await b).InstanceId, $"round {round}");
            (await InstanceCountAsync(seed)).ShouldBe(1, $"round {round}");
        }
    }
    // ---- issue #367: migration ön kontrolü (unique index öncesi canlı çift instance) ----

    private const string PreviousMigration = "20261005214609_AddDirectMessaging";
    private const string UniqueInstanceMigration = "20261006102622_AddUniqueTestInstancePerStudentWorksheet";

    [Fact]
    public async Task Unique_instance_migration_stops_on_live_duplicates_and_applies_after_they_are_soft_deleted()
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
            int retakeId;
            await using (var ctx = new AppDbContext(options))
            {
                await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
                var grade = new Grade { Name = "5" };
                ctx.Grades.Add(grade);
                await ctx.SaveChangesAsync();
                var student = new Student { UserId = 1, StudentNumber = "S1", SchoolName = "Okul" };
                var worksheet = new Worksheet { Name = "W", Description = "", GradeId = grade.Id };
                ctx.AddRange(student, worksheet);
                await ctx.SaveChangesAsync();
                var first = new WorksheetInstance
                {
                    StudentId = student.Id, WorksheetId = worksheet.Id, Status = WorksheetInstanceStatus.Completed,
                    StartTime = DateTime.UtcNow.AddHours(-2), EndTime = DateTime.UtcNow.AddHours(-1)
                };
                var retake = new WorksheetInstance
                {
                    StudentId = student.Id, WorksheetId = worksheet.Id, Status = WorksheetInstanceStatus.Completed,
                    StartTime = DateTime.UtcNow.AddMinutes(-30), EndTime = DateTime.UtcNow
                };
                ctx.TestInstances.AddRange(first, retake);
                await ctx.SaveChangesAsync();
                retakeId = retake.Id;
            }

            await using (var ctx = new AppDbContext(options))
            {
                var ex = await Should.ThrowAsync<PostgresException>(() => ctx.GetService<IMigrator>().MigrateAsync(UniqueInstanceMigration));
                ex.SqlState.ShouldBe(PostgresErrorCodes.RaiseException);
                ex.MessageText.ShouldContain("issue #367");
                ex.MessageText.ShouldContain("1 (StudentId/WorksheetId:");
            }

            await using (var ctx = new AppDbContext(options))
            {
                (await ctx.TestInstances.IgnoreQueryFilters().CountAsync()).ShouldBe(2);
                (await ctx.Database.GetAppliedMigrationsAsync()).ShouldNotContain(UniqueInstanceMigration);

                await ctx.Database.ExecuteSqlRawAsync("""UPDATE "TestInstances" SET "IsDeleted" = TRUE WHERE "Id" = {0}""", retakeId);
                await ctx.GetService<IMigrator>().MigrateAsync(UniqueInstanceMigration);
                (await ctx.Database.GetAppliedMigrationsAsync()).ShouldContain(UniqueInstanceMigration);

                // Canlı ikinci instance artık reddedilir.
                var violation = await Should.ThrowAsync<PostgresException>(() => ctx.Database.ExecuteSqlRawAsync(
                    """UPDATE "TestInstances" SET "IsDeleted" = FALSE WHERE "Id" = {0}""", retakeId));
                violation.SqlState.ShouldBe(PostgresErrorCodes.UniqueViolation);

                await ctx.GetService<IMigrator>().MigrateAsync(PreviousMigration);
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
