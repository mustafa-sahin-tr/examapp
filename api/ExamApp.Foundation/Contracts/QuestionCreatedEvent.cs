using System;

namespace ExamApp.Foundation.Contracts;

public class QuestionCreatedEvent
{
    public int QuestionId { get; set; }
    public int? SubjectId { get; set; }
    public int? TopicId { get; set; }
    public string? ClassificationSource { get; set; } // "Human" or "AI"
    public DateTime CreatedAt { get; set; }
    public string? Text { get; set; }
}
