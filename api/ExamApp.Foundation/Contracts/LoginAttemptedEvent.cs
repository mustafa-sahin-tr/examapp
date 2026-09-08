using System;

namespace ExamApp.Foundation.Contracts;

public class LoginAttemptedEvent
{
    /// <summary>
    /// Producer-assigned correlation id (matches the outbox row's <c>OutboxMessage.Id</c>).
    /// Consumers must dedupe on this, not on the (user, timestamp, success) tuple — two
    /// distinct attempts can legitimately land in the same UTC tick under load.
    /// </summary>
    public Guid EventId { get; set; } = Guid.NewGuid();

    public string KeycloakUserId { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; }
    public bool Success { get; set; }
}
