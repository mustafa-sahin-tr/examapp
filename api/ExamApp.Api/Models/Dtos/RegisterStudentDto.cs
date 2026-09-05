using System.ComponentModel.DataAnnotations;

public class RegisterStudentDto
{
    [Required]
    [MaxLength(50)]
    public string StudentNumber { get; set; }

    public int? SchoolId { get; set; }

    [Required]
    public int GradeId { get; set; }
}
