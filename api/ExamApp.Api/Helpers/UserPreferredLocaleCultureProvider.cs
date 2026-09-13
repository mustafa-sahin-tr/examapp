using System.Security.Claims;
using ExamApp.Api.Models.Dtos;
using ExamApp.Foundation.Localization;
using Microsoft.AspNetCore.Localization;

namespace ExamApp.Api.Helpers;

/// <summary>
/// İsteğin kültürünü, oturum açmış kullanıcının kayıtlı dil tercihinden
/// (<c>UserProfileDto.PreferredLocale</c>, issue #181) belirler.
///
/// Kasıtlı olarak SADECE Redis'teki profil önbelleğine bakar: cache miss'te auth-api'ye
/// HTTP çağrısı yapmaz, <c>null</c> döner ve sıradaki provider'a düşülür. Aksi hâlde her
/// istek, sırf dil çözümlemek için servisler arası senkron bir çağrı doğururdu.
///
/// Bağımlılık <see cref="HttpContext.RequestServices"/> üzerinden istek başına çözülür —
/// provider singleton bir <see cref="RequestLocalizationOptions"/> nesnesinde tutulduğu için
/// scoped servisi constructor'da yakalamak (captive dependency) yanlış olurdu.
/// </summary>
public class UserPreferredLocaleCultureProvider : RequestCultureProvider
{
    public override async Task<ProviderCultureResult?> DetermineProviderCultureResult(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        if (httpContext.User?.Identity?.IsAuthenticated != true)
        {
            return null;
        }

        var sub = httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(sub))
        {
            return null;
        }

        var cache = httpContext.RequestServices.GetService<UserProfileCacheService>();
        if (cache is null)
        {
            return null;
        }

        UserProfileDto? profile;
        try
        {
            profile = await cache.GetAsync(sub);
        }
        catch (Exception)
        {
            // Redis erişilemiyorsa dil çözümlemesi isteği düşürmemeli — varsayılana düşülür.
            return null;
        }

        if (profile is null || !SupportedLocales.TryNormalize(profile.PreferredLocale, out var locale))
        {
            return null;
        }

        var cultureName = SupportedLocales.ToCultureName(locale);
        return new ProviderCultureResult(cultureName, cultureName);
    }
}
