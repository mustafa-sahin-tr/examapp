using MassTransit;
using ExamApp.Foundation.Contracts;
using BadgeService.Services;
namespace BadgeService.Consumers;

/// <summary>
/// Idempotency (issue #243): dedup is keyed on the producer-assigned
/// <see cref="AnswerSubmittedEvent.EventId"/> (matches the outbox row's <c>OutboxMessage.Id</c>,
/// same pattern as <c>LoginAttemptedConsumer</c>) via
/// <see cref="AnswerSubmissionAggregationService.ProcessAsync"/> — see that method's XML doc for the
/// write path (same-transaction ledger row + constraint-specific PK-violation-as-no-op).
///
/// Review fix (issue #243): the badge/streak evaluation pass runs UNCONDITIONALLY, even when
/// <c>ProcessAsync</c> reports a duplicate. Skipping it on duplicate used to be a correctness bug: if
/// the FIRST delivery's aggregate commit succeeded but the evaluator step afterwards threw (e.g.
/// transient failure), the whole message would be redelivered to the dead-letter/retry path — but by
/// then <c>ProcessAsync</c> would report the event as already processed (duplicate) and evaluation
/// would never run again, silently losing that badge forever. Running the evaluator every time is
/// safe because it is itself idempotent: <see cref="BadgeEvaluator.EvaluateAnswerSubmittedAsync"/> only
/// adds a <c>BadgeEarned</c> row / pushes the SignalR notification for a badge NOT already in
/// <c>earnedBadgeIds</c> — re-running it against unchanged aggregates (duplicate case) or the same
/// aggregates twice (retry-after-partial-failure case) produces no new badge and no second push.
///
/// RESOLVED by issue #279 (item 4, owner decision "soru başına bir kez, son cevap sayılır"): the
/// double-points gap above is closed by a SECOND, EventId-independent idempotency layer in
/// <see cref="AnswerSubmissionAggregationService"/> — points are tracked per (TestInstanceId, QuestionId)
/// in <c>AnswerPointAward</c>, keyed on the answer's own revision (<c>SubmittedAt</c>). A re-submission for
/// the same question still counts as a new "attempt" (TotalQuestions/streak/etc.), but only the DELTA
/// between the previous and new awarded points is applied — so correct→wrong→correct nets out to the
/// last answer's points instead of accumulating. See that class's XML doc for the full design.
///
/// Error path (issue #279, item 5 — CHANGED from #243): <see cref="AnswerSubmittedConsumerDefinition"/> now
/// configures bounded retry (1s/5s/15s, 3 attempts). This is now safe for ALL messages, including
/// <see cref="Guid.Empty"/> EventId ones, because the revision-based layer above dedupes independently of
/// EventId. Exhausted retries still dead-letter to <c>badge-service_error</c>.
/// </summary>
public class AnswerSubmittedConsumer : IConsumer<AnswerSubmittedEvent>
{
    private readonly AnswerSubmissionAggregationService _aggregationService;
    private readonly BadgeEvaluator _evaluator;

    public AnswerSubmittedConsumer(AnswerSubmissionAggregationService aggregationService, BadgeEvaluator evaluator)
    {
        _aggregationService = aggregationService;
        _evaluator = evaluator;
    }

    public async Task Consume(ConsumeContext<AnswerSubmittedEvent> context)
    {
        var message = context.Message;

        await _aggregationService.ProcessAsync(message, context.CancellationToken);

        // Always runs, including on a duplicate delivery — see the class XML doc for why this is both
        // safe (BadgeEvaluator is itself idempotent) and necessary (avoids permanently losing a badge
        // if a prior delivery's evaluation step failed after the aggregate had already committed).
        await _evaluator.EvaluateAnswerSubmittedAsync(message.UserId, message.ClientId, context.CancellationToken);
    }
}
