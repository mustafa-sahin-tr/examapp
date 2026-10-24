using System.Net.WebSockets;

/// <summary>
/// Yalnız Development: Angular dev-server'ın (Vite) HMR WebSocket'ini gateway üzerinden geçirir.
/// Tarayıcı UI'ı gateway'den (:5678) açtığı için HMR istemcisi <c>ws://localhost:5678/?token=…</c> (ui) ve
/// <c>/app/?token=…</c> (auth-ui) adreslerine bağlanır; Ocelot bu upgrade'i HTTP catch-all route'una düşürüp
/// iletemiyor, <see cref="HubWebSocketAuthExtensions"/> da (doğru olarak) reddediyordu — konsolda 400 ve canlı yenileme yok.
/// Bu köprü <see cref="HubWebSocketAuthExtensions"/>'tan ÖNCE çalışır ve yalnız şu isteği alır: WebSocket, yol tam
/// <c>/</c> ya da <c>/app/</c>, <c>vite-hmr</c> alt protokolü ve <c>token</c> sorgusu. Diğer her şey eskisi gibi
/// allowlist'e gider. Token'ı Vite kendisi doğrular (köprü yalnız taşır). Production'da hiç eklenmez (prod UI statik nginx).
/// </summary>
public static class DevHmrWebSocketBridgeExtensions
{
    public const string HmrSubProtocol = "vite-hmr";

    /// <summary>İstek HMR bağlantısıysa dev-server'daki karşılığı (ws://host:port/yol?sorgu); değilse null.</summary>
    public static Uri? ResolveTarget(
        PathString path, QueryString query, IEnumerable<string> subProtocols, Func<string, string?> env)
    {
        if (!subProtocols.Contains(HmrSubProtocol) || !query.HasValue || !query.Value!.Contains("token=", StringComparison.Ordinal))
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

            using var upstream = new ClientWebSocket();
            upstream.Options.AddSubProtocol(HmrSubProtocol);
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
            await Task.WhenAny(Pump(downstream, upstream, cts.Token), Pump(upstream, downstream, cts.Token));
            await cts.CancelAsync();
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
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException)
        {
            // Bir taraf koptu; diğer pompa iptal edilir.
        }
    }
}
