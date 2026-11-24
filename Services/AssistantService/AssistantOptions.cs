using System.ComponentModel.DataAnnotations;

namespace AssistantService;

/// <summary>
/// Issue #502: kill switch and limits (decisions on #496). <see cref="Enabled"/> off, or an empty
/// <c>Anthropic:ApiKey</c>, answers every <c>/api/assistant/*</c> call with 503 <c>AssistantDisabled</c>;
/// <see cref="Roles"/> lists the realm roles that may use it (others get 403). Students first (Phase 1a), parents with #1336.
/// </summary>
public sealed class AssistantOptions
{
    public const string SectionName = "Assistant";

    public bool Enabled { get; set; }

    public string[] Roles { get; set; } = [];

    /// <summary>#1336: entries may be comma separated (<c>ASSISTANT_ROLES=Student,Parent</c>); blanks and repeats are dropped.</summary>
    public static string[] SplitRoles(IEnumerable<string>? roles) =>
        (roles ?? []).SelectMany(r => r.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.Ordinal).ToArray();

    [Required]
    public string Model { get; set; } = "claude-haiku-5-5";

    [Range(0, 100)]
    public decimal DailyBudgetUsdPerUser { get; set; } = 0.10m;

    [Range(0, 10_000)]
    public decimal DailyBudgetUsdTotal { get; set; } = 5m;

    [Range(1, 10_000)]
    public int MessagesPerHour { get; set; } = 30;

    /// <summary>#510: question explanations (<c>explain_question</c>, one image each) per student per day.</summary>
    [Range(0, 1_000)]
    public int ExplanationsPerDay { get; set; } = 20;

    /// <summary>#504: per-turn ceilings against runaway tool loops.</summary>
    [Range(1, 20)]
    public int MaxToolCallsPerTurn { get; set; } = 6;

    /// <summary>#504: output tokens per model call (a turn may make up to MaxToolCallsPerTurn + 1 calls).</summary>
    [Range(64, 16_000)]
    public int MaxOutputTokens { get; set; } = 1024;

    /// <summary>#504: earlier messages sent back to the model (the conversation keeps all of them).</summary>
    [Range(2, 200)]
    public int MaxHistoryMessages { get; set; } = 30;

    /// <summary>low / medium / high. Low keeps the first token fast for chat.</summary>
    public string Effort { get; set; } = "low";

    /// <summary>Prompt cache TTL of the system prompt + tools breakpoint: 5m or 1h.</summary>
    public string CacheTtl { get; set; } = "5m";

    /// <summary>Prices of <see cref="Model"/> for the budget estimate (Haiku 5.5).</summary>
    [Range(0, 1_000)]
    public decimal InputUsdPerMTok { get; set; } = 0.10m;

    [Range(0, 1_000)]
    public decimal OutputUsdPerMTok { get; set; } = 0.50m;

    /// <summary>Conversations are hard-deleted after this many days (#496 decision 2).</summary>
    [Range(1, 365)]
    public int RetentionDays { get; set; } = 30;

    /// <summary>#505: how often the retention purge runs.</summary>
    [Range(1, 24 * 60)]
    public int PurgeIntervalMinutes { get; set; } = 60;
}
