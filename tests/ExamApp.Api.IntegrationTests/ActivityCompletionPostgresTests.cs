using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.IntegrationTests.Infrastructure;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Worksheets;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.IntegrationTests;

/// <summary>
/// issue #572 on real PostgreSQL: etkinlik bitirme (<c>EndTest</c>) eşzamanlı çağrılarda bile TEK bir
/// <see cref="ActivityCompletedEvent"/> outbox satırı yazar (koşullu UPDATE satır kilidi + aynı transaction); etkinlik
/// puan / cevap event'i üretmez; <c>ActiveSeconds</c> kolonu migration ile gelir ve doğru kalıcı olur.
/// </summary>
public class ActivityCompletionPostgresTests(IntegrationApiFactory factory) : IntegrationTestBase(factory)
{
    private const int ReportedSeconds = 420;

    private static int NewUserId() => 1_000_000 + Random.Shared.Next(0, 900_000_000);

    private sealed record Seed(int UserId, int InstanceId, int WorksheetId);

    private Task<Seed> SeedActivityAsync() => WithDbAsync(async db =>
    {
        var userId = NewUserId();
        var grade = new Grade { Name = "5" };
        db.Grades.Add(grade);
        await db.SaveChangesAsync();

        var question = new Question { Text = "1+1?", Point = 10, DifficultyLevel = 1 };
        var worksheet = new Worksheet
        {
            Name = "Etkinlik", Description = "", GradeId = grade.Id, Kind = WorksheetKind.Activity,
        };
        var student = new Student { UserId = userId, StudentNumber = $"S{userId}", SchoolName = "Okul", GradeId = grade.Id };
        db.AddRange(question, worksheet, student);
        await db.SaveChangesAsync();

        var correct = new Answer { QuestionId = question.Id, Text = "2", Tag = "A" };
        db.Answers.Add(correct);
        await db.SaveChangesAsync();
        question.CorrectAnswerId = correct.Id;

        var worksheetQuestion = new WorksheetQuestion { TestId = worksheet.Id, QuestionId = question.Id, Order = 1 };
        db.TestQuestions.Add(worksheetQuestion);
        // Etkinlik yalnız en az bir "Cevapları kontrol et"ten sonra bitirilebilir (issue #573) → CheckCount = 1.
        var instance = new WorksheetInstance
        {
            // Geçen süre sınırı (ActiveSeconds ≤ geçen süre): oturum bir saat önce başlamış sayılır.
            StudentId = student.Id, WorksheetId = worksheet.Id, StartTime = DateTime.UtcNow.AddHours(-1),
            Status = WorksheetInstanceStatus.Started, CheckCount = 1,
        };
        db.TestInstances.Add(instance);
        await db.SaveChangesAsync();
        // M2: tek, cevapsız madde (ItemCount = 1, AnsweredItemCount = 0).
        db.TestInstanceQuestions.Add(new WorksheetInstanceQuestion { WorksheetInstanceId = instance.Id, WorksheetQuestionId = worksheetQuestion.Id });
        await db.SaveChangesAsync();
        return new Seed(userId, instance.Id, worksheet.Id);
    });

    /// <summary>
    /// review M1: aynı öğrenciye ikinci bir etkinlik oturumu (aynı başlangıç, CheckCount = 1). Worksheet aynı sınıfta, tek madde.
    /// </summary>
    private Task<Seed> SeedParallelActivityAsync(Seed first) => WithDbAsync(async db =>
    {
        var original = await db.TestInstances.AsNoTracking().Where(x => x.Id == first.InstanceId)
            .Select(x => new { x.StudentId, x.StartTime, x.Worksheet.GradeId }).SingleAsync();
        var questionId = await db.TestQuestions.AsNoTracking().Where(x => x.TestId == first.WorksheetId).Select(x => x.QuestionId).FirstAsync();
        var worksheet = new Worksheet { Name = "Etkinlik 2", Description = "", GradeId = original.GradeId, Kind = WorksheetKind.Activity };
        db.Worksheets.Add(worksheet);
        await db.SaveChangesAsync();
        db.TestQuestions.Add(new WorksheetQuestion { TestId = worksheet.Id, QuestionId = questionId, Order = 1 });
        var instance = new WorksheetInstance
        {
            StudentId = original.StudentId, WorksheetId = worksheet.Id, StartTime = original.StartTime,
            Status = WorksheetInstanceStatus.Started, CheckCount = 1,
        };
        db.TestInstances.Add(instance);
        await db.SaveChangesAsync();
        return new Seed(first.UserId, instance.Id, worksheet.Id);
    });

