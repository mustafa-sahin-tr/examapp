using System;

namespace ExamApp.Api.Data;
using System.ComponentModel.DataAnnotations;
public class Student
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    public int UserId { get; set; }
    public virtual User User { get; set; }

    [Required, MaxLength(50)]
    public string StudentNumber { get; set; }

    [Required, MaxLength(100)]
    public string SchoolName { get; set; }

    // public virtual ICollection<ExamResult> ExamResults { get; set; }
}
