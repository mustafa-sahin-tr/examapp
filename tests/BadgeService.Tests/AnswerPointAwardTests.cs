using BadgeService;
using BadgeService.Entities;
using BadgeService.Services;
using BadgeService.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BadgeService.Tests;

/// <summary>
/// issue #279, item 4 (owner kararı "soru başına bir kez, son cevap sayılır") + item 6 (QuestionPoint cap)
/// + item 3 (reset/KVKK'de AnswerPointAward/ProcessedAnswerSubmission silinmesi).
/// </summary>
public class AnswerPointAwardTests : IDisposable
{
    private readonly BadgeTestDb _db = BadgeTestDb.Create();

    public void Dispose() => _db.Dispose();

    private static AnswerSubmissionAggregationService NewService(BadgeDbContext ctx, int maxQuestionPoint = 100) =>
        new(ctx, Options.Create(new AnswerPointOptions { MaxQuestionPoint = maxQuestionPoint }));

    private static AnswerSubmittedEvent Answer(
        int userId, int testInstanceId, int questionId, bool correct, int point, DateTime submittedAt,
        Guid? eventId = null, int revision = 0) => new()
    {
        EventId = eventId ?? Guid.Empty,
        UserId = userId,
        TestInstanceId = testInstanceId,
        QuestionId = questionId,
        IsCorrect = correct,
        QuestionPoint = point,
        TimeTakenInSeconds = 10,
        SubjectId = 1,
        Subject = "Matematik",
        SubmittedAt = submittedAt,
        Revision = revision,
    };

    [Fact]
    public async Task Correct_then_wrong_then_correct_for_the_same_question_nets_out_to_the_last_answer()
    {
        var t0 = new DateTime(2026, 9, 1, 10, 0, 0, DateTimeKind.Utc);

        // correct (10) -> wrong (0) -> correct (10): if double-counted this would be 20; must be 10.
        await using (var ctx = _db.NewContext())
            await NewService(ctx).ProcessAsync(Answer(1, 5, 42, correct: true, point: 10, submittedAt: t0));
        await using (var ctx = _db.NewContext())
            await NewService(ctx).ProcessAsync(Answer(1, 5, 42, correct: false, point: 10, submittedAt: t0.AddSeconds(1)));
        await using (var ctx = _db.NewContext())
            await NewService(ctx).ProcessAsync(Answer(1, 5, 42, correct: true, point: 10, submittedAt: t0.AddSeconds(2)));

        await using var check = _db.NewContext();
        var agg = await check.StudentQuestionAggregates.SingleAsync(x => x.UserId == 1);
        agg.TotalPoints.ShouldBe(10);
        // Scope note (issue #279 item 4): only POINTS are deduped per question — each answer is still a
        // separate "attempt" for these counters (unchanged, pre-existing behavior).
        agg.TotalQuestions.ShouldBe(3);
        agg.CorrectQuestions.ShouldBe(2);

        var award = await check.AnswerPointAwards.SingleAsync();
        award.PointsAwarded.ShouldBe(10);
        award.LastAppliedRevisionUtc.ShouldBe(t0.AddSeconds(2));
    }

    [Fact]
    public async Task Wrong_then_correct_for_the_same_question_raises_points_by_the_delta_only()
    {
        var t0 = DateTime.UtcNow;
        await using (var ctx = _db.NewContext())
            await NewService(ctx).ProcessAsync(Answer(1, 5, 42, correct: false, point: 10, submittedAt: t0));
        await using (var ctx = _db.NewContext())
            await NewService(ctx).ProcessAsync(Answer(1, 5, 42, correct: true, point: 10, submittedAt: t0.AddSeconds(1)));

        await using var check = _db.NewContext();
        (await check.StudentQuestionAggregates.SingleAsync()).TotalPoints.ShouldBe(10);
    }

