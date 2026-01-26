using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

public class QuestionTransferImportMap : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [MaxLength(200)]
    public string SourceKey { get; set; } = "default";

    [MaxLength(200)]
    public string ExternalQuestionKey { get; set; } = string.Empty;

    public int TargetQuestionId { get; set; }
}
