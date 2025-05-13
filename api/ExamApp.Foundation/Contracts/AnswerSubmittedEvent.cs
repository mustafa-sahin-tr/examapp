using System;

namespace ExamApp.Foundation.Contracts;

public class AnswerSubmittedEvent
{
    public int UserId { get; set; }
    public int QuestionId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public int TestInstanceId { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public int? SelectedAnswerId { get; set; }
    public int TimeTakenInSeconds { get; set; }
    public DateTime SubmittedAt { get; set; }
}

