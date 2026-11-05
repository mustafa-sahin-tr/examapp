using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using ExamApp.Api.Data;
using ExamApp.Api.Services.Storage;

namespace ExamApp.Api.Models.Dtos;

public class SaveFlashcardDeckDto
{
    [Required, MaxLength(150)]
    public string Title { get; set; } = string.Empty;

    public int? GradeId { get; set; }
    public int? SubjectId { get; set; }
    public int? TopicId { get; set; }
}

public class SaveFlashcardDto
{
    [MaxLength(Flashcard.MaxTextLength)]
    public string? Front { get; set; }

    [Required, MaxLength(Flashcard.MaxTextLength)]
    public string Back { get; set; } = string.Empty;

    /// <summary>Yeni ön yüz görseli, <c>data:image/...;base64,</c> biçiminde. Boşsa mevcut görsel kalır.</summary>
    [MaxLength(3_000_000)]
    public string? Image { get; set; }

    public bool RemoveImage { get; set; }
}

public class AssignFlashcardDeckDto
{
    public int? StudentId { get; set; }
    public int? GradeId { get; set; }
}

public class FlashcardReviewRequestDto
{
    /// <summary><c>Forgot</c>, <c>Hard</c> ya da <c>Good</c>.</summary>
    [Required]
    public string Recall { get; set; } = string.Empty;
}

public class FlashcardDeckSummaryDto
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public int? GradeId { get; set; }
    public string? GradeName { get; set; }
    public int? SubjectId { get; set; }
    public string? SubjectName { get; set; }
    public int? TopicId { get; set; }
    public string? TopicName { get; set; }
    public int CardCount { get; set; }

    /// <summary>Öğrenci listesinde bugün gelen kart sayısı; sahip görünümünde boş.</summary>
    public int? DueCount { get; set; }

    /// <summary>Çağıranın kendi destesi: kart ekleyip düzenleyebilir.</summary>
    public bool IsOwner { get; set; }
}

public class FlashcardDto
{
    public int Id { get; set; }
    public string Front { get; set; } = string.Empty;
    public string Back { get; set; } = string.Empty;

    [StorageUrl(StorageArea.QuestionImage)]
    public string? ImageUrl { get; set; }

    public int Order { get; set; }
}

public class FlashcardDeckDetailDto : FlashcardDeckSummaryDto
{
    public List<FlashcardDto> Cards { get; set; } = new();
}

public class FlashcardDueCardDto : FlashcardDto
{
    public int DeckId { get; set; }
    public string DeckTitle { get; set; } = string.Empty;

    /// <summary>Boş: kart yeni, daha önce tekrar edilmedi.</summary>
    public int? Box { get; set; }
}

public class FlashcardDueCountDto
{
    public int Count { get; set; }
}

public class FlashcardReviewResultDto
{
    public int CardId { get; set; }
    public int Box { get; set; }
    public DateOnly DueDay { get; set; }
}

public class FlashcardDeckAssignmentDto
{
    public int Id { get; set; }
    public int? StudentId { get; set; }
    public string? StudentName { get; set; }
    public int? GradeId { get; set; }
    public string? GradeName { get; set; }
}
