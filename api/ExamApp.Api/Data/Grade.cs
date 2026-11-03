using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;
using ExamApp.Api.Data;

public class Grade : BaseEntity
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } // Örn: "1. Sınıf", "5. Sınıf", "9. Sınıf"

    /// <summary>Ortamlar arasında değişmeyen kod; ad değişse de korunur (bkz. CatalogCode).</summary>
    [MaxLength(400)]
    public string? Code { get; set; }

    public ICollection<GradeSubject> GradeSubjects { get; set; }
}
