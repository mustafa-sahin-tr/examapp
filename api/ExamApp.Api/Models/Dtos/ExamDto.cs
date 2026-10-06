using ExamApp.Api.Services.Storage;
using System;
using System.ComponentModel.DataAnnotations;
using System.Numerics;

namespace ExamApp.Api.Models.Dtos;

public class ExamDto
{

    public int? Id { get; set; }
    public required string Name { get; set; }

    public string? Description { get; set; }

    public required int GradeId { get; set; }

    /// <summary>issue #396: sunucu süre sınırı bu değerden kopyalanır; 0 = süre sınırı yok, üst sınır 24 saat.</summary>
    [Range(0, WorksheetDurationLimits.MaxSeconds)]
    public required int MaxDurationSeconds { get; set; }

    public bool IsPracticeTest { get; set; }

    /// <summary>
    /// issue #105: öğrenciler yorum-soru yazabilir mi. null = oluştururken varsayılan (true), güncellerken mevcut
    /// değer korunur — alanı göndermeyen eski istemciler ayarı yanlışlıkla kapatmasın.
    /// </summary>
    public bool? CommentsEnabled { get; set; }

    public string? Subtitle { get; set; }

    [StorageUrl(StorageArea.WorksheetCover)]
    public string? ImageUrl { get; set; }

    public string? BadgeText { get; set; }


    public int? BookTestId { get; set; }
    public int? BookId { get; set; }

    public int? SubjectId { get; set; }

    public int? TopicId { get; set; }

    public int? SubTopicId { get; set; }

    public string? NewBookName { get; set; }
    public string? NewBookTestName { get; set; }

}

public class ExamSavedDto : ResponseBaseDto
{
    public int? BookId { get; set; }
    public int? BookTestId { get; set; } = null;
    public int? ExamId { get; set; } = null;
}

public class ExamStatisticsDto
{
    public int TotalSolvedTests { get; set; } = 0;
    public int CompletedTests { get; set; } = 0;
    public int TotalTimeSpentMinutes { get; set; } = 0;
    public int TotalCorrectAnswers { get; set; } = 0;
    public int TotalWrongAnswers { get; set; } = 0;
}

public class ExamAllStatisticsDto
{
    public ExamStatisticsDto Total { get; set; } = new();
    public List<ExamStatisticsDto> Grouped { get; set; } = new();
}

/// <summary>issue #396: test süresi sınırları (saniye). 0 = süre sınırı yok.</summary>
public static class WorksheetDurationLimits
{
    public const int MaxSeconds = 86_400;
}