    [Fact]
    public async Task An_older_out_of_order_message_for_the_same_question_is_ignored_entirely()
    {
        var t0 = DateTime.UtcNow;
        // Newer message arrives FIRST (e.g. redelivery reordering), older one arrives second.
        await using (var ctx = _db.NewContext())
            await NewService(ctx).ProcessAsync(Answer(1, 5, 42, correct: true, point: 10, submittedAt: t0.AddSeconds(5)));

        bool secondApplied;
        await using (var ctx = _db.NewContext())
            secondApplied = await NewService(ctx).ProcessAsync(Answer(1, 5, 42, correct: false, point: 10, submittedAt: t0));

        secondApplied.ShouldBeFalse();

        await using var check = _db.NewContext();
        var agg = await check.StudentQuestionAggregates.SingleAsync();
        agg.TotalPoints.ShouldBe(10); // the older, out-of-order message never applied
        agg.TotalQuestions.ShouldBe(1); // the whole aggregate update was skipped for the stale message, not just points
    }

    [Fact]
    public async Task A_duplicate_message_with_the_exact_same_revision_is_a_no_op_even_without_an_EventId()
    {
        var t0 = DateTime.UtcNow;
        var e = Answer(1, 5, 42, correct: true, point: 10, submittedAt: t0);

        bool first, second;
        await using (var ctx = _db.NewContext())
            first = await NewService(ctx).ProcessAsync(e);
        await using (var ctx = _db.NewContext())
            second = await NewService(ctx).ProcessAsync(e);

        first.ShouldBeTrue();
        second.ShouldBeFalse();

        await using var check = _db.NewContext();
        (await check.StudentQuestionAggregates.SingleAsync()).TotalPoints.ShouldBe(10);
    }

    [Fact]
    public async Task Different_questions_for_the_same_user_are_independent_and_additive()
    {
        var t0 = DateTime.UtcNow;
        await using (var ctx = _db.NewContext())
            await NewService(ctx).ProcessAsync(Answer(1, 5, 1, correct: true, point: 10, submittedAt: t0));
        await using (var ctx = _db.NewContext())
            await NewService(ctx).ProcessAsync(Answer(1, 5, 2, correct: true, point: 15, submittedAt: t0.AddSeconds(1)));

        await using var check = _db.NewContext();
        (await check.StudentQuestionAggregates.SingleAsync()).TotalPoints.ShouldBe(25);
        check.AnswerPointAwards.Count().ShouldBe(2);
    }

    [Fact]
    public async Task Same_QuestionId_in_a_different_TestInstance_is_an_independent_question()
    {
        var t0 = DateTime.UtcNow;
        await using (var ctx = _db.NewContext())
            await NewService(ctx).ProcessAsync(Answer(1, testInstanceId: 5, questionId: 42, correct: true, point: 10, submittedAt: t0));
        await using (var ctx = _db.NewContext())
            await NewService(ctx).ProcessAsync(Answer(1, testInstanceId: 6, questionId: 42, correct: true, point: 10, submittedAt: t0.AddSeconds(1)));

        await using var check = _db.NewContext();
        (await check.StudentQuestionAggregates.SingleAsync()).TotalPoints.ShouldBe(20);
    }

    // ---- issue #279 review (blocker): DB-generated int Revision as the primary ordering source ----

    [Fact]
    public async Task Int_revision_ordering_wins_over_SubmittedAt_when_Revision_is_set()
    {
        var t0 = DateTime.UtcNow;
        // Revision=2 applies first; a later-arriving message with Revision=1 but a NEWER SubmittedAt must
        // still be treated as stale — Revision (not the wall clock) is the source of truth once populated.
        await using (var ctx = _db.NewContext())
            await NewService(ctx).ProcessAsync(Answer(1, 5, 42, correct: true, point: 10, t0, revision: 2));

        bool applied;
        await using (var ctx = _db.NewContext())
            applied = await NewService(ctx).ProcessAsync(Answer(1, 5, 42, correct: false, point: 10, t0.AddSeconds(5), revision: 1));

        applied.ShouldBeFalse();
        await using var check = _db.NewContext();
        (await check.StudentQuestionAggregates.SingleAsync()).TotalPoints.ShouldBe(10);
        (await check.AnswerPointAwards.SingleAsync()).LastAppliedRevision.ShouldBe(2);
    }

    [Fact]
    public async Task A_higher_int_revision_applies_even_with_an_earlier_SubmittedAt()
    {
        var t0 = DateTime.UtcNow;
        await using (var ctx = _db.NewContext())
            await NewService(ctx).ProcessAsync(Answer(1, 5, 42, correct: false, point: 10, t0.AddSeconds(5), revision: 1));

        bool applied;
        await using (var ctx = _db.NewContext())
            applied = await NewService(ctx).ProcessAsync(Answer(1, 5, 42, correct: true, point: 10, t0, revision: 2));

        applied.ShouldBeTrue();
        (await _db.NewContext().StudentQuestionAggregates.SingleAsync()).TotalPoints.ShouldBe(10);
    }

