using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Foundation.Localization;
using Microsoft.EntityFrameworkCore;

namespace BadgeService.Services;

/// <summary>
/// <see cref="IUserLocaleResolver"/>'ın BadgeDbContext üzerinden çalışan uygulaması (issue #185).
/// Önce <c>UserId</c> ile arar (asıl anahtar); event/entity'de UserId bilinmiyorsa (ör. eski
/// event'lerde olabilecek boş alan) <c>KeycloakId</c>'ye düşer. İkisi de bulunamazsa varsayılan
/// dile (tr) döner — auth-api'deki <c>User.PreferredLocale</c> default'uyla tutarlı, backfill
/// gerekmez.
/// </summary>
public class UserLocaleResolver : IUserLocaleResolver
{
    private readonly BadgeDbContext _db;

    public UserLocaleResolver(BadgeDbContext db)
    {
        _db = db;
    }

    public async Task<CultureInfo> ResolveAsync(int userId, string? keycloakId, CancellationToken ct = default)
    {
        Entities.UserLocalePreference? pref = null;

        if (userId > 0)
        {
            pref = await _db.UserLocalePreferences
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.UserId == userId, ct);
        }

        if (pref is null && !string.IsNullOrWhiteSpace(keycloakId))
        {
            pref = await _db.UserLocalePreferences
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.KeycloakId == keycloakId, ct);
        }

        var cultureName = pref is null
            ? SupportedLocales.DefaultCultureName
            : SupportedLocales.ToCultureName(pref.Locale);

        return CultureInfo.GetCultureInfo(cultureName);
    }
}
