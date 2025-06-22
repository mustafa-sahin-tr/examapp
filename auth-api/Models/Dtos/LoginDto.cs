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
 

 public class UserProfileDto
{
    public int Id { get; set; }
    public string KeycloakId { get; set; }
    public string FullName { get; set; }
    public string Email { get; set; }
    public string Role { get; set; }
    public string Avatar { get; set;}
    public int ProfileId { get; set;}    
    // public string? SchoolName { get; set; } // Student bilgisi
    // public string? Department { get; set; } // opsiyonel
}

public class RealmAccess
{
    public required List<string> roles {get;set;}
}

public class LoginResponseDto
{
    public required string Token { get; set; }
    public required List<string> Roles {get;set;}
}