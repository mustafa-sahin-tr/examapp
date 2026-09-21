using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;

namespace ExamApp.Api.Services;

/// <summary>
/// issue #189: BaseController.GetAuthenticatedUserAsync ve AuthController.RefreshProfileInformation
/// tarafından ortak kullanılan profil yükleme deseni. Cache miss'te auth-api'den profili alır ve
/// SchoolId'yi ISchoolContextResolver ile DB'den doldurup öyle cache'ler.
/// </summary>
public class UserProfileProvider : IUserProfileProvider
{
    private readonly UserProfileCacheService _cacheService;
    private readonly IAuthApiClient _authApiClient;
    private readonly ISchoolContextResolver _schoolContextResolver;

    public UserProfileProvider(
        UserProfileCacheService cacheService,
        IAuthApiClient authApiClient,
        ISchoolContextResolver schoolContextResolver)
    {
        _cacheService = cacheService;
        _authApiClient = authApiClient;
        _schoolContextResolver = schoolContextResolver;
    }

    public async Task<UserProfileDto> GetAsync(string keycloakId, CancellationToken ct = default)
    {
        return await _cacheService.GetOrSetAsync(keycloakId, async () =>
        {
            var profile = await _authApiClient.GetUserProfileAsync();
            if (profile is null)
                return profile;

            profile.SchoolId = await _schoolContextResolver.ResolveSchoolIdAsync(profile, ct);
            return profile;
        });
    }
}
