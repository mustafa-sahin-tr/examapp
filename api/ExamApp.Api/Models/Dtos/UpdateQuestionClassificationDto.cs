using ExamApp.Api.Data;

namespace ExamApp.Api.Models.Dtos;

public class UpdateQuestionClassificationDto
{
    // If provided as 0, it will be treated as null/cleared.
    public int? SubjectId { get; set; }

    // If provided as 0, it will be treated as null/cleared.
    public int? TopicId { get; set; }

    // If provided, question subtopic mapping will be replaced with this single value.
    // If provided as 0, all subtopic mappings will be cleared.
    public int? SubTopicId { get; set; }

    // If provided, question subtopic mappings will be replaced with these values.
    // If provided as empty array, all subtopic mappings will be cleared.
    // If both SubTopicId and SubTopicIds are provided, SubTopicIds takes precedence.
    public int[]? SubTopicIds { get; set; }

    // "Human" or "AI" as string; will be parsed to enum in service
    public string? ClassificationSource { get; set; }
}
