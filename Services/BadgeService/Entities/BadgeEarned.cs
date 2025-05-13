public class BadgeEarned
{
    public Guid Id { get; set; }
    public int UserId { get; set; }
    public Guid BadgeDefinitionId { get; set; }
    public DateTime EarnedDate { get; set; }

    public BadgeDefinition BadgeDefinition { get; set; } = default!;
}
