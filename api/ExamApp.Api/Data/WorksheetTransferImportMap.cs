using System.ComponentModel.DataAnnotations;

namespace ExamApp.Api.Data;

/// <summary>Başka ortamdan alınan test ve etkinlik: aynı kaynaktan ikinci kez gelen kayıt yeniden oluşturulmaz.</summary>
public class WorksheetTransferImportMap : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [MaxLength(200)]
    public string SourceKey { get; set; } = string.Empty;

    [MaxLength(200)]
    public string ExternalWorksheetKey { get; set; } = string.Empty;

    public int TargetWorksheetId { get; set; }
}
