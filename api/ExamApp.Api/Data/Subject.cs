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

    /// <summary>Ortamlar arasında değişmeyen kod; ad değişse de korunur (bkz. CatalogCode).</summary>
    [MaxLength(400)]
    public string? Code { get; set; }

    public ICollection<GradeSubject> GradeSubjects { get; set; }

    public ICollection<Question> Questions { get; set; } = new List<Question>();

    /// <summary>Bu dersi veren bağımsız öğretmenler (issue #95).</summary>
    public ICollection<TeacherSubject> TeacherSubjects { get; set; } = new List<TeacherSubject>();

}
