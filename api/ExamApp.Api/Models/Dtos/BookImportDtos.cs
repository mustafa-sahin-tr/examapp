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
    public string? Error { get; set; }
}

/// <summary>Issue #1018 / #1019: başlangıç ekranındaki "Bu ay N/M kitap" kartı (Limit null = sınırsız).</summary>
public class BookImportQuotaDto
{
    public int? Limit { get; set; }
    public int Used { get; set; }
    public DateTime? ResetsAtUtc { get; set; }
    public bool Allowed { get; set; }
}
