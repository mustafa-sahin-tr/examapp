using BadgeService;
using BadgeService.Consumers;
using BadgeService.Entities;
using BadgeService.Hubs;
using BadgeService.Services;
using BadgeService.Tests.Support;
using ExamApp.Foundation.Contracts;
using MassTransit;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace BadgeService.Tests;

/// <summary>
/// Issue #243: <see cref="AnswerSubmittedEvent.EventId"/> (= producer outbox row Id) based dedup in
/// <see cref="AnswerSubmissionAggregationService"/>. See that class's XML doc and
/// <see cref="ProcessedAnswerSubmission"/>'s XML doc for the write path.
/// </summary>
public class AnswerSubmittedIdempotencyTests : IDisposable
{
    private readonly BadgeTestDb _db = BadgeTestDb.Create();

    private AnswerSubmissionAggregationService NewService(BadgeDbContext ctx) => new(ctx);

    // issue #279 item 4: her Answer() çağrısı (aksi belirtilmedikçe) kendi benzersiz QuestionId'sini alır —
    // bu dosyanın testleri EventId dedup'ını hedefler, (TestInstanceId, QuestionId) revizyon katmanının
    // araya girmemesi gerekir.
    private static int _questionSeq;

    private static AnswerSubmittedEvent Answer(
        Guid eventId, int userId = 7, bool correct = true, int point = 10, int? questionId = null) => new()
    {
        EventId = eventId,
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

    public void Dispose() => _db.Dispose();

    /// <summary>
    /// Builds a simulated 23505 carrying a specific <c>ConstraintName</c> — the property has no public
    /// setter, so the full constructor overload is used (matches AuthApi.Tests' technique, extended with
    /// the constraint name <see cref="AnswerSubmissionAggregationService.ProcessAsync"/>'s
    /// <c>IsUniqueViolation</c> check now requires, review fix issue #243).
    /// </summary>
    private static Npgsql.PostgresException UniqueViolation(string constraintName) => new(
        messageText: "simulated", severity: "ERROR", invariantSeverity: "ERROR", sqlState: "23505",
        detail: null, hint: null, position: 0, internalPosition: 0, internalQuery: null, where: null,
        schemaName: null, tableName: null, columnName: null, dataTypeName: null,
        constraintName: constraintName, file: null, line: null, routine: null);

    [Fact]
    public async Task First_delivery_of_an_event_with_a_real_EventId_is_applied_and_ledgered()
    {
        var eventId = Guid.NewGuid();

        bool applied;
        await using (var ctx = _db.NewContext())
        {
            applied = await NewService(ctx).ProcessAsync(Answer(eventId));
        }

        applied.ShouldBeTrue();

        await using var check = _db.NewContext();
        (await check.StudentQuestionAggregates.SingleAsync()).TotalPoints.ShouldBe(10);
        var ledger = await check.ProcessedAnswerSubmissions.SingleAsync();
        ledger.EventId.ShouldBe(eventId);
        ledger.UserId.ShouldBe(7);
    }

    [Fact]
    public async Task Same_EventId_delivered_twice_only_increases_points_once()
    {
        var eventId = Guid.NewGuid();

        bool firstApplied, secondApplied;
        await using (var ctx = _db.NewContext())
            firstApplied = await NewService(ctx).ProcessAsync(Answer(eventId));
        await using (var ctx = _db.NewContext())
            secondApplied = await NewService(ctx).ProcessAsync(Answer(eventId)); // simulated redelivery

        firstApplied.ShouldBeTrue();
        secondApplied.ShouldBeFalse();

        await using var check = _db.NewContext();
        (await check.StudentQuestionAggregates.SingleAsync()).TotalPoints.ShouldBe(10);
        (await check.StudentQuestionAggregates.SingleAsync()).TotalQuestions.ShouldBe(1);
        check.ProcessedAnswerSubmissions.Count().ShouldBe(1);
        // Only one StudentPointsChangedEvent outbox row — redelivery did not re-emit the leaderboard event.
        check.OutboxMessages.Count().ShouldBe(1);
    }

    [Fact]
    public async Task A_different_EventId_for_the_same_user_is_independent_and_still_applies()
    {
        await using (var ctx = _db.NewContext())
            await NewService(ctx).ProcessAsync(Answer(Guid.NewGuid(), point: 10));
        await using (var ctx = _db.NewContext())
            await NewService(ctx).ProcessAsync(Answer(Guid.NewGuid(), point: 15));

        await using var check = _db.NewContext();
        (await check.StudentQuestionAggregates.SingleAsync()).TotalPoints.ShouldBe(25);
        check.ProcessedAnswerSubmissions.Count().ShouldBe(2);
    }

    [Fact]
    public async Task Empty_EventId_bypasses_the_EventId_ledger_but_is_still_protected_by_the_revision_layer()
    {
        // Guid.Empty simulates a producer/message that predates issue #243 — the EventId ledger
        // (ProcessedAnswerSubmission) is skipped entirely (no row written). Before issue #279 this meant a
        // raw redelivery of the exact same message re-applied points (legacy at-least-once, double-count
        // bug). issue #279 item 4/5 closes this gap with a SECOND, EventId-independent layer: the
        // (TestInstanceId, QuestionId) revision (AnswerPointAward, keyed on SubmittedAt) — a redelivery
        // carries the SAME SubmittedAt, so it is now recognised as stale and skipped even without an EventId.
        var e = Answer(Guid.Empty, point: 10);

        bool firstApplied, secondApplied;
        await using (var ctx = _db.NewContext())
            firstApplied = await NewService(ctx).ProcessAsync(e);
        await using (var ctx = _db.NewContext())
            secondApplied = await NewService(ctx).ProcessAsync(e); // raw redelivery: identical SubmittedAt

        firstApplied.ShouldBeTrue();
        secondApplied.ShouldBeFalse(); // issue #279: no longer double-counted

        await using var check = _db.NewContext();
        (await check.StudentQuestionAggregates.SingleAsync()).TotalPoints.ShouldBe(10);
        check.ProcessedAnswerSubmissions.Count().ShouldBe(0); // still no EventId ledger row (EventId empty)
    }

    [Fact]
    public async Task Empty_EventId_with_a_genuinely_later_revision_still_applies_as_a_new_answer()
    {
        // A real answer CHANGE (not a redelivery) for the same question — later SubmittedAt — must still
        // go through, delta-applied against the previously awarded points (issue #279 item 4).
        var first = Answer(Guid.Empty, correct: true, point: 10, questionId: 900);
        var second = Answer(Guid.Empty, correct: true, point: 15, questionId: 900);
        second.TestInstanceId = first.TestInstanceId;
        second.SubmittedAt = first.SubmittedAt.AddSeconds(1);

        await using (var ctx = _db.NewContext())
            (await NewService(ctx).ProcessAsync(first)).ShouldBeTrue();
        await using (var ctx = _db.NewContext())
            (await NewService(ctx).ProcessAsync(second)).ShouldBeTrue();

        await using var check = _db.NewContext();
        // Not 25 (additive) — same question, so only the LAST answer's points count.
        (await check.StudentQuestionAggregates.SingleAsync()).TotalPoints.ShouldBe(15);
    }

    private static ConsumeContext<AnswerSubmittedEvent> Context(AnswerSubmittedEvent message)
    {
        var ctx = Substitute.For<ConsumeContext<AnswerSubmittedEvent>>();
        ctx.Message.Returns(message);
        ctx.CancellationToken.Returns(CancellationToken.None);
        return ctx;
    }

    [Fact]
    public async Task Consumer_evaluates_badges_on_every_delivery_but_a_duplicate_does_not_double_earn_or_double_push()
    {
        // AnswerCount(target=1) badge — first delivery earns it and pushes over SignalR. A redelivery of
        // the SAME EventId still RUNS evaluation (review fix, issue #243 — see AnswerSubmittedConsumer's
        // XML doc: evaluator is idempotent, so this is safe and avoids losing a badge if a prior
        // delivery's evaluation step failed after the aggregate had already committed) — but because the
        // aggregate is unchanged and the badge is already in BadgeEarned, no second row/push happens.
        await using (var seed = _db.NewContext())
        {
            seed.BadgeDefinitions.Add(new BadgeDefinition
            {
                Id = Guid.NewGuid(), Name = "İlk Adım", Description = "İlk soruyu çöz",
                Category = "General", RuleType = "AnswerCount", RuleConfigJson = "{\"target\":1}",
            });
            await seed.SaveChangesAsync();
        }

        var hub = Substitute.For<IHubContext<BadgeNotificationHub>>();
        hub.Clients.User(Arg.Any<string>()).SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var eventId = Guid.NewGuid();
        var e = Answer(eventId);
        e.ClientId = "kc-7";

        await using (var ctx = _db.NewContext())
            await new AnswerSubmittedConsumer(new AnswerSubmissionAggregationService(ctx), new BadgeEvaluator(ctx, hub))
                .Consume(Context(e));
        await using (var ctx = _db.NewContext())
            await new AnswerSubmittedConsumer(new AnswerSubmissionAggregationService(ctx), new BadgeEvaluator(ctx, hub))
                .Consume(Context(e)); // redelivery, same EventId

        await using var check = _db.NewContext();
        check.BadgeEarned.Count().ShouldBe(1);
        await hub.Clients.User("kc-7").Received(1).SendCoreAsync(
            "BadgeEarned", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task A_redelivery_still_earns_a_badge_the_first_deliverys_evaluation_step_missed()
    {
        // Regression guard for the bug the review fix removes: the aggregate commits (ProcessAsync
        // succeeds) but the evaluation step never got to run on the first delivery (e.g. it threw before
        // reaching BadgeEvaluator's own SaveChanges — simulated here by simply not calling the evaluator
        // at all on the "first delivery"). Before the fix, the consumer skipped evaluation on ANY
        // duplicate-flagged redelivery, so this badge would never be earned. After the fix, the evaluator
        // runs unconditionally and recovers it.
        await using (var seed = _db.NewContext())
        {
            seed.BadgeDefinitions.Add(new BadgeDefinition
            {
                Id = Guid.NewGuid(), Name = "İlk Adım", Description = "İlk soruyu çöz",
                Category = "General", RuleType = "AnswerCount", RuleConfigJson = "{\"target\":1}",
            });
            await seed.SaveChangesAsync();
        }

        var hub = Substitute.For<IHubContext<BadgeNotificationHub>>();
        hub.Clients.User(Arg.Any<string>()).SendCoreAsync(
            Arg.Any<string>(), Arg.Any<object?[]>(), Arg.Any<CancellationToken>()).Returns(Task.CompletedTask);

        var eventId = Guid.NewGuid();
        var e = Answer(eventId);
        e.ClientId = "kc-7";

        // "First delivery": only the aggregation step runs (as if the evaluator had thrown right after).
        await using (var ctx = _db.NewContext())
            await new AnswerSubmissionAggregationService(ctx).ProcessAsync(e);

        await using (var check1 = _db.NewContext())
            (await check1.BadgeEarned.AnyAsync()).ShouldBeFalse(); // not earned yet — evaluator never ran

        // "Redelivery" of the SAME EventId: ProcessAsync reports it as already processed (duplicate),
        // but the consumer must still run the evaluator.
        await using (var ctx = _db.NewContext())
            await new AnswerSubmittedConsumer(new AnswerSubmissionAggregationService(ctx), new BadgeEvaluator(ctx, hub))
                .Consume(Context(e));

        await using var check2 = _db.NewContext();
        check2.BadgeEarned.Count().ShouldBe(1); // recovered, not lost
        await hub.Clients.User("kc-7").Received(1).SendCoreAsync(
            "BadgeEarned", Arg.Any<object?[]>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// Simulates a lost race with a concurrent redelivery (issue #243): by the time THIS delivery's
    /// SaveChanges runs, another delivery of the same EventId has already committed the ledger row, so
    /// the insert collides on the EventId PK/unique constraint. SQLite doesn't reproduce Postgres's
    /// SqlState, so the interceptor throws the same exception shape <c>IsUniqueViolation</c> looks for
    /// (same technique as AuthApi.Tests' <c>FailUserInsertInterceptor</c>) — this isolates "does the
    /// caller correctly interpret a 23505 as duplicate/no-op" from needing genuine thread concurrency.
    /// </summary>
    private sealed class UniqueViolationOnLedgerInsertInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<ProcessedAnswerSubmission>().Any(e => e.State == EntityState.Added))
            {
                throw new DbUpdateException("simulated concurrent duplicate ledger insert",
                    UniqueViolation("PK_ProcessedAnswerSubmissions"));
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    /// <summary>
    /// Review fix (issue #243): a 23505 on a DIFFERENT constraint — e.g. a genuinely concurrent, DIFFERENT
    /// answer for the same user racing on <c>IX_StudentQuestionAggregates_UserId</c> — must NOT be treated
    /// as a duplicate-delivery no-op. <see cref="AnswerSubmissionAggregationService.ProcessAsync"/> must
    /// let it propagate (retry/dead-letter), not silently drop that student's points.
    /// </summary>
    private sealed class UniqueViolationOnAggregateInsertInterceptor : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (eventData.Context!.ChangeTracker.Entries<StudentQuestionAggregate>().Any(e => e.State == EntityState.Added))
            {
                throw new DbUpdateException("simulated concurrent aggregate race (NOT the ledger)",
                    UniqueViolation("IX_StudentQuestionAggregates_UserId"));
            }
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }
    }

    [Fact]
    public async Task A_unique_violation_on_the_aggregate_index_is_not_swallowed_as_a_duplicate()
    {
        var path = Path.Combine(Path.GetTempPath(), $"badge-dedupe-nonledger-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path}";
        try
        {
            using (var setup = new BadgeDbContext(new DbContextOptionsBuilder<BadgeDbContext>().UseSqlite(connectionString).Options))
                setup.Database.EnsureCreated();

            var options = new DbContextOptionsBuilder<BadgeDbContext>()
                .UseSqlite(connectionString)
                .AddInterceptors(new UniqueViolationOnAggregateInsertInterceptor())
                .Options;

            await using var ctx = new BadgeDbContext(options);
            await Should.ThrowAsync<DbUpdateException>(
                () => NewService(ctx).ProcessAsync(Answer(Guid.NewGuid(), point: 10)));
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { /* temp dosyası; en kötü OS temizler */ }
        }
    }

    [Fact]
    public async Task Concurrent_duplicate_delivery_hitting_the_unique_constraint_is_a_no_op()
    {
        var path = Path.Combine(Path.GetTempPath(), $"badge-dedupe-{Guid.NewGuid():N}.db");
        var connectionString = $"Data Source={path}";
        try
        {
            using (var setup = new BadgeDbContext(new DbContextOptionsBuilder<BadgeDbContext>().UseSqlite(connectionString).Options))
                setup.Database.EnsureCreated();

            var options = new DbContextOptionsBuilder<BadgeDbContext>()
                .UseSqlite(connectionString)
                .AddInterceptors(new UniqueViolationOnLedgerInsertInterceptor())
                .Options;

            bool applied;
            await using (var ctx = new BadgeDbContext(options))
                applied = await NewService(ctx).ProcessAsync(Answer(Guid.NewGuid(), point: 10));

            applied.ShouldBeFalse();

            await using var check = new BadgeDbContext(new DbContextOptionsBuilder<BadgeDbContext>().UseSqlite(connectionString).Options);
            // The whole SaveChanges (aggregate update + ledger insert, same transaction) rolled back —
            // no partial write: the aggregate was never persisted, badge/streak evaluation must be
            // skipped too (verified separately at the consumer level).
            (await check.StudentQuestionAggregates.AnyAsync()).ShouldBeFalse();
            (await check.ProcessedAnswerSubmissions.AnyAsync()).ShouldBeFalse();
            (await check.OutboxMessages.AnyAsync()).ShouldBeFalse();
        }
        finally
        {
            try { File.Delete(path); } catch (IOException) { /* temp dosyası; en kötü OS temizler */ }
        }
    }
}
