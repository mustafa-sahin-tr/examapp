using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ExamApp.Api.Data;

public class WorksheetPrototypeDetail : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int WorksheetPrototypeId { get; set; }

    [ForeignKey("WorksheetPrototypeId")]
    public WorksheetPrototype WorksheetPrototype { get; set; }

    [Required]
    public int SubjectId { get; set; }

    [ForeignKey("SubjectId")]
    public Subject Subject { get; set; }

    [Required]
    public int QuestionCount { get; set; } // Bu dersten kaç soru olacağı
}
