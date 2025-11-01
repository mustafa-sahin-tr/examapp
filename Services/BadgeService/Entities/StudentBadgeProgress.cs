using System;

namespace BadgeService.Entities;


public class StudentBadgeProgress
{
    public Guid Id { get; set; }
    public int UserId { get; set; }
    public Guid BadgeDefinitionId { get; set; }
    public int CurrentValue { get; set; }
    public int TargetValue { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime LastUpdatedUtc { get; set; }
    public BadgeDefinition BadgeDefinition { get; set; } = default!;
}
