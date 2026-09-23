namespace ExamApp.Api.Models.Responses;

/// <summary>
/// <c>POST /api/auth/register</c> kabul yanıtı (issue #240). E-posta yeni de olsa zaten kayıtlı da olsa
/// birebir aynı gövde döner — kullanıcı kimliği/KeycloakId gibi kaydın gerçekleştiğini ele veren alan içermez.
/// </summary>
public class RegisterResponse
{
    public string Message { get; set; } = string.Empty;
}
