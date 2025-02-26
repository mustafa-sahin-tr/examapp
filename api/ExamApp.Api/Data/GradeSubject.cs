using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class GradeSubject
{
    [Key]
    public int Id { get; set; }

    [Required]
    public int GradeId { get; set; }

    [ForeignKey("GradeId")]
    public Grade Grade { get; set; }

    [Required]
    public int SubjectId { get; set; }

    [ForeignKey("SubjectId")]
    public Subject Subject { get; set; }
}
