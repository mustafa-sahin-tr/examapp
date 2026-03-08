using System;
using System.Collections.Generic;

namespace ExamApp.Api.Models.Dtos;

public class StudyPageDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? GradeId { get; set; }
    public int? SubjectId { get; set; }
    public int? TopicId { get; set; }
    public int? SubTopicId { get; set; }
    public bool IsPublished { get; set; }
    public int CreatedByUserId { get; set; }
    public string CreatedByName { get; set; } = string.Empty;
    public string CreatedByRole { get; set; } = string.Empty;
    public DateTime CreateTime { get; set; }
    public int ImageCount { get; set; }
    public string? CoverImageUrl { get; set; }
    public List<StudyPageImageDto> Images { get; set; } = new();
}

public class StudyPageImageDto
{
    public int Id { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public string? FileName { get; set; }
}
