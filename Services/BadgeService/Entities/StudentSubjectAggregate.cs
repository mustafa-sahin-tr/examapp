using System;

namespace BadgeService.Entities;

public class StudentSubjectAggregate
{
    public Guid Id { get; set; }
    public int UserId { get; set; }
    public int? SubjectId { get; set; }
    public string SubjectName { get; set; } = string.Empty;
    public int TotalQuestions { get; set; }
    public int CorrectQuestions { get; set; }
    public int TotalTimeSeconds { get; set; }
    public int TotalPoints { get; set; }
    public DateTime LastUpdatedUtc { get; set; }
}
