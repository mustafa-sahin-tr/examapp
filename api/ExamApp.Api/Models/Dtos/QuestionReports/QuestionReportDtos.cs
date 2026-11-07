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

/// <summary>
/// Bildireni içermez: soru sahibi öğrenciyi tanımayabilir. #865: bir satır bir sorudur — aynı sorunun aynı durumdaki
/// bildirimleri birleşir; <see cref="Id"/> en yeni bildirimin kimliğidir ve karar uçları onunla çağrılır.
/// </summary>
public class QuestionReportItemDto
{
    public int Id { get; set; }
    public int QuestionId { get; set; }

    [StorageUrl(StorageArea.QuestionImage)]
    public string? QuestionImageUrl { get; set; }

    /// <summary>En yeni bildirimin nedeni.</summary>
    public string Reason { get; set; } = string.Empty;

    /// <summary>Birleşen bildirimlerdeki farklı nedenler, en yeni önce.</summary>
    public List<string> Reasons { get; set; } = new();

    public string? Note { get; set; }

    /// <summary>Birleşen bildirimlerin boş olmayan notları, en yeni önce (en çok <c>NotesLimit</c>).</summary>
    public List<string> Notes { get; set; } = new();

    public int ReportCount { get; set; }

    /// <summary>Soruyu içeren testlerin adları (en çok <c>TestNamesLimit</c>) ve toplam test sayısı.</summary>
    public List<string> TestNames { get; set; } = new();
    public int TestCount { get; set; }

    /// <summary>Soruyu cevaplayan farklı öğrenci sayısı.</summary>
    public int AnsweredCount { get; set; }

    public string Status { get; set; } = string.Empty;

    /// <summary>En yeni bildirimin zamanı.</summary>
    public DateTime CreateTime { get; set; }

    /// <summary><c>fixed</c> | <c>noIssue</c>; #865 öncesi kapatılanlarda null.</summary>
    public string? Outcome { get; set; }
    public string? OutcomeNote { get; set; }
    public DateTime? ReviewedAt { get; set; }
    public bool ReviewedByMe { get; set; }
}

public class QuestionReportPageDto
{
    public int TotalCount { get; set; }
    public List<QuestionReportItemDto> Items { get; set; } = new();
}

/// <summary>#865: "Soruyu düzelttim" ya da "Sorun yok" (+ kısa not).</summary>
public class ReviewQuestionReportDto
{
    public const string OutcomePattern = "^(fixed|noIssue)$";

    [Required]
    [RegularExpression(OutcomePattern)]
    public string Outcome { get; set; } = string.Empty;

    [MaxLength(QuestionReport.NoteMaxLength)]
    [PlainText]
    public string? Note { get; set; }
}
