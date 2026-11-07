using ExamApp.Api.Services.Storage;
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
    [NotStorageUrl("Link tipi çalışma etkinliğinin dış bağlantısı (YouTube/EBA vb.), MinIO nesnesi değil")]
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
    [StorageUrl(StorageArea.StudyPage)]
    public string? CoverImageUrl { get; set; }
    public List<StudyItemImageDto> Images { get; set; } = new();

    /// <summary>Issue #933: ana içerikten sonra gösterilen bloklar, sırayla.</summary>
    public List<StudyItemBlockDto> Blocks { get; set; } = new();

    /// <summary>Issue #933: silinmemiş blok sayısı; liste blokları taşımaz, kart bunu gösterir.</summary>
    public int BlockCount { get; set; }
}

/// <summary>Issue #933: tek içerik bloğu; tipe ait olmayan alanlar null.</summary>
public class StudyItemBlockDto
{
    public int Id { get; set; }
    public int SortOrder { get; set; }
    public StudyItemBlockType Type { get; set; }
    public string? Text { get; set; }
    public string? BackText { get; set; }
    [StorageUrl(StorageArea.StudyPage)]
    public string? ImageUrl { get; set; }
    [NotStorageUrl("Link bloğunun dış bağlantısı (YouTube/EBA vb.), MinIO nesnesi değil")]
    public string? Url { get; set; }
    public StudyItemLinkPlatform? Platform { get; set; }
    public int? QuestionId { get; set; }
    /// <summary>Soru bloğu önizlemesi: soru kökü metni (varsa).</summary>
    public string? QuestionText { get; set; }
    [StorageUrl(StorageArea.QuestionImage)]
    public string? QuestionImageUrl { get; set; }
}

/// <summary>Issue #933: <c>POST api/study-items/block-images</c> yanıtı. Değer kayıtta <c>imageUrl</c> olarak geri gönderilir.</summary>
public class StudyItemBlockImageUploadDto
{
    [StorageUrl(StorageArea.StudyPage)]
    public string ImageUrl { get; set; } = string.Empty;
}

public class StudyItemImageDto
{
    public int Id { get; set; }
    [StorageUrl(StorageArea.StudyPage)]
    public string ImageUrl { get; set; } = string.Empty;
    public int SortOrder { get; set; }
    public string? FileName { get; set; }
}
