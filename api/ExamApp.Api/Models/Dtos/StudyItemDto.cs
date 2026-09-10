using System;
using System.Collections.Generic;
using ExamApp.Api.Data;

namespace ExamApp.Api.Models.Dtos;

public class StudyItemDto
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

    public StudyItemContentType ContentType { get; set; }

    // Link
    public string? Url { get; set; }
    public StudyItemLinkPlatform? Platform { get; set; }

    // BookPageRange
    public int? BookId { get; set; }
    public string? BookName { get; set; }
    public int? BookTestId { get; set; }
    public string? BookTestName { get; set; }
    public int? StartPage { get; set; }
    public int? EndPage { get; set; }

    // Image
    public int ImageCount { get; set; }
    public string? CoverImageUrl { get; set; }
    public List<StudyItemImageDto> Images { get; set; } = new();
}

public class StudyItemImageDto
{
    public int Id { get; set; }
    public string ImageUrl { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public string? FileName { get; set; }
}
