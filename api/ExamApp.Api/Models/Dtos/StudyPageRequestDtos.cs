using System.Collections.Generic;

namespace ExamApp.Api.Models.Dtos;

public class CreateStudyPageRequestDto
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? GradeId { get; set; }
    public int? SubjectId { get; set; }
    public int? TopicId { get; set; }
    public int? SubTopicId { get; set; }
    public bool IsPublished { get; set; } = true;
}

public class UpdateStudyPageRequestDto
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? GradeId { get; set; }
    public int? SubjectId { get; set; }
    public int? TopicId { get; set; }
    public int? SubTopicId { get; set; }
    public bool IsPublished { get; set; } = true;
    public List<int> RemovedImageIds { get; set; } = new();
}

public class StudyPageFilterDto
{
    public string? Search { get; set; }
    public int? SubjectId { get; set; }
    public int? TopicId { get; set; }
    public int? SubTopicId { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
