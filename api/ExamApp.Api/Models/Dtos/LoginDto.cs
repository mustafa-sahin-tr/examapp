using System.ComponentModel.DataAnnotations;

public class LoginDto
{
    [Required]
    [EmailAddress]
    public string Email { get; set; }

    [Required]
    [MinLength(6)]
    public string Password { get; set; }
}

public class CodeDto
{
    [Required]    
    public string Code { get; set; }
}
