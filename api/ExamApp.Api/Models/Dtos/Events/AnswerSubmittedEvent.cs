using System;

namespace ExamApp.Api.Models.Dtos.Events;

public class AnswerSubmittedEvent
{
    public int UserId { get; set; }
    public int QuestionId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public int TestInstanceId { get; set; }
    public int? SelectedAnswerId { get; set; }
    public int TimeTakenInSeconds { get; set; }
    public DateTime SubmittedAt { get; set; }
}

