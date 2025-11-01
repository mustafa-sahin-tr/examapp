using System;

namespace BadgeService.Entities;

public class StudentDailyActivity
{
    public Guid Id { get; set; }
    public int UserId { get; set; }
    public DateTime ActivityDate { get; set; }
    public int QuestionCount { get; set; }
    public int CorrectCount { get; set; }
    public int TotalTimeSeconds { get; set; }
    public int TotalPoints { get; set; }
    public int ActivityScore { get; set; }
    public DateTime LastUpdatedUtc { get; set; }
}
