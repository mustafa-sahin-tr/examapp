using System.Text.Json;
using BadgeService;
using BadgeService.Commands;
using BadgeService.Controllers;
using BadgeService.Entities;
using BadgeService.Services;
using BadgeService.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace BadgeService.Tests;

/// <summary>
/// issue #225: BadgeService puan değişiminde kendi outbox'ına MUTLAK değer taşıyan
/// <see cref="StudentPointsChangedEvent"/> yazar (aggregate ile aynı SaveChanges).
/// </summary>
public class StudentPointsOutboxTests : IDisposable
{
    private readonly BadgeTestDb _db = BadgeTestDb.Create();

    public void Dispose() => _db.Dispose();

    // issue #279 item 4: her çağrı, aksi belirtilmedikçe kendi benzersiz QuestionId'sini alır — bu dosyanın
    // testleri "her Answer() bağımsız bir soru" varsayımını korur (önceki toplamsal davranış).
    private static int _questionSeq;

    private static AnswerSubmittedEvent Answer(int userId = 7, bool correct = true, int point = 10, int? questionId = null) => new()
    {
        UserId = userId,
        IsCorrect = correct,
        QuestionPoint = point,
        TimeTakenInSeconds = 5,
        SubjectId = 1,
        Subject = "Matematik",
        SubmittedAt = DateTime.UtcNow,
        TestInstanceId = 1,
        QuestionId = questionId ?? System.Threading.Interlocked.Increment(ref _questionSeq),
    };

    private async Task ProcessAsync(params AnswerSubmittedEvent[] events)
    {
        foreach (var e in events)
        {
            await using var ctx = _db.NewContext();
            await new AnswerSubmissionAggregationService(ctx).ProcessAsync(e);
        }
    }

    private async Task<List<StudentPointsChangedEvent>> OutboxEventsAsync()
    {
        await using var ctx = _db.NewContext();
        var rows = await ctx.OutboxMessages.OrderBy(m => m.CreatedAt).ToListAsync();
        rows.ShouldAllBe(m => m.Type == OutboxEventRegistry.NameFor<StudentPointsChangedEvent>());
        rows.ShouldAllBe(m => m.ProcessedAt == null);
        return rows.Select(m => JsonSerializer.Deserialize<StudentPointsChangedEvent>(m.Content)!).ToList();
    }

    [Fact]
    public async Task Correct_answer_writes_the_absolute_total_not_the_delta()
    {
        await ProcessAsync(Answer(point: 10), Answer(point: 15));

        var events = await OutboxEventsAsync();
        events.Select(e => e.TotalPoints).ShouldBe(new[] { 10, 25 });
        events.ShouldAllBe(e => e.UserId == 7);
        events[1].UpdatedAtUtc.ShouldBeGreaterThanOrEqualTo(events[0].UpdatedAtUtc);
        events.ShouldAllBe(e => e.UpdatedAtUtc.Kind == DateTimeKind.Utc);

        await using var ctx = _db.NewContext();
        (await ctx.StudentQuestionAggregates.SingleAsync()).TotalPoints.ShouldBe(25);
    }

    [Fact]
    public async Task Wrong_answer_does_not_change_points_and_writes_no_event()
    {
        await ProcessAsync(Answer(correct: false), Answer(correct: true, point: 0));

        (await OutboxEventsAsync()).ShouldBeEmpty();
    }

    [Fact]
    public async Task Event_version_is_the_aggregate_timestamp_truncated_to_microseconds()
    {
        await ProcessAsync(Answer());

        var evt = (await OutboxEventsAsync()).Single();
        (evt.UpdatedAtUtc.Ticks % 10).ShouldBe(0);

        await using var ctx = _db.NewContext();
        var agg = await ctx.StudentQuestionAggregates.SingleAsync();
        evt.UpdatedAtUtc.ShouldBe(EventVersion.Normalize(agg.LastUpdatedUtc));
    }

