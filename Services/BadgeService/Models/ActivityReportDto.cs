using System;
using System.Collections.Generic;

namespace BadgeService.Models;

public class ActivityReportDto
{
    public int UserId { get; set; }
    public DateTime StartDateUtc { get; set; }
    public DateTime EndDateUtc { get; set; }
    public int TotalActiveDays { get; set; }
    public int CurrentStreakDays { get; set; }
    public int BestStreakDays { get; set; }
    public List<ActivityDayDto> Days { get; set; } = new();
}

public class ActivityDayDto
{
    public DateTime DateUtc { get; set; }
    public int QuestionCount { get; set; }
    public int CorrectCount { get; set; }
    public int TotalTimeSeconds { get; set; }
    public int TotalPoints { get; set; }
    public int ActivityScore { get; set; }
}
