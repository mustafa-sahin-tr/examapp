using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Http;

namespace ExamApp.Api.Models.Dtos;

/// <summary>Issue #1018: kitap PDF'i yükleme formu.</summary>
public class StartBookImportFormDto
{
    public IFormFile? File { get; set; }

    public string? BookName { get; set; }

    public int GradeId { get; set; }

    public int? SubjectId { get; set; }
}

/// <summary>Issue #1018: aktarımlar listesi satırı ve takip ekranının başlığı.</summary>
public class BookImportJobDto
{
    public Guid Id { get; set; }
    public string BookName { get; set; } = string.Empty;
    public int GradeId { get; set; }
    public int? SubjectId { get; set; }
    public int? BookId { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Stage { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public long FileSizeBytes { get; set; }
    public int? PageCount { get; set; }
    public int StageTotal { get; set; }
    public int StageProcessed { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    /// <summary>İşin sahibi (Admin listesinde başkalarının işlerini ayırmak için).</summary>
    public int? OwnerId { get; set; }
}

/// <summary>Issue #1018: takip ekranı — iş + test satırları.</summary>
public class BookImportJobDetailDto : BookImportJobDto
{
    public List<BookImportJobTestDto> Tests { get; set; } = new();
}

public class BookImportJobTestDto
{
    public int Order { get; set; }
    public int Unit { get; set; }
    public int TestNo { get; set; }
    public string? Title { get; set; }
    public string Status { get; set; } = string.Empty;
    public int QuestionsFound { get; set; }
    public int KeyAnswers { get; set; }
    public List<string> Flags { get; set; } = new();

    /// <summary>Issue #1022: taslak kaydedilen ve kırpma tekrarı olduğu için atlanan soru sayısı.</summary>
    public int QuestionsSaved { get; set; }
    public int DuplicatesSkipped { get; set; }

    /// <summary>Issue #1022: aktarım inceleme işaretleri (kod, dosya sayfaları, soru numaraları).</summary>
    public IReadOnlyList<Services.BookImport.BookImportReviewFlag> ReviewFlags { get; set; } = [];

    public string? Error { get; set; }
}

/// <summary>Issue #1018 / #1019: başlangıç ekranındaki "Bu ay N/M kitap" kartı (Limit null = sınırsız).</summary>
public class BookImportQuotaDto
{
    public int? Limit { get; set; }
    public int Used { get; set; }
    public DateTime? ResetsAtUtc { get; set; }
    public bool Allowed { get; set; }

    /// <summary>Kullanıcının planı (#1019; ör. <c>free</c>). Sınır 0 ise arayüz demoya (#1032) yönlendirir.</summary>
    public string? PlanKey { get; set; }
}

/// <summary>Issue #1021: kurulum onay ekranı — kurulum özeti, üniteler ve düzenlenebilir test tablosu.</summary>
public class BookImportSetupDto
{
    public Guid JobId { get; set; }
    public string BookName { get; set; } = string.Empty;
    public int? PageCount { get; set; }

    /// <summary>Basılı = dosya + ofset; null: basılı sayfa no okunamadı.</summary>
    public int? Offset { get; set; }

    public List<BookImportPageRangeDto> AnswerKeyRanges { get; set; } = new();
    public List<int> SelfAssessmentPages { get; set; } = new();
    public List<BookImportSetupUnitDto> Units { get; set; } = new();
    public List<string> OrphanKeys { get; set; } = new();

    /// <summary>Kitapta cevap anahtarı bulunamadı: sorular cevapsız aktarılır (devam / iptal).</summary>
    public bool NoAnswerKey { get; set; }

    public decimal AiCostUsd { get; set; }
    public int SchemaRevision { get; set; }

    /// <summary>Aynı adlı mevcut kitap (onayda "mevcut kitabı kullan" ya da yeni ad seçilir).</summary>
    public BookImportBookRefDto? ExistingBook { get; set; }

    public List<BookImportSetupTestDto> Tests { get; set; } = new();
}

public record BookImportPageRangeDto(int FileFrom, int FileTo);

public record BookImportBookRefDto(int Id, string Name);

public class BookImportSetupUnitDto
{
    public int Unit { get; set; }
    public string? Title { get; set; }
    public int FileFrom { get; set; }
    public int FileTo { get; set; }
    public int? TopicId { get; set; }
    public string? TopicName { get; set; }
}

public class BookImportSetupTestDto
{
    public int Order { get; set; }
    public int Unit { get; set; }
    public int TestNo { get; set; }
    public string? Title { get; set; }
    public int? Minutes { get; set; }
    public int? FileFrom { get; set; }
    public int? FileTo { get; set; }
    public int? ExpectedQuestions { get; set; }

    /// <summary>Soru sırasıyla harf (A–E, okunamayan "?"); null anahtar yok.</summary>
    public string? AnswerKey { get; set; }

    /// <summary>"." uyumlu; harf ikinci okumanın değeri; "-" ikinci okumada yok.</summary>
    public string? KeyConflicts { get; set; }

    public List<string> Flags { get; set; } = new();
}

/// <summary>Issue #1021: kurulum onayı. Listede olmayan test aktarılmaz; anahtar boş/null ise test cevapsız aktarılır.</summary>
public class ConfirmBookImportSetupDto
{
    public string? BookName { get; set; }

    /// <summary>Aynı adlı mevcut kitabı kullan (kayıt kümeleri boşsa kurulumla doldurulur).</summary>
    public int? ExistingBookId { get; set; }

    public List<ConfirmBookImportTestDto> Tests { get; set; } = new();
}

public class ConfirmBookImportTestDto
{
    public int Order { get; set; }
    public string? Title { get; set; }
    public int? Minutes { get; set; }
    public int FileFrom { get; set; }
    public int FileTo { get; set; }
    public string? AnswerKey { get; set; }
}
