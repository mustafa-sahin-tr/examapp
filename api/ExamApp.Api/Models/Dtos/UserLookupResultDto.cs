namespace ExamApp.Api.Models.Dtos;

public class UserLookupResultDto
{
    public int Id { get; set; }
    public string KeycloakId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Avatar { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Keycloak hesap durumu (issue #152). Yalnızca <see cref="Services.Interfaces.IAuthApiClient.GetUsersWithAccountStatusByIdsAsync"/>
    /// ile doldurulur; aksi halde ya da Keycloak okunamadığında null ("bilinmiyor").
    /// </summary>
    public bool? Enabled { get; set; }
}
