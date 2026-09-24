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
/// NOT covered by EventId dedup (separate, pre-existing product concern, tracked in #243 as
/// out-of-scope): a student re-submitting an answer for the SAME question (answer change) produces a
/// brand-new outbox row / EventId each time <c>TestSessionService.SaveAnswer</c> runs, so each such
/// call is — correctly, per current product behavior — counted again.
///
/// Error path (unchanged by #243, see <see cref="AnswerSubmittedConsumerDefinition"/>): no retry is
/// configured — an unhandled exception is not swallowed, it propagates and MassTransit moves the
/// message straight to the <c>badge-service_error</c> (dead-letter) queue for investigation. Now that
/// this consumer is idempotent for events carrying a non-empty EventId, adding bounded retry would be
/// safe for those messages, but is left out of #243's scope since events without an EventId (legacy
/// producers) would still double-count on retry.
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
