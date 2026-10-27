using System;
using System.Collections.Generic;

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
    public string? ClassificationSource { get; set; } // "Human", "AI" or "Book" (issue #577)

    /// <summary>
    /// Issue #577: the subtopics the question was created with (e.g. from a book's table of contents). With
    /// <c>ClassificationSource = "Book"</c> the consumer skips classification when this is non-empty and, in
    /// <c>Hybrid</c> mode, classifies only within <see cref="TopicId"/> otherwise. Null from older publishers = unknown
    /// (treated as empty).
    /// </summary>
    public List<int>? SubTopicIds { get; set; }
    public DateTime CreatedAt { get; set; }
    public string? Text { get; set; }
}
