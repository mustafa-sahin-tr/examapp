using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using ExamApp.Api.Data;

public class Subject : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } // Örn: "Matematik", "Türkçe", "Fen Bilimleri"

    public ICollection<GradeSubject> GradeSubjects { get; set; }

    public ICollection<Question> Questions { get; set; } = new List<Question>();

}
