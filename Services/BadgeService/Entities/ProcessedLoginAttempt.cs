using System;

namespace BadgeService.Entities;

/// <summary>
/// Idempotency ledger for <c>LoginAttemptedConsumer</c> (issue #84). Redelivery/duplicate-publish
/// detection is keyed on the producer-assigned <see cref="EventId"/> (unique) — not on
/// (user, timestamp, success), since two distinct attempts can share a UTC tick under load and
/// would otherwise be wrongly deduped away. A row is inserted only after the write-back to exam
/// API's <c>POST /api/login-events</c> succeeds; a duplicate delivery hits the unique index and is
/// treated as a no-op (see <c>LoginAttemptedConsumer</c>).
/// </summary>
public class ProcessedLoginAttempt
{
    public int Id { get; set; }

    public Guid EventId { get; set; }

    public string KeycloakUserId { get; set; } = string.Empty;

    public DateTime OccurredAtUtc { get; set; }

    public bool Success { get; set; }

    public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
}
