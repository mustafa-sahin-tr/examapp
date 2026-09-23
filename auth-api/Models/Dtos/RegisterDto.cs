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
    [MaxLength(254)] // RFC 5321; kullanıcı adı = e-posta (#240: biçim kontrolü controller'da da, DB'den önce)
    public string Email { get; set; }

    [Required]
    [MinLength(6)]
    // Realm'de passwordPolicy tanımlı değil (deploy/keycloak/import/realm-export.json) — Keycloak'ın ek kuralı yok;
    // üst sınır yalnızca hash maliyetini sınırlar (#240).
    [MaxLength(128)]
    public string Password { get; set; } 

    [Required]
    public string Role { get; set; } // Öğrenci, Öğretmen veya Veli
}
 