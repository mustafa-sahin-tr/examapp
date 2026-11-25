using System;

namespace ExamApp.Foundation.Contracts;

/// <summary>
/// Issue #519 (assistant Phase 4): a student who has not answered a question for a few days gets one in-app nudge
/// ("3 gündür soru çözmedin"). Written by the exam API's <c>StudentStudyNudgeJob</c> in the same transaction as the student's
/// <c>LastStudyNudgeAtUtc</c> stamp; BadgeService creates the notification. One recipient (the student) per event.
///
/// Security: ids and a day count only; no e-mail, no names.
/// </summary>
public class StudentStudyNudgeEvent
{
    /// <summary>Idempotency key.</summary>
    public Guid EventId { get; set; }

    /// <summary>Students.Id.</summary>
    public int StudentId { get; set; }

    /// <summary>The student's user id (Notification.UserId).</summary>
    public int RecipientUserId { get; set; }

    /// <summary>The student's Keycloak sub; may be empty (the consumer resolves it from BadgeService data).</summary>
    public string RecipientKeycloakId { get; set; } = string.Empty;

    /// <summary>Whole days since the student's last answer.</summary>
    public int InactiveDays { get; set; }

    public DateTime CreatedAtUtc { get; set; }
}
