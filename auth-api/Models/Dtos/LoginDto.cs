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

    /// <summary>
    /// Issue #347: login başlatılırken istemcinin ürettiği PKCE <c>code_verifier</c> (RFC 7636 §4.1:
    /// 43-128 karakter, <c>[A-Za-z0-9-._~]</c>). Keycloak token ucuna <c>code_verifier</c> olarak iletilir;
    /// authorization isteğindeki S256 <c>code_challenge</c> ile eşleşmezse Keycloak <c>invalid_grant</c> döner.
    /// Zorunlu: verifier'sız değişim kabul edilmez (PKCE'siz başlatılmış akış tamamlanamaz).
    /// </summary>
    [Required]
    [RegularExpression(CodeVerifierPattern)]
    public string CodeVerifier { get; set; } = string.Empty;

    public const string CodeVerifierPattern = @"^[A-Za-z0-9\-._~]{43,128}$";
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
    /// <summary>Kullanıcının dil tercihi (issue #181): "tr" | "en". Boş dönmez.</summary>
    public string PreferredLocale { get; set; } = ExamApp.Foundation.Localization.SupportedLocales.Default;
    // public string? SchoolName { get; set; } // Student bilgisi
    // public string? Department { get; set; } // opsiyonel
}

public class CompleteProfileDto
{
    [Required]
    public string Role { get; set; } = string.Empty;
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