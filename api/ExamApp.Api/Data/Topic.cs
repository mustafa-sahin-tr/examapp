using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using ExamApp.Api.Data;

public class Topic : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } // Örn: "Okuduğunu Anlama"

    [Required]
    public int SubjectId { get; set; } // Hangi derse ait?

    [ForeignKey("SubjectId")]
    public Subject Subject { get; set; }

    [Required]
    public int GradeId { get; set; }

    [ForeignKey("GradeId")]
    public Grade Grade  { get; set; }

    public ICollection<SubTopic> SubTopics { get; set; }
}
