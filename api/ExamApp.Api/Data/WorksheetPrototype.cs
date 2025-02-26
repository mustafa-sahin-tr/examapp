using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class WorksheetPrototype
{
    [Key]
    public int Id { get; set; }

    [Required]
    public string Name { get; set; } // Örn: "3. Sınıf Genel Test"

    [Required]
    public int GradeId { get; set; }

    [ForeignKey("GradeId")]
    public Grade Grade { get; set; }

    [Required]
    public int DifficultyLevel { get; set; } // 1-10 arasında zorluk seviyesi

    public ICollection<WorksheetPrototypeDetail> WorksheetPrototypeDetails { get; set; }
}
