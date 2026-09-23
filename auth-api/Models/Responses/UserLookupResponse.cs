namespace ExamApp.Api.Models.Responses;

public class UserLookupResponse
{
    public int Id { get; set; }
    public string KeycloakId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Avatar { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Keycloak hesap durumu (<c>enabled</c>, issue #152). Yalnızca istekte <c>IncludeAccountStatus=true</c> iken
    /// doldurulur; istenmediyse, Keycloak'ta kullanıcı yoksa ya da Keycloak erişilemez/zaman aşımındaysa null ("bilinmiyor").
    /// </summary>
    public bool? Enabled { get; set; }
}
