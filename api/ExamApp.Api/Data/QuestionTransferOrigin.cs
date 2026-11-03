using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

/// <summary>Bu ortamın kalıcı kimliği: tek satır. Paketler bununla işaretlenir, aynı ortamdan gelen soru ikinci kez yazılmaz.</summary>
public class QuestionTransferOrigin
{
    public const int SingleRowId = 1;

    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public int Id { get; set; } = SingleRowId;

    [MaxLength(200)]
    public string Key { get; set; } = string.Empty;
}
