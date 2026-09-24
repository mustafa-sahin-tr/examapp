using System;

namespace BadgeService.Entities;

/// <summary>
/// Idempotency ledger for <c>AnswerSubmittedConsumer</c> (issue #243). Redelivery/duplicate-publish
/// detection is keyed on the producer-assigned <see cref="EventId"/> (matches the outbox row's
/// <c>OutboxMessage.Id</c>, same pattern as <c>ProcessedLoginAttempt</c>/<c>LoginAttemptedEvent</c>)
/// — not on (user, question, timestamp), since a resubmitted answer for the same question
/// legitimately produces a new event and must NOT be deduped away (see
/// <see cref="AnswerSubmittedConsumer"/> XML doc for that separate, out-of-scope case).
///
/// This row is written in the SAME <c>SaveChanges</c> call as the aggregate update
/// (<c>StudentQuestionAggregate.TotalPoints</c> and the <c>StudentPointsChangedEvent</c> outbox row)
/// — so a redelivered message either applies both or neither, never one without the other.
/// <see cref="EventId"/> is the primary key: a concurrent duplicate delivery hits the PK/unique
/// constraint and <c>SaveChanges</c> throws a 23505, which the caller treats as a no-op (the
/// badge/streak evaluation pass is skipped too — see <c>AnswerSubmittedConsumer</c>).
///
/// <see cref="Guid.Empty"/> (legacy producers, or messages already in-flight before this change)
/// bypasses dedup entirely: no row is written and duplicate delivery re-applies the aggregate
/// update, identical to the pre-#243 behavior.
/// </summary>
public class ProcessedAnswerSubmission
{
    public Guid EventId { get; set; }

    public int UserId { get; set; }

    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
