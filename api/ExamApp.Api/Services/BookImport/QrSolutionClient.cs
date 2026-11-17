using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace ExamApp.Api.Services.BookImport;

/// <summary>
/// #1219: QR bağlantısındaki yayıncı sayfasını ve çözüm dosyalarını çeker. QR içeriği güvenilmez girdidir: yalnız okuyucusu olan
/// alan adlarına gidilir (yönlendirmelerde de her adım denetlenir), iç ağ / loopback adreslerine bağlanılmaz (DNS çözümünden
/// sonra, bağlantı anında), yanıt boyutu ve süresi sınırlıdır.
/// </summary>
public sealed class QrSolutionClient(IHttpClientFactory httpClientFactory)
{
    public const string HttpClientName = "qr-solutions";
    private const int MaxRedirects = 5;

    /// <summary>Yanıt gövdesi; alan adı izinli değilse, boyut aşılırsa ya da istek başarısızsa <see cref="QrFetchException"/>.</summary>
    public async Task<byte[]> GetAsync(Uri uri, Func<Uri, bool> allowed, int maxBytes, CancellationToken ct)
    {
        var client = httpClientFactory.CreateClient(HttpClientName);
        for (var hop = 0; hop <= MaxRedirects; hop++)
        {
            if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps || !allowed(uri))
                throw new QrFetchException($"Host not allowed: {uri.Host}");
            using var response = await client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, ct);
            if ((int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location)
            {
                uri = location.IsAbsoluteUri ? location : new Uri(uri, location);
                continue;
            }

            if (!response.IsSuccessStatusCode)
                throw new QrFetchException($"HTTP {(int)response.StatusCode} from {uri.Host}");
            if (response.Content.Headers.ContentLength > maxBytes)
                throw new QrFetchException($"Response too large from {uri.Host}");
            await using var body = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await body.ReadAsync(chunk, ct)) > 0)
            {
                if (buffer.Length + read > maxBytes)
                    throw new QrFetchException($"Response too large from {uri.Host}");
                buffer.Write(chunk, 0, read);
            }

            return buffer.ToArray();
        }

        throw new QrFetchException("Too many redirects");
    }

    /// <summary>Yönlendirmeyi kendimiz izleriz (her adımın alan adı denetlensin); bağlantı yalnız genel adreslere.</summary>
    public static SocketsHttpHandler CreateHandler() => new()
    {
        AllowAutoRedirect = false,
        ConnectTimeout = TimeSpan.FromSeconds(10),
        ConnectCallback = async (context, ct) =>
        {
            var addresses = await Dns.GetHostAddressesAsync(context.DnsEndPoint.Host, ct);
            var address = addresses.FirstOrDefault(IsPublic)
                          ?? throw new HttpRequestException($"No public address for {context.DnsEndPoint.Host}");
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, context.DnsEndPoint.Port), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
                throw;
            }
        },
    };

    internal static bool IsPublic(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (IPAddress.IsLoopback(address) || address.Equals(IPAddress.Any) || address.Equals(IPAddress.IPv6Any))
            return false;
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var b = address.GetAddressBytes();
            return !(b[0] == 0 || b[0] == 10 || b[0] == 127
                     || b[0] == 100 && b[1] >= 64 && b[1] <= 127
                     || b[0] == 169 && b[1] == 254
                     || b[0] == 172 && b[1] >= 16 && b[1] <= 31
                     || b[0] == 192 && b[1] == 168
                     || b[0] >= 224);
        }

        return !(address.IsIPv6LinkLocal || address.IsIPv6SiteLocal || address.IsIPv6Multicast
                 || (address.GetAddressBytes()[0] & 0xFE) == 0xFC);
    }
}

public sealed class QrFetchException(string message) : Exception(message);
