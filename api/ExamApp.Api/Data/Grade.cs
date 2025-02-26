using System.ComponentModel.DataAnnotations;
using System.Collections.Generic;

public class Grade
{
    [Key]
    public int Id { get; set; }

    [Required]
    [MaxLength(50)]
    public string Name { get; set; } // Örn: "1. Sınıf", "5. Sınıf", "9. Sınıf"

    public ICollection<GradeSubject> GradeSubjects { get; set; }
}
