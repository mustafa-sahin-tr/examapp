using System.ComponentModel.DataAnnotations;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services.Storage;

namespace ExamApp.Api.Models.Dtos.QuestionReports;

public class CreateQuestionReportDto
{
    public const string ReasonPattern = "^(wrongAnswer|unreadable|wrongContent|other)$";

    [Range(1, int.MaxValue)]
    public int TestInstanceId { get; set; }

    [Required]
    [RegularExpression(ReasonPattern)]
    public string Reason { get; set; } = string.Empty;

    [MaxLength(QuestionReport.NoteMaxLength)]
    [PlainText]
    public string? Note { get; set; }
}

/// <summary>Tekrar bildirim idempotenttir: <see cref="AlreadyReported"/> true döner, kayıt değişmez.</summary>
public class QuestionReportCreatedDto
{
    public bool AlreadyReported { get; set; }
}

/// <summary>Bildireni içermez: soru sahibi öğrenciyi tanımayabilir.</summary>
public class QuestionReportItemDto
{
    public int Id { get; set; }
    public int QuestionId { get; set; }

    [StorageUrl(StorageArea.QuestionImage)]
    public string? QuestionImageUrl { get; set; }

    public string Reason { get; set; } = string.Empty;
    public string? Note { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime CreateTime { get; set; }
}

public class QuestionReportPageDto
{
    public int TotalCount { get; set; }
    public List<QuestionReportItemDto> Items { get; set; } = new();
}
