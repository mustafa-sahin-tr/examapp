using System;

namespace ExamApp.Foundation.Contracts;

public class AnswerSubmittedEvent
{
    public int UserId { get; set; }
    public int QuestionId { get; set; }
    public int? SubjectId { get; set; }
    public string Subject { get; set; } = string.Empty;
    public int? TopicId { get; set; }
    public int? SubTopicId { get; set; }
    public int TestInstanceId { get; set; }
    public string ClientId { get; set; } = string.Empty;
    public int? SelectedAnswerId { get; set; }
    public bool IsCorrect { get; set; }
    public int QuestionPoint { get; set; }
    public int DifficultyLevel { get; set; }
    public int TimeTakenInSeconds { get; set; }
    public DateTime SubmittedAt { get; set; }
}