    private async Task<TestSessionResultDto> EndTestAsync(Seed seed, int activeSeconds)
    {
        using var scope = Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITestSessionService>();
        return await service.EndTest(
            seed.InstanceId, seed.UserId, new EndTestRequestDto { ActiveSeconds = activeSeconds }, $"kc-{seed.UserId}");
    }

    private async Task<TestSessionResultDto> EndTestAsync(Seed seed)
    {
        using var scope = Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITestSessionService>();
        return await service.EndTest(
            seed.InstanceId, seed.UserId, new EndTestRequestDto { ActiveSeconds = ReportedSeconds }, $"kc-{seed.UserId}");
    }

    private Task<List<ActivityCompletedEvent>> ActivityEventsAsync(Seed seed) => WithDbAsync(async db =>
    {
        var type = OutboxEventRegistry.NameFor<ActivityCompletedEvent>();
        return (await db.OutboxMessages.AsNoTracking().Where(m => m.Type == type).Select(m => m.Content).ToListAsync())
            .Select(c => JsonSerializer.Deserialize<ActivityCompletedEvent>(c)!)
            .Where(e => e.WorksheetInstanceId == seed.InstanceId)
            .ToList();
    });

    private Task<int> OutboxCountAsync(Type eventType) => WithDbAsync(db =>
    {
        var type = OutboxEventRegistry.NameFor(eventType);
        return db.OutboxMessages.AsNoTracking().CountAsync(m => m.Type == type);
    });

