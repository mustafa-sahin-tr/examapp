using Microsoft.AspNetCore.Http;

namespace BadgeService.Security;

/// <summary>
/// Token'daki Keycloak <c>sub</c> değerini, BadgeService verisinde kullanılan auth-api sayısal
/// <c>User.Id</c>'sine çevirir. BadgeService'te sub→id eşlemesi tutulmadığı için çözüm auth-api'ye
/// sorularak yapılır (bkz. <see cref="AuthApiCallerIdentityResolver"/>).
///
/// IDOR koruması (issue #165) bu değere dayanır: çözümlenemeyen bir çağıran <c>null</c> döner ve
/// controller 403 verir — <c>null</c> asla "kontrolü atla" anlamına gelmez.
/// </summary>
public interface ICallerIdentityResolver
{
    /// <summary>
    /// Çağıranın sayısal user id'sini döner; token'da <c>sub</c> yoksa, auth-api'ye ulaşılamıyorsa
    /// veya yanıt tutarsızsa <c>null</c>. Hiçbir durumda exception fırlatmaz.
    /// </summary>
    Task<int?> ResolveUserIdAsync(HttpContext httpContext, CancellationToken ct);
}
