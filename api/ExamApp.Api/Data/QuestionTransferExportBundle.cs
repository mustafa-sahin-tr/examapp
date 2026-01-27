using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

public class QuestionTransferExportBundle : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [MaxLength(200)]
    public string SourceKey { get; set; } = "default";

    public int BundleNo { get; set; }

    public int QuestionCount { get; set; }

    // MinIO URL like: /img/{bucket}/question-transfer/exports/{sourceKey}/bundle-0001.zip
    [MaxLength(1000)]
    public string? FileUrl { get; set; }
}