    [Fact]
    public async Task Reset_writes_a_zero_points_event_in_the_same_save()
    {
        await ProcessAsync(Answer(point: 40));

        await using (var ctx = _db.NewContext())
        {
            var result = await new ResetController(new UserResetService(ctx)).ResetUserAsync(7, CancellationToken.None);
            result.ShouldBeOfType<Microsoft.AspNetCore.Mvc.OkObjectResult>();
        }

        var events = await OutboxEventsAsync();
        events.Select(e => e.TotalPoints).ShouldBe(new[] { 40, 0 });
        events[1].UpdatedAtUtc.ShouldBeGreaterThanOrEqualTo(events[0].UpdatedAtUtc);

        await using var check = _db.NewContext();
        (await check.StudentQuestionAggregates.AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Reset_without_a_points_aggregate_writes_no_event()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.StudentDailyActivities.Add(new StudentDailyActivity
            {
                Id = Guid.NewGuid(), UserId = 8, ActivityDate = DateTime.UtcNow.Date, LastUpdatedUtc = DateTime.UtcNow,
            });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
        {
            (await new UserResetService(ctx).ResetAsync(8)).ShouldBeFalse();
        }

        (await OutboxEventsAsync()).ShouldBeEmpty();
        await using var check = _db.NewContext();
        (await check.StudentDailyActivities.AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task Aggregate_timestamps_are_stamped_together_after_the_reads()
    {
        await ProcessAsync(Answer());

        await using var ctx = _db.NewContext();
        var q = await ctx.StudentQuestionAggregates.SingleAsync();
        (await ctx.StudentSubjectAggregates.SingleAsync()).LastUpdatedUtc.ShouldBe(q.LastUpdatedUtc);
        (await ctx.StudentDailyActivities.SingleAsync()).LastUpdatedUtc.ShouldBe(q.LastUpdatedUtc);
    }

    [Fact]
    public async Task Backfill_writes_one_event_per_aggregate_using_the_aggregate_timestamp()
    {
        var t1 = new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc);
        var t2 = new DateTime(2026, 9, 2, 9, 30, 0, DateTimeKind.Utc);
        await using (var seed = _db.NewContext())
        {
            seed.StudentQuestionAggregates.AddRange(
                new StudentQuestionAggregate { Id = Guid.NewGuid(), UserId = 2, TotalPoints = 120, LastUpdatedUtc = t1 },
                new StudentQuestionAggregate { Id = Guid.NewGuid(), UserId = 1, TotalPoints = 30, LastUpdatedUtc = t2 });
            await seed.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
        {
            var count = await StudentPointsBackfillCommand.RunAsync(ctx, NullLogger.Instance, dryRun: false);
            count.ShouldBe(2);
        }

        var events = (await OutboxEventsAsync()).OrderBy(e => e.UserId).ToList();
        events.Count.ShouldBe(2);
        events[0].ShouldSatisfyAllConditions(
            e => e.UserId.ShouldBe(1), e => e.TotalPoints.ShouldBe(30), e => e.UpdatedAtUtc.ShouldBe(t2));
        events[1].ShouldSatisfyAllConditions(
            e => e.UserId.ShouldBe(2), e => e.TotalPoints.ShouldBe(120), e => e.UpdatedAtUtc.ShouldBe(t1));
    }

    [Fact]
    public async Task Backfill_dry_run_writes_nothing()
    {
        await ProcessAsync(Answer());
        await using (var clear = _db.NewContext())
        {
            clear.OutboxMessages.RemoveRange(clear.OutboxMessages);
            await clear.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
        {
            (await StudentPointsBackfillCommand.RunAsync(ctx, NullLogger.Instance, dryRun: true)).ShouldBe(1);
        }

        (await OutboxEventsAsync()).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(new[] { "backfill-student-points" }, true, false)]
    [InlineData(new[] { "backfill-student-points", "--dry-run" }, true, true)]
    [InlineData(new[] { "--urls=http://+:8006" }, false, false)]
    [InlineData(new string[0], false, false)]
    public void Command_parsing(string[] args, bool requested, bool dryRun)
    {
        StudentPointsBackfillCommand.IsRequested(args).ShouldBe(requested);
        if (requested)
            StudentPointsBackfillCommand.ParseArgs(args).DryRun.ShouldBe(dryRun);
    }

    [Fact]
    public void Command_rejects_unknown_arguments()
        => Should.Throw<ArgumentException>(() =>
            StudentPointsBackfillCommand.ParseArgs(new[] { "backfill-student-points", "--force" }));

    // Flag/ortam matrisi ve dry-run testleri: StudentPointsBackfillCommandFlagTests.cs (issue #243).

    [Fact]
    public async Task Answer_submitted_consumer_chain_produces_a_row_the_outbox_publisher_can_publish()
    {
        // "Soru çözülünce" zinciri, BadgeService tarafı: AnswerSubmittedConsumer (aggregation + badge
        // değerlendirme) → outbox satırı → OutboxPublisher'ın çözüm yolu (Resolve + Deserialize(content, type)).
        var hub = Substitute.For<Microsoft.AspNetCore.SignalR.IHubContext<BadgeService.Hubs.BadgeNotificationHub>>();
        await using (var ctx = _db.NewContext())
        {
            var consumer = new BadgeService.Consumers.AnswerSubmittedConsumer(
                new AnswerSubmissionAggregationService(ctx), new BadgeEvaluator(ctx, hub));
            var context = Substitute.For<MassTransit.ConsumeContext<AnswerSubmittedEvent>>();
            context.Message.Returns(Answer(userId: 11, point: 35));
            await consumer.Consume(context);
        }

        await using var check = _db.NewContext();
        var row = await check.OutboxMessages.SingleAsync();
        var type = OutboxEventRegistry.Resolve(row.Type);
        type.ShouldBe(typeof(StudentPointsChangedEvent));
        var evt = (StudentPointsChangedEvent)JsonSerializer.Deserialize(row.Content, type!)!;
        evt.UserId.ShouldBe(11);
        evt.TotalPoints.ShouldBe(35);
        row.Content.ShouldNotContain("kc", Case.Sensitive); // payload: yalnızca id + puan + zaman (Keycloak sub vb. yok)
    }

    [Fact]
    public void StudentPointsChangedEvent_is_publishable_by_the_outbox_publisher()
        => OutboxEventRegistry.Resolve(OutboxEventRegistry.NameFor<StudentPointsChangedEvent>())
            .ShouldBe(typeof(StudentPointsChangedEvent));
}
