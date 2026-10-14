using System.Net;
using Microsoft.AspNetCore.Http;

namespace ExamApp.Api.Helpers;

/// <summary>
/// Issue #438: kayıt amaçlı (rıza izi) istemci IP'si. Gateway (<c>Services/Gateway/Program.cs</c>) <c>X-Forwarded-For</c>'u kendi
/// gördüğü uzak adresle ÜZERİNE YAZAR (append etmez); bu yüzden başlığın ilk değeri gateway'den gelen istekte gerçek istemcidir.
/// Başlık yoksa / IP değilse bağlantı adresi. Yetki ya da rate limit kararında KULLANILMAZ (exam API'ye gateway dışından gelen
/// istek başlığı sahteleyebilir) — yalnız denetim kaydı.
/// </summary>
public static class ClientIpAddress
{
    public const int MaxLength = 45;

    public static string? Resolve(HttpContext? context)
    {
        if (context == null)
            return null;

        var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
        if (!string.IsNullOrWhiteSpace(forwarded))
        {
            var first = forwarded.Split(',')[0].Trim();
            if (IPAddress.TryParse(first, out var parsed))
                return Normalize(parsed);
        }

        return context.Connection.RemoteIpAddress is { } remote ? Normalize(remote) : null;
    }

    private static string? Normalize(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6)
            ip = ip.MapToIPv4();
        var text = ip.ToString();
        return text.Length <= MaxLength ? text : null;
    }
}