    [Fact]
    public async Task Concurrent_end_test_calls_write_exactly_one_activity_completed_event()
    {
        var seed = await SeedActivityAsync();

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => EndTestAsync(seed)));

        results.ShouldAllBe(r => r.Success);
        var events = await ActivityEventsAsync(seed);
        events.Count.ShouldBe(1);
        events[0].ActivityId.ShouldBe(seed.WorksheetId);
        events[0].UserId.ShouldBe(seed.UserId);
        events[0].WorksheetInstanceId.ShouldBe(seed.InstanceId);
        events[0].ActiveSeconds.ShouldBe(ReportedSeconds);
        events[0].ItemCount.ShouldBe(1);
        events[0].AnsweredItemCount.ShouldBe(0); // M2: seed'deki madde cevapsız, event yine yazılır
        events[0].EventId.ShouldNotBe(Guid.Empty);
    }

    [Fact]
    public async Task Repeated_end_test_after_completion_writes_no_second_event_and_keeps_active_seconds()
    {
        var seed = await SeedActivityAsync();
        (await EndTestAsync(seed)).Success.ShouldBeTrue();
        (await EndTestAsync(seed)).Success.ShouldBeTrue();

        (await ActivityEventsAsync(seed)).Count.ShouldBe(1);

        var activeSeconds = await WithDbAsync(db => db.TestInstances.AsNoTracking()
            .Where(x => x.Id == seed.InstanceId).Select(x => x.ActiveSeconds).SingleAsync());
        activeSeconds.ShouldBe(ReportedSeconds);
    }

    [Fact]
    public async Task Activity_completion_writes_no_answer_or_points_events()
    {
        var seed = await SeedActivityAsync();
        (await EndTestAsync(seed)).Success.ShouldBeTrue();

        (await ActivityEventsAsync(seed)).Count.ShouldBe(1);
        (await OutboxCountAsync(typeof(AnswerSubmittedEvent))).ShouldBe(0);
        (await OutboxCountAsync(typeof(StudentPointsChangedEvent))).ShouldBe(0);
    }

    [Fact]
    public async Task Two_parallel_started_activities_finished_sequentially_second_gets_only_non_overlapping_seconds()
    {
        // Oturumlar bir saat önce birlikte açıldı; ilk bitiren 30 dk'nın tamamını alır, ikinci aynı pencereyi tekrar sayamaz.
        var first = await SeedActivityAsync();
        var second = await SeedParallelActivityAsync(first);

        (await EndTestAsync(first, 1800)).Success.ShouldBeTrue();
        (await EndTestAsync(second, 1800)).Success.ShouldBeTrue();

        (await ActivityEventsAsync(first)).ShouldHaveSingleItem().ActiveSeconds.ShouldBe(1800);
        (await ActivityEventsAsync(second)).ShouldHaveSingleItem().ActiveSeconds.ShouldBeInRange(0, 5);
    }

    [Fact]
    public async Task Two_parallel_started_activities_finished_concurrently_do_not_both_claim_the_window()
    {
        var first = await SeedActivityAsync();
        var second = await SeedParallelActivityAsync(first);

        var results = await Task.WhenAll(EndTestAsync(first, 1800), EndTestAsync(second, 1800));

        results.ShouldAllBe(r => r.Success);
        var total = (await ActivityEventsAsync(first)).Single().ActiveSeconds + (await ActivityEventsAsync(second)).Single().ActiveSeconds;
        total.ShouldBeLessThanOrEqualTo(1805); // kilitsiz ikisi de 1800 alırdı (3600)
    }

    // ---------------------------------------------------------------- issue #609: yeniden çöz (aynı oturum sıfırlanır)

    private async Task<ActivityRetakeResultDto> RetakeAsync(Seed seed)
    {
        using var scope = Factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<ITestSessionService>();
        return await service.RetakeActivityAsync(seed.InstanceId, seed.UserId);
    }

    [Fact]
    public async Task Concurrent_retakes_reopen_the_instance_once_and_keep_first_completion()
    {
        var seed = await SeedActivityAsync();
        (await EndTestAsync(seed)).Success.ShouldBeTrue();
        var firstEnd = await WithDbAsync(db => db.TestInstances.AsNoTracking()
            .Where(x => x.Id == seed.InstanceId).Select(x => x.EndTime).SingleAsync());

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => RetakeAsync(seed)));

        results.ShouldAllBe(r => r.Success && r.InstanceId == seed.InstanceId);
        results.Count(r => !r.AlreadyRetaking).ShouldBe(1); // yalnız biri sıfırladı
        var instance = await WithDbAsync(db => db.TestInstances.AsNoTracking().SingleAsync(x => x.Id == seed.InstanceId));
        instance.Status.ShouldBe(WorksheetInstanceStatus.Started);
        instance.EndTime.ShouldBeNull();
        instance.CheckCount.ShouldBe(0);
        instance.FirstCompletedAt.ShouldBe(firstEnd);
        instance.ActiveSeconds.ShouldBe(ReportedSeconds);
        (await WithDbAsync(db => db.TestInstances.AsNoTracking().CountAsync(x => x.WorksheetId == seed.WorksheetId))).ShouldBe(1);
    }

    [Fact]
    public async Task Recompleting_a_retaken_activity_writes_a_second_event_for_the_same_instance_and_accumulates_seconds()
    {
        var seed = await SeedActivityAsync();
        (await EndTestAsync(seed)).Success.ShouldBeTrue();
        var firstCompletedAt = await WithDbAsync(db => db.TestInstances.AsNoTracking()
            .Where(x => x.Id == seed.InstanceId).Select(x => x.FirstCompletedAt).SingleAsync());
        firstCompletedAt.ShouldNotBeNull();

        (await RetakeAsync(seed)).AlreadyRetaking.ShouldBeFalse();
        // Yeni denemede bir kontrol (EndTest en az bir kontrol ister, #573); deneme 10 dk önce açılmış sayılır (etkin süre penceresi
        // AttemptStartedAt'ten başlar).
        await WithDbAsync(db => db.TestInstances.Where(x => x.Id == seed.InstanceId)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.CheckCount, 1)
                .SetProperty(x => x.AttemptStartedAt, (DateTime?)DateTime.UtcNow.AddMinutes(-10))));
        (await EndTestAsync(seed, 60)).Success.ShouldBeTrue();

        var events = await ActivityEventsAsync(seed);
        events.Count.ShouldBe(2);
        events.ShouldAllBe(e => e.WorksheetInstanceId == seed.InstanceId && e.UserId == seed.UserId);
        var instance = await WithDbAsync(db => db.TestInstances.AsNoTracking().SingleAsync(x => x.Id == seed.InstanceId));
        instance.Status.ShouldBe(WorksheetInstanceStatus.Completed);
        instance.FirstCompletedAt.ShouldBe(firstCompletedAt);
        instance.ActiveSeconds.ShouldBe(ReportedSeconds + 60);
        instance.RetakeCount.ShouldBe(1);
    }
}
