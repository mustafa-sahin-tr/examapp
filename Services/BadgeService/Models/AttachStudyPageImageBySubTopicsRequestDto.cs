using System.Collections.Generic;

namespace BadgeService.Models;

public class AttachStudyPageImageBySubTopicsRequestDto
{
    public string ImageUrl { get; set; } = string.Empty;
    public List<int> SubTopicIds { get; set; } = new();
}