using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace BadgeService.Models;

public class StudyPageAnalysisResponseDto
{
    public string Subject { get; set; } = string.Empty;
    public string Topic { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public List<string> Subtopics { get; set; } = new();

    [JsonPropertyName("subtopic_ids")]
    public List<int> SubtopicIds { get; set; } = new();
}