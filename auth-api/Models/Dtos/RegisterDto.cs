using System.ComponentModel.DataAnnotations;

public class RegisterDto
{
    [Required]
    [MaxLength(100)]
    public string FirstName { get; set; }

    [Required]
    [MaxLength(100)]
    public string LastName { get; set; }

    [Required]
    [EmailAddress]
    public string Email { get; set; }

    [Required]
    [MinLength(6)]
    public string Password { get; set; } 

    [Required]
    public string Role { get; set; } // Öğrenci, Öğretmen veya Veli
}
 