    [Fact]
    public async Task Equal_int_revision_redelivery_is_a_no_op()
    {
        var e = Answer(1, 5, 42, correct: true, point: 10, DateTime.UtcNow, revision: 7);

        bool first, second;
        await using (var ctx = _db.NewContext())
            first = await NewService(ctx).ProcessAsync(e);
        await using (var ctx = _db.NewContext())
            second = await NewService(ctx).ProcessAsync(e); // exact redelivery, same Revision

        first.ShouldBeTrue();
        second.ShouldBeFalse();
    }

    [Fact]
    public async Task Sub_microsecond_SubmittedAt_redelivery_is_still_recognised_as_stale_via_the_fallback_path()
    {
        // Revision=0 (legacy fallback): two ticks within the same microsecond truncate to an IDENTICAL
        // normalized timestamp (EventVersion.Normalize truncates to 1µs) — must be treated as the same
        // revision (stale on redelivery), not accidentally "newer".
        var t0 = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddTicks(5); // < 1 tick of a microsecond
        var e1 = Answer(1, 5, 42, correct: true, point: 10, t0);
        var e2 = Answer(1, 5, 42, correct: false, point: 10, t0.AddTicks(4)); // still same µs after truncation

        bool first, second;
        await using (var ctx = _db.NewContext())
            first = await NewService(ctx).ProcessAsync(e1);
        await using (var ctx = _db.NewContext())
            second = await NewService(ctx).ProcessAsync(e2);

        first.ShouldBeTrue();
        second.ShouldBeFalse();
        (await _db.NewContext().StudentQuestionAggregates.SingleAsync()).TotalPoints.ShouldBe(10);
    }

    [Fact]
    public async Task A_future_SubmittedAt_beyond_the_clock_skew_tolerance_is_rejected_when_there_is_no_revision()
    {
        var future = DateTime.UtcNow.AddMinutes(10);

        bool applied;
        await using (var ctx = _db.NewContext())
            applied = await NewService(ctx).ProcessAsync(Answer(1, 5, 42, correct: true, point: 10, future));

        applied.ShouldBeFalse();
        (await _db.NewContext().StudentQuestionAggregates.AnyAsync()).ShouldBeFalse();
        (await _db.NewContext().AnswerPointAwards.AnyAsync()).ShouldBeFalse();
    }

    [Fact]
    public async Task A_SubmittedAt_within_the_five_minute_clock_skew_tolerance_is_accepted()
    {
        var withinTolerance = DateTime.UtcNow.AddMinutes(4);

        bool applied;
        await using (var ctx = _db.NewContext())
            applied = await NewService(ctx).ProcessAsync(Answer(1, 5, 42, correct: true, point: 10, withinTolerance));

        applied.ShouldBeTrue();
    }

    [Fact]
    public async Task User_mismatch_on_an_existing_award_is_rejected_without_any_update()
    {
        var t0 = DateTime.UtcNow;
        // Same (TestInstanceId, QuestionId) but a DIFFERENT UserId than the one already recorded — should
        // never happen legitimately (TestInstanceId is bound to a single student), defense in depth.
        await using (var ctx = _db.NewContext())
            await NewService(ctx).ProcessAsync(Answer(1, 5, 42, correct: true, point: 10, t0, revision: 1));

        bool applied;
        await using (var ctx = _db.NewContext())
            applied = await NewService(ctx).ProcessAsync(Answer(999, 5, 42, correct: true, point: 50, t0.AddSeconds(1), revision: 2));

        applied.ShouldBeFalse();
        await using var check = _db.NewContext();
        (await check.StudentQuestionAggregates.AnyAsync(x => x.UserId == 999)).ShouldBeFalse();
        var award = await check.AnswerPointAwards.SingleAsync();
        award.UserId.ShouldBe(1);
        award.PointsAwarded.ShouldBe(10); // untouched
        award.LastAppliedRevision.ShouldBe(1); // untouched
    }

