using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ExamApp.Api.Data;

public class StudyPageImage : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int StudyPageId { get; set; }

    [ForeignKey("StudyPageId")]
    public StudyPage StudyPage { get; set; } = default!;

    [Required]
    public string ImageUrl { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public string? FileName { get; set; }
}
