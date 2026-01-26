using System;
using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

public enum QuestionTransferJobKind
{
    Export = 1,
    Import = 2,
}

public enum QuestionTransferJobStatus
{
    Queued = 1,
    Running = 2,
    Completed = 3,
    Failed = 4,
}

public class QuestionTransferJob : BaseEntity
{
    [Key]
    public Guid Id { get; set; }

    public QuestionTransferJobKind Kind { get; set; }

    public QuestionTransferJobStatus Status { get; set; } = QuestionTransferJobStatus.Queued;

    // Free-form key to avoid re-importing questions from the same source.
    // Example: "prod-eu" or a GUID.
    [MaxLength(200)]
    public string SourceKey { get; set; } = "default";

    // JSON payload (question ids, options, etc.)
    public string? RequestJson { get; set; }

    public int TotalItems { get; set; }

    public int ProcessedItems { get; set; }

    // For export jobs: URL of the produced zip.
    // For import jobs: URL of the uploaded input zip.
    public string? FileUrl { get; set; }

    public string? Message { get; set; }
}
