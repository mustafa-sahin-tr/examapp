using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;

namespace ExamApp.Api.Helpers;

/// <summary>
/// Issue #440 review (security MINOR2): rate limit anahtarı için istemci IP'si. <see cref="ClientIpAddress"/> (rıza izi) ilk
/// <c>X-Forwarded-For</c> değerine koşulsuz güvenir — rate limit için yeterli değil: exam API'ye gateway'i atlayarak gelen istek başlığı
/// sahteleyip her istekte yeni kova açabilirdi. Kural (gateway'in <c>UseTrustedForwardedFor</c> deseni):
/// <list type="bullet">
/// <item>Bağlantı adresi GÜVENİLİR proxy ise (<c>ForwardedHeaders:KnownProxies</c> / <c>KnownNetworks</c> — gateway ile aynı anahtarlar;
/// tanımlı değilse YALNIZ loopback — Aspire'da gateway API'ye localhost'tan gelir) <c>X-Forwarded-For</c>'un SON değeri (en yakın proxy'nin
/// eklediği; gateway başlığı zaten tek değerle ezer) kullanılır. docker-compose / k8s'te gateway'in ağı <c>KnownNetworks</c> ile
/// tanımlanmalı (bkz. docker-compose.yml, deploy/gcp/k8s/apps.yaml); tanımlanmazsa tüm istekler gateway IP'sinin kovasını paylaşır.</item>
/// <item>Aksi halde bağlantı adresi — başlık yok sayılır.</item>
/// </list>
/// IPv4-mapped IPv6 → IPv4; IPv6 /64 önekine indirgenir (gateway <c>ClientKey</c> ile aynı).
/// </summary>
public static class TrustedClientIp
{
    public static string Resolve(HttpContext context, IConfiguration? configuration)
    {
        var remote = context.Connection.RemoteIpAddress;
        if (remote != null && IsTrustedProxy(remote, configuration))
        {
            var forwarded = context.Request.Headers["X-Forwarded-For"].ToString();
            var last = forwarded.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).LastOrDefault();
            if (last != null && IPAddress.TryParse(last, out var parsed))
                return Key(parsed);
        }
        return remote == null ? "unknown" : Key(remote);
    }

    /// <summary>
    /// Security re-review r3 M1: Development dışında açılışta çağrılır. <c>ForwardedHeaders:KnownProxies</c> ve <c>KnownNetworks</c> ikisi de
    /// boşsa (tüm aktivasyon trafiği gateway IP'sinin tek kovasını paylaşırdı) ya da bir değer IP/CIDR olarak çözülemiyorsa (ör. k8s
    /// <c>REPLACE_POD_CIDR</c> yer tutucusu) <see cref="InvalidOperationException"/>.
    /// </summary>
    public static void ValidateTrustedProxyConfiguration(IConfiguration configuration)
    {
        var proxies = configuration.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
        var networks = configuration.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [];
        if (proxies.Length == 0 && networks.Length == 0)
            throw new InvalidOperationException(
                "ForwardedHeaders:KnownProxies / KnownNetworks tanımlı değil. Development dışında gateway'in adresi/ağı zorunludur " +
                "(ForwardedHeaders__KnownProxies__0 / ForwardedHeaders__KnownNetworks__0) — aksi halde istemci IP'si çözülemez.");
        foreach (var p in proxies)
        {
            if (!IPAddress.TryParse(p?.Trim(), out _))
                throw new InvalidOperationException($"ForwardedHeaders:KnownProxies geçerli bir IP değil: '{p}'.");
        }
        foreach (var n in networks)
        {
            if (!System.Net.IPNetwork.TryParse(n?.Trim(), out var net))
                throw new InvalidOperationException($"ForwardedHeaders:KnownNetworks geçerli bir CIDR değil: '{n}'.");
            if (net.PrefixLength == 0)
                throw new InvalidOperationException("ForwardedHeaders:KnownNetworks 0.0.0.0/0 ya da ::/0 olamaz (herkese güvenirdi).");
        }
    }

    internal static bool IsTrustedProxy(IPAddress remote, IConfiguration? configuration)
    {
        if (remote.IsIPv4MappedToIPv6)
            remote = remote.MapToIPv4();
        var proxies = configuration?.GetSection("ForwardedHeaders:KnownProxies").Get<string[]>() ?? [];
        var networks = configuration?.GetSection("ForwardedHeaders:KnownNetworks").Get<string[]>() ?? [];
        if (proxies.Length > 0 || networks.Length > 0)
        {
            return proxies.Any(p => IPAddress.TryParse(p.Trim(), out var ip) && ip.Equals(remote))
                   || networks.Any(n => System.Net.IPNetwork.TryParse(n.Trim(), out var net) && net.Contains(remote));
        }
        // Security re-review MINOR: varsayılan yalnız loopback (özel ağ adresleri aynı ağdaki herhangi bir konteyner olabilir).
        return IPAddress.IsLoopback(remote);
    }

    private static string Key(IPAddress ip)
    {
        if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
        if (ip.AddressFamily != AddressFamily.InterNetworkV6) return ip.ToString();
        var bytes = ip.GetAddressBytes();
        Array.Clear(bytes, 8, 8);
        return new IPAddress(bytes) + "/64";
    }
}
