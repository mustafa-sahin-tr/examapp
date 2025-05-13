public class BadgeDefinition
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string Description { get; set; } = default!;
    public string? IconUrl { get; set; }
    public string Category { get; set; } = default!;
    public string RuleType { get; set; } = default!; // "AnswerCount", "CorrectStreak" vb.
    public string RuleConfigJson { get; set; } = default!;
}
