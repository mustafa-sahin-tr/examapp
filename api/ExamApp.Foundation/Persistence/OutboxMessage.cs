using System;

namespace ExamApp.Foundation.Persistence;

public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>
    /// Logical event type name — <see cref="Type.FullName"/> of the contract
    /// (namespace + class, no assembly). Resolved via <c>OutboxEventRegistry</c>
    /// so publishing survives assembly/version renames.
    /// </summary>
    public string Type { get; set; } = null!;

    public string Content { get; set; } = null!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; set; }

    /// <summary>Number of failed publish attempts so far.</summary>
    public int RetryCount { get; set; }

    /// <summary>Earliest time the row may be retried (exponential backoff). Null = eligible now.</summary>
    public DateTime? NextAttemptAt { get; set; }

    /// <summary>Last failure message. Set while retrying and when the row is dead-lettered.</summary>
    public string? Error { get; set; }
}
