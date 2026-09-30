using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.Helpers;

/// <summary>
/// Çağıran kullanıcının (auth-api'ye kendi adına iletilecek) erişim token'ını DOĞRULANMIŞ istekten çözer.
///
/// SignalR WebSocket bağlantısı <c>Authorization</c> header'ı taşıyamaz; token query string ile gelir ve yalnızca
/// JwtBearer'ın <c>OnMessageReceived</c>'i (whiteboard hub yolu, header yokken — <see cref="SignalRQueryToken"/>) onu
/// kabul eder. Sıra (security review O1 — iletilen token, kimliği belirleyen token olmalı):
/// <list type="number">
/// <item>Varsayılan authenticate şemasının sakladığı token (<c>JwtBearerOptions.SaveToken</c>): JwtBearer'ın doğrulayıp
/// <c>HttpContext.User</c>'ı ürettiği token'ın kendisi.</item>
/// <item>Yedek: kimlik JwtBearer'dan geldiyse ama token saklanmadıysa <c>Authorization: Bearer</c> header'ı (JwtBearer
/// header ile query'yi aynı anda kabul etmediği için doğrulanan token budur).</item>
/// </list>
/// Kimlik JwtBearer dışından geliyorsa (ör. <c>/hangfire</c> cookie'si) ya da istek kimliksizse null — header'daki
/// doğrulanmamış bir token iletilmez. Token değeri hiçbir yerde loglanmaz.
/// </summary>
public static class CallerAccessToken
{
    private const string BearerPrefix = "Bearer ";

    /// <summary>Token (şema öneki olmadan) ya da bulunamazsa null.</summary>
    public static async Task<string?> ResolveAsync(HttpContext? httpContext)
    {
        // HttpContext yoksa (arka plan işi) ya da authentication servisi yoksa doğrulanmış kimlik de yoktur.
        if (httpContext?.RequestServices?.GetService<IAuthenticationService>() is null)
            return null;

        // Varsayılan şema (policy scheme → JwtBearer / HangfireCookie); sonuç istek başına handler'da önbelleklidir.
        var result = await httpContext.AuthenticateAsync();
        if (!result.Succeeded || result.Ticket is null)
            return null;

        var saved = result.Properties?.GetTokenValue("access_token");
        if (!string.IsNullOrEmpty(saved))
            return saved;

        if (!string.Equals(result.Ticket.AuthenticationScheme, JwtBearerDefaults.AuthenticationScheme, StringComparison.Ordinal))
            return null;

        string? header = httpContext.Request.Headers.Authorization;
        if (string.IsNullOrEmpty(header) || !header.StartsWith(BearerPrefix, StringComparison.OrdinalIgnoreCase))
            return null;

        var fromHeader = header[BearerPrefix.Length..].Trim();
        return fromHeader.Length > 0 ? fromHeader : null;
    }
}

/// <summary>
/// Kullanıcı adına auth-api çağrısı yapılacak ama doğrulanmış istekte (JwtBearer) erişim token'ı yok. auth-api'ye
/// token'sız gidip 401 → 500 almak yerine açık hata. HTTP isteği / SignalR hub bağlamı DIŞINDA (Hangfire işi, CLI,
/// consumer — HttpContext yok) ya da cookie ile kimliklenen istekte (<c>/hangfire</c>) HER ZAMAN fırlatılır: kullanıcı
/// profili oralarda <c>IUserProfileProvider</c> ile cache miss'te yüklenemez.
/// Eşleme: BaseController bunu <see cref="UserProfileUnavailableException"/>'a sarar ve
/// <see cref="UserProfileUnavailableFilterAttribute"/> doğrudan da tanır → 503; ApprovedTeacher policy'si → 403.
/// </summary>
public sealed class CallerAccessTokenMissingException()
    : Exception("Caller access token is not available on the current request; cannot call auth-api on behalf of the user.");
