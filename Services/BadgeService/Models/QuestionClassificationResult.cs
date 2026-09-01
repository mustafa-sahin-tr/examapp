using System.Text.Json.Serialization;

namespace BadgeService.Models;

/// <summary>Structured output returned by the LLM for a single question image.</summary>
public class QuestionClassificationResult
{
    [JsonPropertyName("subTopicIds")]
    public List<int> SubTopicIds { get; set; } = new();

    [JsonPropertyName("subjectId")]
    public int? SubjectId { get; set; }

    [JsonPropertyName("topicId")]
    public int? TopicId { get; set; }

    [JsonPropertyName("difficultyLevel")]
    public int DifficultyLevel { get; set; }

    [JsonPropertyName("reasoning")]
    public string? Reasoning { get; set; }
}
