using System;

namespace ExamApp.Foundation.Contracts;

public class AnswerSubmittedEvent
{
    /// <summary>
    /// Producer-assigned correlation id (issue #243) — matches the outbox row's
    /// <c>OutboxMessage.Id</c>, same pattern as <see cref="LoginAttemptedEvent.EventId"/>.
    /// Consumers dedupe on this. Defaults to <see cref="Guid.Empty"/> so that older producers
    /// (or messages already in-flight before this change) keep the pre-existing at-least-once
    /// behavior — dedup is skipped entirely when this is empty, never treated as a real id.
    /// </summary>
    public Guid EventId { get; set; } = Guid.Empty;

    public int UserId { get; set; }
    public int QuestionId { get; set; }
    public int? SubjectId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public int? TopicId { get; set; }
    public int? SubTopicId { get; set; }
    public int TestInstanceId { get; set; }

    /// <summary>
    /// issue #279 review: <c>WorksheetInstanceQuestion.Id</c> (the row's own PK, cheap to carry since the
    /// producer already has it loaded). Not currently used for dedup/consumer logic (that stays keyed on
    /// TestInstanceId+QuestionId, which already identifies the row uniquely for a given student) — carried
    /// for future diagnostics/correlation without another DB round trip.
    /// </summary>
    public int TestInstanceQuestionId { get; set; }

    public string ClientId { get; set; } = string.Empty;
    public int? SelectedAnswerId { get; set; }
    public bool IsCorrect { get; set; }
    public int QuestionPoint { get; set; }
    public int DifficultyLevel { get; set; }
    public int TimeTakenInSeconds { get; set; }
    public DateTime SubmittedAt { get; set; }

    /// <summary>
    /// issue #279 review (blocker): DB-generated monotonic revision of the answer row
    /// (<c>WorksheetInstanceQuestion.AnswerRevision</c>), atomically incremented in
    /// <c>TestSessionService.SaveAnswer</c>. This is the PRIMARY signal <c>AnswerPointAward</c> uses to
    /// order/dedupe answer changes — unlike <see cref="SubmittedAt"/> (wall-clock, sub-microsecond
    /// collisions possible on redelivery, and only as precise as the producer's clock), this is a DB
    /// sequence with no two SaveAnswer calls for the same row ever producing the same value.
    /// Additive/backward-compatible: defaults to 0, which the consumer treats as "no DB revision available"
    /// (old in-flight messages predating this field) and falls back to <see cref="SubmittedAt"/> — see
    /// <c>AnswerSubmissionAggregationService</c>'s XML doc for the exact fallback rule.
    /// </summary>
    public int Revision { get; set; }
}

