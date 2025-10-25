using System;

namespace ExamApp.Api.Data;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

public class Student : BaseEntity
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    public int UserId { get; set; }    

    [Required, MaxLength(50)]
    public string StudentNumber { get; set; }

    [Required, MaxLength(100)]
    public string SchoolName { get; set; }    
 
    public int? GradeId { get; set; } // 🟢 Grade artık opsiyonel (nullable)

    [ForeignKey("GradeId")]
    public Grade? Grade { get; set; } // 🟢 Grade ilişkisi

    // public virtual ICollection<ExamResult> ExamResults { get; set; }

    public virtual ICollection<StudentPoint> StudentPoints { get; set; } = new List<StudentPoint>();
    public virtual ICollection<StudentBadge> StudentBadges { get; set; } = new List<StudentBadge>();
}
