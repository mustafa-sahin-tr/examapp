using System.ComponentModel.DataAnnotations;

namespace BadgeService.Services;

/// <summary>
/// Config for the Claude-backed question classifier (issue #469), bound from the "Anthropic" section — same
/// section/env name as the exam API's labeling detector, so Aspire passes the one <c>anthropic-api-key</c>
/// secret parameter to both as <c>Anthropic__ApiKey</c>. <see cref="ApiKey"/> never goes into the repo.
/// </summary>
public class ClaudeClassifierOptions
{
    public const string SectionName = "Anthropic";

    public string? ApiKey { get; set; }

    /// <summary>Claude model id, e.g. <c>claude-haiku-5-5</c>.</summary>
    [Required]
    public string Model { get; set; } = "claude-haiku-5-5";

    /// <summary>USD per 1M (uncached) input tokens of <see cref="Model"/> — only for the logged cost estimate.</summary>
    [Range(0, 1_000)]
    public decimal InputUsdPerMTok { get; set; } = 0.10m;

    /// <summary>USD per 1M output tokens of <see cref="Model"/> — only for the logged cost estimate.</summary>
    [Range(0, 1_000)]
    public decimal OutputUsdPerMTok { get; set; } = 0.50m;

    /// <summary><c>low|medium|high|xhigh</c> (MEAI <c>ReasoningEffort</c>); empty = the model's default.</summary>
    public string? Effort { get; set; } = "low";

    /// <summary>Output cap per question (thinking included); the answer itself is a small JSON.</summary>
    [Range(256, 32_000)]
    public int MaxOutputTokens { get; set; } = 4000;

    /// <summary>
    /// Prompt-cache lifetime of the taxonomy block: <c>5m</c> or <c>1h</c>. Questions arrive in sporadic bursts,
    /// so 1h (2x write price, written rarely) usually beats 5m (1.25x, re-written after every quiet gap).
    /// </summary>
    [RegularExpression("^(5m|1h)$")]
    public string CacheTtl { get; set; } = "1h";

    /// <summary>How long the taxonomy fetched from the exam API is reused before it is fetched again.</summary>
    [Range(0, 86_400)]
    public int TaxonomyCacheSeconds { get; set; } = 300;
}
