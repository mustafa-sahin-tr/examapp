using System;

namespace ExamApp.Api.Models.Dtos;

public class EndTestInstanceDto
{
    public int Id { get; set; }

    public int DurationSeconds { get; set; }

}

public class InstanceSummaryDto
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public DateTime CompletedDate { get; set; }
    public int Score { get; set; }
    public int DurationMinutes { get; set; }
    public int CorrectAnswers { get; set; }
    public int WrongAnswers { get; set; }
    public int TotalQuestions { get; set; }

    public int Status { get; set; } // 0: In Progress, 1: Completed, 2: Canceled
}
