using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;

namespace ExamApp.Api.Services;

/// <summary>
/// issue #189: BaseController.GetAuthenticatedUserAsync ve AuthController.RefreshProfileInformation
/// tarafından ortak kullanılan profil yükleme deseni. Cache miss'te auth-api'den profili alır ve
/// SchoolId'yi ISchoolContextResolver ile DB'den doldurup öyle cache'ler.
///
/// security review O1: auth-api profili İLETİLEN token'ın sahibine göre döner; cache anahtarı ise doğrulanmış
/// <c>sub</c>'dır. İkisi ayrışırsa (başka kullanıcının token'ı iletildi) profil başka bir kullanıcının anahtarına
/// yazılmaz — <see cref="UserProfileSubjectMismatchException"/> fırlatılır.
/// </summary>
public class UserProfileProvider : IUserProfileProvider
{
    private readonly UserProfileCacheService _cacheService;
    private readonly IAuthApiClient _authApiClient;
    private readonly ISchoolContextResolver _schoolContextResolver;
    private readonly ILogger<UserProfileProvider>? _logger;

    public UserProfileProvider(
        UserProfileCacheService cacheService,
        IAuthApiClient authApiClient,
        ISchoolContextResolver schoolContextResolver,
        ILogger<UserProfileProvider>? logger = null)
    {
        _cacheService = cacheService;
        _authApiClient = authApiClient;
        _schoolContextResolver = schoolContextResolver;
        _logger = logger;
    }

    public async Task<UserProfileDto> GetAsync(string keycloakId, CancellationToken ct = default)
    {
        return await _cacheService.GetOrSetAsync(keycloakId, async () =>
        {
            var profile = await _authApiClient.GetUserProfileAsync(ct);
            if (profile is null)
                return profile;

            if (!string.Equals(profile.KeycloakId, keycloakId, StringComparison.Ordinal))
            {
                // Token loglanmaz; yalnızca iki sub.
                _logger?.LogWarning(
                    "auth-api profil sub'ı doğrulanan sub ile uyuşmuyor; profil cache'lenmedi. ExpectedSub={ExpectedSub}, ProfileSub={ProfileSub}",
                    keycloakId, profile.KeycloakId);
                throw new UserProfileSubjectMismatchException();
            }

            profile.SchoolId = await _schoolContextResolver.ResolveSchoolIdAsync(profile, ct);
            return profile;
        });
    }
}

/// <summary>auth-api'nin döndürdüğü profil, doğrulanan kimliğe (<c>sub</c>) ait değil. Profil cache'e yazılmaz.</summary>
public sealed class UserProfileSubjectMismatchException()
    : Exception("User profile returned by auth-api does not belong to the authenticated subject.");
