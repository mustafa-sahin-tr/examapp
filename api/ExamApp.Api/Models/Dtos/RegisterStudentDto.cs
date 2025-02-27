using System.ComponentModel.DataAnnotations;

public class RegisterStudentDto
{
    [Required]
    [MaxLength(50)]
    public string StudentNumber { get; set; }

    [Required]
    [MaxLength(100)]
    public string SchoolName { get; set; }

    [Required] 
    public int GradeId { get; set; }
}
