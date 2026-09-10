using System.Collections.Generic;
using ExamApp.Api.Data;

namespace ExamApp.Api.Models.Dtos;

public class CreateStudyItemRequestDto
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? GradeId { get; set; }
    public int? SubjectId { get; set; }
    public int? TopicId { get; set; }
    public int? SubTopicId { get; set; }
    public bool IsPublished { get; set; } = true;

    public StudyItemContentType ContentType { get; set; } = StudyItemContentType.Image;

    // Link
    public string? Url { get; set; }
    public StudyItemLinkPlatform? Platform { get; set; }

    // BookPageRange — BookId/BookTestId var olan kayıt; NewBookName/NewBookTestName inline oluşturma
    public int? BookId { get; set; }
    public int? BookTestId { get; set; }
    public string? NewBookName { get; set; }
    public string? NewBookTestName { get; set; }
    public int? StartPage { get; set; }
    public int? EndPage { get; set; }

    // Image
    public string? MinioImages { get; set; } // JSON string
}

public class UpdateStudyItemRequestDto
{
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int? GradeId { get; set; }
    public int? SubjectId { get; set; }
    public int? TopicId { get; set; }
    public int? SubTopicId { get; set; }
    public bool IsPublished { get; set; } = true;

    public StudyItemContentType ContentType { get; set; } = StudyItemContentType.Image;

    // Link
    public string? Url { get; set; }
    public StudyItemLinkPlatform? Platform { get; set; }

    // BookPageRange
    public int? BookId { get; set; }
    public int? BookTestId { get; set; }
    public string? NewBookName { get; set; }
    public string? NewBookTestName { get; set; }
    public int? StartPage { get; set; }
    public int? EndPage { get; set; }

    // Image
    public List<int> RemovedImageIds { get; set; } = new();
    public string? MinioImages { get; set; } // JSON string
}

public class StudyItemFilterDto
{
    public string? Search { get; set; }
    public int? SubjectId { get; set; }
    public int? TopicId { get; set; }
    public int? SubTopicId { get; set; }
    public StudyItemContentType? ContentType { get; set; }
    public int PageNumber { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}

public class AttachStudyItemImageBySubTopicsRequestDto
{
    public string ImageUrl { get; set; } = string.Empty;
    public List<int> SubTopicIds { get; set; } = new();
}

public class AttachStudyItemImageBySubTopicsResultDto : ResponseBaseDto
{
    public int CreatedStudyItemCount { get; set; }
    public int UpdatedStudyItemCount { get; set; }
    public List<int> MissingSubTopicIds { get; set; } = new();
    public List<StudyItemDto> StudyItems { get; set; } = new();
}
