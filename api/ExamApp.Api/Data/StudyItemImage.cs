using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

public class StudyItemImage : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int StudyItemId { get; set; }

    [ForeignKey("StudyItemId")]
    public StudyItem StudyItem { get; set; } = default!;

    [Required]
    public string ImageUrl { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public string? FileName { get; set; }
}
