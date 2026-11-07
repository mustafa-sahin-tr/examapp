using ExamApp.Api.Services.Storage;
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
    [NotStorageUrl("İstek DTO'su: Link tipi dış bağlantı")]
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

    // Issue #933: blok listesi JSON'u ([{id?, type, text, backText, imageUrl, url, platform, questionId}]); null → bloklara dokunma
    public string? Blocks { get; set; }
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
    [NotStorageUrl("İstek DTO'su: Link tipi dış bağlantı")]
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

    // Issue #933: blok listesi JSON'u ([{id?, type, text, backText, imageUrl, url, platform, questionId}]); null → bloklara dokunma
    public string? Blocks { get; set; }
}

/// <summary>Issue #933: <see cref="CreateStudyItemRequestDto.Blocks"/> JSON'undaki tek blok. Sıra listedeki sıradır.</summary>
public class StudyItemBlockRequestDto
{
    /// <summary>Güncellemede var olan blok; null ya da sayfaya ait değilse yeni blok.</summary>
    public int? Id { get; set; }
    public StudyItemBlockType Type { get; set; }
    public string? Text { get; set; }
    public string? BackText { get; set; }
    [NotStorageUrl("İstek DTO'su: StudyItemService StorageAreaPolicy ile normalize eder")]
    public string? ImageUrl { get; set; }
    [NotStorageUrl("İstek DTO'su: Link bloğunun dış bağlantısı")]
    public string? Url { get; set; }
    public StudyItemLinkPlatform? Platform { get; set; }
    public int? QuestionId { get; set; }
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
    [NotStorageUrl("İstek DTO'su: StudyItemService StorageAreaPolicy ile normalize eder")]
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
