using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Interfaces;

/// <summary>
/// issue #189: BaseController ve AuthController arasında ortaklaştırılmış profil yükleme
/// deseni (Redis get-or-set + auth-api + SchoolId DB doğrulaması). Service account kısa devresi
/// ve hata fallback'i çağıran taraftadır (BaseController.GetAuthenticatedUserAsync), bu servis
/// yalnızca cache/DB yükleme mantığını taşır.
/// </summary>
public interface IUserProfileProvider
{
    Task<UserProfileDto> GetAsync(string keycloakId, CancellationToken ct = default);
}
