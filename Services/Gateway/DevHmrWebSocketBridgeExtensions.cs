using System.Net.WebSockets;
using Microsoft.AspNetCore.WebUtilities;

/// <summary>
/// Yalnız Development: Angular dev-server'ın (Vite) HMR WebSocket'ini gateway üzerinden geçirir.
/// Tarayıcı UI'ı gateway'den (:5678) açtığı için HMR istemcisi <c>ws://localhost:5678/?token=…</c> (ui) ve
/// <c>/app/?token=…</c> (auth-ui) adreslerine bağlanır; Ocelot bu upgrade'i HTTP catch-all route'una düşürüp
/// iletemiyor, <see cref="HubWebSocketAuthExtensions"/> da (doğru olarak) reddediyordu — konsolda 400 ve canlı yenileme yok.
/// Bu köprü <see cref="HubWebSocketAuthExtensions"/>'tan ÖNCE çalışır ve yalnız şu isteği alır: WebSocket, yol tam
/// <c>/</c> ya da <c>/app/</c>, <c>vite-hmr</c> alt protokolü ve <c>token</c> sorgusu. Diğer her şey eskisi gibi
/// allowlist'e gider. Production'da hiç eklenmez (prod UI statik nginx).
/// <para>
/// Çapraz site koruması (security review M1): Vite token'ı yalnız <c>Origin</c> başlığı olan istekte denetler; köprünün
/// <see cref="ClientWebSocket"/>'i Origin göndermeseydi her bağlantı token'sız kabul edilirdi (CVE-2025-24010 sınıfı). Bu
/// yüzden Origin zorunludur, gateway'in kendi origin'i (aynı scheme + Host) olmalıdır ve dev-server'a aynen iletilir —
/// token denetimini yine Vite yapar.
/// </para>
/// </summary>
public static class DevHmrWebSocketBridgeExtensions
{
    public const string HmrSubProtocol = "vite-hmr";

    /// <summary>İstek HMR bağlantısıysa dev-server'daki karşılığı (ws://host:port/yol?sorgu); değilse null.</summary>
    public static Uri? ResolveTarget(
        PathString path, QueryString query, IEnumerable<string> subProtocols, Func<string, string?> env)
    {
        if (!subProtocols.Contains(HmrSubProtocol) || !QueryHelpers.ParseQuery(query.Value).ContainsKey("token"))
        {
            return null;
        }

        // Varsayılanlar ocelot.Development.json'daki docker-compose host adları; Aspire *_HOST/*_PORT ile ezer.
        var (host, port) = path.Value switch
        {
            "/" => (env("ANGULAR_APP_HOST") ?? "angular-app", env("ANGULAR_APP_PORT") ?? "4200"),
            "/app/" => (env("AUTH_UI_HOST") ?? "auth-ui", env("AUTH_UI_PORT") ?? "4200"),
            _ => (null, null),
        };
        if (host is null || !int.TryParse(port, out var portNumber))
        {
            return null;
        }

        return new UriBuilder("ws", host, portNumber, path.Value) { Query = query.Value![1..] }.Uri;
    }

    /// <summary>Origin gateway'in kendi origin'i mi (tarayıcıdaki sayfa gateway'den açılmış olmalı).</summary>
    public static bool IsSameOrigin(HttpRequest request)
    {
        var origin = request.Headers.Origin.ToString();
        return Uri.TryCreate(origin, UriKind.Absolute, out var parsed)
            && string.Equals(parsed.Scheme, request.Scheme, StringComparison.OrdinalIgnoreCase)
            && string.Equals(parsed.Authority, request.Host.Value, StringComparison.OrdinalIgnoreCase);
    }

    public static IApplicationBuilder UseDevHmrWebSocketBridge(this IApplicationBuilder app, Func<string, string?>? env = null)
    {
        env ??= Environment.GetEnvironmentVariable;
        return app.Use(async (context, next) =>
        {
            var target = context.WebSockets.IsWebSocketRequest
                ? ResolveTarget(context.Request.Path, context.Request.QueryString, context.WebSockets.WebSocketRequestedProtocols, env)
                : null;
            if (target is null)
            {
                await next();
                return;
            }

            if (!IsSameOrigin(context.Request))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            using var upstream = new ClientWebSocket();
            upstream.Options.AddSubProtocol(HmrSubProtocol);
            upstream.Options.SetRequestHeader("Origin", context.Request.Headers.Origin.ToString());
            try
            {
                await upstream.ConnectAsync(target, context.RequestAborted);
            }
            catch (Exception ex) when (ex is WebSocketException or HttpRequestException)
            {
                context.Response.StatusCode = StatusCodes.Status502BadGateway;
                return;
            }

            using var downstream = await context.WebSockets.AcceptWebSocketAsync(HmrSubProtocol);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(context.RequestAborted);
            var toDevServer = Pump(downstream, upstream, cts.Token);
            var toBrowser = Pump(upstream, downstream, cts.Token);
            await Task.WhenAny(toDevServer, toBrowser);
            await cts.CancelAsync();
            // Soketler dispose edilmeden önce iki pompa da bitsin (review: yarışta ObjectDisposedException).
            await Task.WhenAll(toDevServer, toBrowser);
        });
    }

    private static async Task Pump(WebSocket from, WebSocket to, CancellationToken ct)
    {
        var buffer = new byte[16 * 1024];
        try
        {
            while (!ct.IsCancellationRequested)
            {
                var result = await from.ReceiveAsync(buffer, ct);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    if (to.State == WebSocketState.Open)
                    {
                        await to.CloseOutputAsync(result.CloseStatus ?? WebSocketCloseStatus.NormalClosure, result.CloseStatusDescription, ct);
                    }
                    return;
                }

                await to.SendAsync(buffer.AsMemory(0, result.Count), result.MessageType, result.EndOfMessage, ct);
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException)
        {
            // Bir taraf koptu; diğer pompa iptal edilir.
        }
    }
}
