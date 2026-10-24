using System;

namespace ExamApp.Foundation.Contracts;

public class QuestionCreatedEvent
{
    public int QuestionId { get; set; }
    public int? SubjectId { get; set; }
    public int? TopicId { get; set; }

    /// <summary>
    /// Issue #565: the grade the question belongs to, when the source knows it (labeling screen selection, or the
    /// worksheet's grade). The AI classifier restricts the taxonomy to this grade; null = whole taxonomy.
    /// </summary>
    public int? GradeId { get; set; }
    public string? ClassificationSource { get; set; } // "Human" or "AI"
    public DateTime CreatedAt { get; set; }
    public string? Text { get; set; }
}
