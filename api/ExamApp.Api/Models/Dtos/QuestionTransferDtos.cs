using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace ExamApp.Api.Models.Dtos;

public class StartQuestionExportDto
{
    public List<int> QuestionIds { get; set; } = new();

    // Used for idempotent imports: (SourceKey, ExternalQuestionKey)
    public string? SourceKey { get; set; }
}

public class StartQuestionImportDto
{
    // Used for idempotency. Must match the export SourceKey if provided.
    public string? SourceKey { get; set; }
}

public class StartQuestionImportFormDto
{
    public string? SourceKey { get; set; }

    [Required]
    public IFormFile File { get; set; } = default!;
}

public class QuestionTransferJobDto
{
    public Guid Id { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string SourceKey { get; set; } = "default";
    public int TotalItems { get; set; }
    public int ProcessedItems { get; set; }
    public string? FileUrl { get; set; }
    public string? Message { get; set; }
}

public class QuestionTransferExportBundleDto
{
    public string SourceKey { get; set; } = "default";
    public int BundleNo { get; set; }
    public int QuestionCount { get; set; }
    public string? FileUrl { get; set; }
}

public class QuestionTransferImportPreviewDto
{
    public string SourceKey { get; set; } = "default";
    public int QuestionCount { get; set; }
    public int AlreadyImportedCount { get; set; }
}