    // ---- item 6: QuestionPoint cap ----

    [Fact]
    public async Task A_QuestionPoint_above_the_configured_ceiling_is_clamped_not_rejected()
    {
        await using var ctx = _db.NewContext();
        var applied = await NewService(ctx, maxQuestionPoint: 100)
            .ProcessAsync(Answer(1, 5, 42, correct: true, point: 999_999, submittedAt: DateTime.UtcNow));

        applied.ShouldBeTrue();
        (await ctx.StudentQuestionAggregates.SingleAsync()).TotalPoints.ShouldBe(100);
    }

    [Fact]
    public async Task A_QuestionPoint_at_the_ceiling_is_accepted_unclamped()
    {
        await using var ctx = _db.NewContext();
        await NewService(ctx, maxQuestionPoint: 100)
            .ProcessAsync(Answer(1, 5, 42, correct: true, point: 100, submittedAt: DateTime.UtcNow));

        (await ctx.StudentQuestionAggregates.SingleAsync()).TotalPoints.ShouldBe(100);
    }

    [Fact]
    public void Default_ceiling_is_one_hundred()
        => new AnswerPointOptions().MaxQuestionPoint.ShouldBe(100);

    // ---- item 3: reset/KVKK deletes the per-question ledgers ----

    [Fact]
    public async Task Resetting_a_user_deletes_their_AnswerPointAwards_and_ProcessedAnswerSubmissions()
    {
        var eventId = Guid.NewGuid();
        await using (var ctx = _db.NewContext())
            await NewService(ctx).ProcessAsync(Answer(1, 5, 42, correct: true, point: 10, DateTime.UtcNow, eventId));

        await using (var ctx = _db.NewContext())
        {
            (await ctx.AnswerPointAwards.AnyAsync(x => x.UserId == 1)).ShouldBeTrue();
            (await ctx.ProcessedAnswerSubmissions.AnyAsync(x => x.UserId == 1)).ShouldBeTrue();
        }

        await using (var ctx = _db.NewContext())
            await new UserResetService(ctx).ResetAsync(1);

        await using var check = _db.NewContext();
        (await check.AnswerPointAwards.AnyAsync(x => x.UserId == 1)).ShouldBeFalse();
        (await check.ProcessedAnswerSubmissions.AnyAsync(x => x.UserId == 1)).ShouldBeFalse();
    }

    // ---- item 1: concurrency retry (unit-level, provider-independent) ----

    [Fact]
    public async Task ConcurrencyRetry_retries_on_a_concurrency_exception_and_eventually_succeeds()
    {
        var attempts = 0;
        var result = await ConcurrencyRetry.ExecuteAsync(() =>
        {
            attempts++;
            if (attempts < 3)
                throw new DbUpdateConcurrencyException("simulated");
            return Task.FromResult(42);
        });

        result.ShouldBe(42);
        attempts.ShouldBe(3);
    }

    [Fact]
    public async Task ConcurrencyRetry_gives_up_after_the_configured_number_of_attempts()
    {
        var attempts = 0;
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => ConcurrencyRetry.ExecuteAsync<int>(
            () =>
            {
                attempts++;
                throw new DbUpdateConcurrencyException("simulated");
            },
            maxAttempts: 3));

        attempts.ShouldBe(3);
    }

    [Fact]
    public async Task ConcurrencyRetry_invokes_the_onRetry_callback_before_each_retry_but_not_after_the_final_failure()
    {
        var retryCalls = 0;
        await Should.ThrowAsync<DbUpdateConcurrencyException>(() => ConcurrencyRetry.ExecuteAsync<int>(
            () => throw new DbUpdateConcurrencyException("simulated"),
            onRetry: (_, _) => retryCalls++,
            maxAttempts: 3));

        retryCalls.ShouldBe(2); // called before attempts 2 and 3, not after the 3rd (final) failure
    }

    [Fact]
    public async Task ConcurrencyRetry_does_not_retry_other_exception_types()
    {
        var attempts = 0;
        await Should.ThrowAsync<InvalidOperationException>(() => ConcurrencyRetry.ExecuteAsync<int>(() =>
        {
            attempts++;
            throw new InvalidOperationException("not a concurrency conflict");
        }));

        attempts.ShouldBe(1);
    }
}
