namespace AssistantService.Data;

/// <summary>
/// Issue #502: assistant database (own DB, no access to other services' data). Users are keyed by the Keycloak
/// <c>sub</c>; content is kept <see cref="AssistantOptions.RetentionDays"/> days (#505 purges it).
/// </summary>
public sealed class Conversation
{
    public Guid Id { get; set; }
    public string UserSub { get; set; } = string.Empty;
    /// <summary>Role the conversation was started with (Student, Parent …); tools are filtered by it.</summary>
    public string Role { get; set; } = string.Empty;
    public string? Title { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<Message> Messages { get; set; } = [];
}

public sealed class Message
{
    public long Id { get; set; }
    public Guid ConversationId { get; set; }
    public Conversation Conversation { get; set; } = null!;
    /// <summary>user, assistant or tool.</summary>
    public string Role { get; set; } = string.Empty;
    /// <summary>Content blocks as JSON (text, tool calls, tool results, cards).</summary>
    public string ContentJson { get; set; } = "[]";
    public string? Model { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public int CacheReadTokens { get; set; }
    public decimal CostUsd { get; set; }
    public DateTime CreatedAt { get; set; }
}

public enum PendingActionStatus
{
    Pending = 1,
    Confirmed = 2,
    Rejected = 3,
    Expired = 4,
    Failed = 5,
}

/// <summary>A write tool's previewed call; it runs only after the user confirms the card (enforced on the server).</summary>
public sealed class PendingAction
{
    public Guid Id { get; set; }
    public Guid ConversationId { get; set; }
    public string UserSub { get; set; } = string.Empty;
    public string Tool { get; set; } = string.Empty;
    public string ArgumentsJson { get; set; } = "{}";
    public string? PreviewJson { get; set; }
    public PendingActionStatus Status { get; set; } = PendingActionStatus.Pending;
    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? DecidedAt { get; set; }
}

/// <summary>Per user per day counters for the budget and rate limits (#504).</summary>
public sealed class UsageDay
{
    public long Id { get; set; }
    public string UserSub { get; set; } = string.Empty;
    public DateOnly Day { get; set; }
    public int Messages { get; set; }
    public int ImageExplanations { get; set; }
    public int InputTokens { get; set; }
    public int OutputTokens { get; set; }
    public decimal CostUsd { get; set; }
}

public sealed class Feedback
{
    public long Id { get; set; }
    public long MessageId { get; set; }
    public string UserSub { get; set; } = string.Empty;
    /// <summary>+1 thumbs up, -1 thumbs down.</summary>
    public int Rating { get; set; }
    /// <summary>e.g. wrong-info, not-helpful; free text is not stored.</summary>
    public string? Reason { get; set; }
    public DateTime CreatedAt { get; set; }
}
