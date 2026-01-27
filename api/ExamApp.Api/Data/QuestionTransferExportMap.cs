using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

public class QuestionTransferExportMap : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [MaxLength(200)]
    public string SourceKey { get; set; } = "default";

    public int QuestionId { get; set; }

    public int BundleNo { get; set; }
}
