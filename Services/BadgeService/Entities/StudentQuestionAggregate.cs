using System;

namespace BadgeService.Entities;

public class StudentQuestionAggregate
{
    public Guid Id { get; set; }
    public int UserId { get; set; }
    public int TotalQuestions { get; set; }
    public int CorrectQuestions { get; set; }
    public int TotalTimeSeconds { get; set; }
    public int TotalPoints { get; set; }
    public int CurrentCorrectStreak { get; set; }
    public int BestCorrectStreak { get; set; }
    public DateTime? LastAnsweredAtUtc { get; set; }
    public DateTime LastUpdatedUtc { get; set; }
}
