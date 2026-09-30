using Microsoft.AspNetCore.Http;

namespace BadgeService.Security;

/// <summary>
/// SignalR WebSocket upgrade'i Authorization header'ı taşıyamaz; istemci token'ı query string'de gönderir. JwtBearer
/// <c>OnMessageReceived</c>'te query token'ı YALNIZCA verilen hub yolunda ve istekte <c>Authorization</c> header'ı
/// YOKKEN kabul edilir (security review O1): header varken query'yi almak, kimliği query token'ı belirlerken header'daki
/// başka bir token'ın aşağı akışa (auth-api) iletilmesine yol açabilirdi. Exam API'deki aynı adlı yardımcıyla aynı kural.
/// </summary>
public static class SignalRQueryToken
{
    public const string QueryParameter = "access_token";

    /// <summary>Kabul edilecek query token'ı ya da null (JwtBearer header'a düşer).</summary>
    public static string? Resolve(HttpRequest request, PathString hubPath)
    {
        if (!request.Path.StartsWithSegments(hubPath))
            return null;
        if (!string.IsNullOrEmpty(request.Headers.Authorization))
            return null;

        string? token = request.Query[QueryParameter];
        return string.IsNullOrEmpty(token) ? null : token;
    }
}
