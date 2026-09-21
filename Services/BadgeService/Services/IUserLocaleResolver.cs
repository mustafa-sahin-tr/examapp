using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace BadgeService.Services;

/// <summary>
/// Bildirim üretirken hedef kullanıcının dilini çözer (issue #185). Consumer'larda istek
/// bağlamı (Accept-Language, HttpContext) olmadığı için kültür her zaman burada, açıkça
/// parametre olarak istenir/üretilir — hiçbir yerde <see cref="CultureInfo.CurrentUICulture"/>'a
/// ima ile güvenilmez.
/// </summary>
public interface IUserLocaleResolver
{
    /// <summary>
    /// <paramref name="userId"/> (varsa) ya da <paramref name="keycloakId"/> ile
    /// <c>UserLocalePreference</c> tablosuna bakar; kayıt yoksa
    /// <see cref="ExamApp.Foundation.Localization.SupportedLocales.DefaultCultureName"/> döner.
    /// </summary>
    Task<CultureInfo> ResolveAsync(int userId, string? keycloakId, CancellationToken ct = default);
}
