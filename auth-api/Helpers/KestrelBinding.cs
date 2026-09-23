using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace ExamApp.Api.Helpers;

/// <summary>
/// auth-api'nin Kestrel dinleme adresi (issue #100).
///
/// <c>Kestrel:BindLoopbackOnly=true</c> → yalnızca loopback (<c>127.0.0.1</c> + <c>::1</c>) dinlenir.
/// Aspire'da auth-api host process olarak koşar; gateway ve seed CLI ona <c>localhost:6079</c> ile
/// ulaşır, LAN'daki başka bir makine ise ulaşamaz. Böylece gateway'i atlayıp sahte
/// <c>X-Forwarded-For</c> ile IP rate limit'ini (bkz. <see cref="AuthRateLimiting"/>) delmek mümkün olmaz.
///
/// Varsayılan (<c>false</c>) → tüm arayüzler: container'da (docker-compose / k8s) gateway başka bir
/// network namespace'inden bağlandığı için loopback yeterli olmaz. Orada dış erişim port publish
/// (compose: <c>127.0.0.1:6079:5079</c>) ve <c>ForwardedHeaders:KnownNetworks</c> ile kısıtlanır.
///
/// DİKKAT: loopback modunda yalnızca aynı makinedeki host process'ler bağlanabilir. Aspire'a auth-api'ye
/// erişen bir container kaynağı eklenirse (container host'un loopback'ine ulaşamaz) erişim kopar; o
/// durumda <c>BindLoopbackOnly</c> kapatılıp ForwardedHeaders KnownNetworks/KnownProxies ile pinlenmelidir.
/// </summary>
public static class KestrelBinding
{
    public const string PortKey = "Kestrel:Port";
    public const string BindLoopbackOnlyKey = "Kestrel:BindLoopbackOnly";
    public const int DefaultPort = 5079;

    public static int ResolvePort(IConfiguration configuration) =>
        configuration.GetValue(PortKey, DefaultPort);

    public static bool IsLoopbackOnly(IConfiguration configuration) =>
        configuration.GetValue(BindLoopbackOnlyKey, false);

    public static void Configure(KestrelServerOptions options, IConfiguration configuration)
    {
        var port = ResolvePort(configuration);
        if (IsLoopbackOnly(configuration))
            options.ListenLocalhost(port);
        else
            options.ListenAnyIP(port);
    }
}
