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
    public string ClientId { get; set; } = string.Empty;
    public int? SelectedAnswerId { get; set; }
    public bool IsCorrect { get; set; }
    public int QuestionPoint { get; set; }
    public int DifficultyLevel { get; set; }
    public int TimeTakenInSeconds { get; set; }
    public DateTime SubmittedAt { get; set; }
}

