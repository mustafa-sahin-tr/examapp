using System;
using System.Collections.Generic;

namespace BadgeService.Models;

public class BadgeProgressReportDto
{
    public StudentSummaryDto? Summary { get; set; }
    public List<BadgeProgressItemDto> BadgeProgress { get; set; } = new();
    public List<SubjectAggregateDto> SubjectBreakdown { get; set; } = new();
}

public class StudentSummaryDto
{
    public int UserId { get; set; }
    public int TotalQuestions { get; set; }
    public int CorrectQuestions { get; set; }
    public double AccuracyPercentage { get; set; }
    public int TotalPoints { get; set; }
    public int CurrentCorrectStreak { get; set; }
    public int BestCorrectStreak { get; set; }
    public DateTime? LastAnsweredAtUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; }
}

public class BadgeProgressItemDto
{
    public Guid BadgeDefinitionId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string? IconUrl { get; set; }
    public int CurrentValue { get; set; }
    public int TargetValue { get; set; }
    public bool IsCompleted { get; set; }
    public DateTime? EarnedDateUtc { get; set; }
}

public class SubjectAggregateDto
{
    public int? SubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public int TotalQuestions { get; set; }
    public int CorrectQuestions { get; set; }
    public double AccuracyPercentage { get; set; }
    public int TotalPoints { get; set; }
    public int TotalTimeSeconds { get; set; }
}
