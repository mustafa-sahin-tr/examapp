using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Issue #505 (epic #495): a student's own data was reset (<c>StudentResetJob</c>, #396). The exam API writes it to its
/// outbox in the SAME SaveChanges that removes the student's sessions, answers and programs. AssistantService consumes it and
/// hard-deletes the student's assistant conversations (messages, pending actions and feedback go with them); its data lives in
/// the assistant DB, so the consumer is there (architecture.md, #225 rule).
///
/// Security: ids and the Keycloak sub only; no names, no e-mail.
/// </summary>
public class StudentProgressResetEvent
{
    /// <summary>Correlation id of this reset (each job attempt writes a new one; the delete is idempotent).</summary>
    public Guid EventId { get; set; }

    /// <summary>Keycloak <c>sub</c> of the student — the assistant keys its data by it.</summary>
    public string KeycloakId { get; set; } = string.Empty;

    /// <summary>auth-api User.Id.</summary>
    public int UserId { get; set; }

    /// <summary>Students.Id.</summary>
    public int StudentId { get; set; }

    /// <summary>Reset line (exam API clock, UTC). Consumers delete what was created before it.</summary>
    public DateTime ResetAtUtc { get; set; }
}
