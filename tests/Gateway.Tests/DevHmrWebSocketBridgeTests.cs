using System.Net.WebSockets;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace Gateway.Tests;

/// <summary>Development HMR köprüsü: yalnız dev-server'ın vite-hmr bağlantısını taşır, allowlist'i gevşetmez.</summary>
public class DevHmrWebSocketBridgeTests
{
    private static readonly string[] Hmr = [DevHmrWebSocketBridgeExtensions.HmrSubProtocol];

    private static string? Env(string name) => name switch
    {
        "ANGULAR_APP_HOST" => "localhost",
        "ANGULAR_APP_PORT" => "4200",
        "AUTH_UI_HOST" => "localhost",
        "AUTH_UI_PORT" => "4201",
        _ => null,
    };

    [Theory]
    [InlineData("/", "?token=abc", "ws://localhost:4200/?token=abc")]
    [InlineData("/app/", "?token=abc", "ws://localhost:4201/app/?token=abc")]
    public void ResolveTarget_HmrRequest_MapsToDevServer(string path, string query, string expected)
    {
        var target = DevHmrWebSocketBridgeExtensions.ResolveTarget(new PathString(path), new QueryString(query), Hmr, Env);
        Assert.Equal(new Uri(expected), target);
    }

    [Theory]
    [InlineData("/", "?token=abc", false)]   // vite-hmr alt protokolü yok
    [InlineData("/", "", true)]              // token yok
    [InlineData("/app", "?token=abc", true)] // yol tam değil
    [InlineData("/api/exam/", "?token=abc", true)]
    [InlineData("/hub/badges", "?token=abc", true)]
    public void ResolveTarget_OtherRequests_ReturnsNull(string path, string query, bool withHmrProtocol)
    {
        var protocols = withHmrProtocol ? Hmr : ["json"];
        Assert.Null(DevHmrWebSocketBridgeExtensions.ResolveTarget(new PathString(path), new QueryString(query), protocols, Env));
    }

    [Fact]
    public void ResolveTarget_NoEnv_FallsBackToComposeHostNames()
    {
        var target = DevHmrWebSocketBridgeExtensions.ResolveTarget(new PathString("/app/"), new QueryString("?token=t"), Hmr, _ => null);
        Assert.Equal(new Uri("ws://auth-ui:4200/app/?token=t"), target);
    }

    [Fact]
    public async Task HmrUpgrade_IsRelayedBothWaysToDevServer()
    {
        await using var devServer = await StartFakeViteAsync();
        var port = new Uri(devServer.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First()).Port;
        await using var gateway = await StartGatewayAsync(name => name switch
        {
            "ANGULAR_APP_HOST" => "127.0.0.1",
            "ANGULAR_APP_PORT" => port.ToString(),
            _ => null,
        });

        var client = gateway.GetTestServer().CreateWebSocketClient();
        client.SubProtocols.Add(DevHmrWebSocketBridgeExtensions.HmrSubProtocol);
        using var socket = await client.ConnectAsync(new Uri("ws://localhost/?token=abc"), CancellationToken.None);

        Assert.Equal(DevHmrWebSocketBridgeExtensions.HmrSubProtocol, socket.SubProtocol);
        Assert.Equal("connected token=abc", await ReceiveTextAsync(socket));

        await socket.SendAsync(Encoding.UTF8.GetBytes("ping"), WebSocketMessageType.Text, true, CancellationToken.None);
        Assert.Equal("echo:ping", await ReceiveTextAsync(socket));
    }

    [Theory]
    [InlineData("ws://localhost/?token=abc", false)] // HMR alt protokolü yok → allowlist 400
    [InlineData("ws://localhost/ng-cli-ws", true)]   // HMR yolu değil → allowlist 400
    public async Task NonHmrUpgrade_StillRejectedByAllowlist(string url, bool withHmrProtocol)
    {
        await using var gateway = await StartGatewayAsync(Env);
        var client = gateway.GetTestServer().CreateWebSocketClient();
        if (withHmrProtocol)
        {
            client.SubProtocols.Add(DevHmrWebSocketBridgeExtensions.HmrSubProtocol);
        }

        var ex = await Assert.ThrowsAnyAsync<Exception>(() => client.ConnectAsync(new Uri(url), CancellationToken.None));
        Assert.Contains("400", ex.Message);
    }

    private static async Task<WebApplication> StartGatewayAsync(Func<string, string?> env)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        var app = builder.Build();
        app.UseWebSockets();
        app.UseDevHmrWebSocketBridge(env);
        app.UseHubWebSocketAuth();
        app.Run(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });
        await app.StartAsync();
        return app;
    }

    /// <summary>Vite benzeri dev-server: vite-hmr ile kabul eder, açılışta sorguyu bildirir, sonra yankılar.</summary>
    private static async Task<WebApplication> StartFakeViteAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        var app = builder.Build();
        app.UseWebSockets();
        app.Run(async ctx =>
        {
            if (!ctx.WebSockets.IsWebSocketRequest || !ctx.WebSockets.WebSocketRequestedProtocols.Contains(DevHmrWebSocketBridgeExtensions.HmrSubProtocol))
            {
                ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
                return;
            }

            using var ws = await ctx.WebSockets.AcceptWebSocketAsync(DevHmrWebSocketBridgeExtensions.HmrSubProtocol);
            await ws.SendAsync(Encoding.UTF8.GetBytes($"connected token={ctx.Request.Query["token"]}"), WebSocketMessageType.Text, true, CancellationToken.None);
            var buffer = new byte[1024];
            while (true)
            {
                var result = await ws.ReceiveAsync(buffer, CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    return;
                }

                var text = "echo:" + Encoding.UTF8.GetString(buffer, 0, result.Count);
                await ws.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);
            }
        });
        await app.StartAsync();
        return app;
    }

    private static async Task<string> ReceiveTextAsync(WebSocket socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var buffer = new byte[1024];
        var result = await socket.ReceiveAsync(buffer, timeout.Token);
        return Encoding.UTF8.GetString(buffer, 0, result.Count);
    }
}
