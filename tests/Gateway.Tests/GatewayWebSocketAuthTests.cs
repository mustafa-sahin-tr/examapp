using System.IdentityModel.Tokens.Jwt;
using System.Net.WebSockets;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace Gateway.Tests;

[CollectionDefinition("Gateway", DisableParallelization = true)]
public sealed class GatewayCollection;

/// <summary>
/// Issue #311 (L6): Ocelot's WebSocket branch (UseWebSockets=true) does not run AuthenticationOptions,
/// so Program.cs puts UseHubWebSocketAuth in front of it. These tests drive the REAL gateway pipeline
/// (Program.cs + Ocelot + JwtBearer) in-process and proxy to a fake downstream hub on a real Kestrel port.
/// </summary>
[Collection("Gateway")]
public sealed class GatewayWebSocketAuthTests : IClassFixture<GatewayWebSocketAuthTests.GatewayFactory>
{
    // SignalR sends the bearer in this query parameter on WebSocket upgrades.
    private const string TokenParam = "access" + "_token";

    private readonly GatewayFactory _factory;

    public GatewayWebSocketAuthTests(GatewayFactory factory) => _factory = factory;

    private static Uri Url(string path, string? token = null) =>
        new("ws://localhost" + path + (token is null ? "" : "?" + TokenParam + "=" + token));

    [Theory]
    [InlineData("/hub/whiteboard")]
    [InlineData("/hub/badges")]
    public async Task Upgrade_WithoutToken_IsRejected401_AndNeverReachesDownstream(string path)
    {
        var before = _factory.DownstreamConnections;
        var client = _factory.Server.CreateWebSocketClient();

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => client.ConnectAsync(Url(path), CancellationToken.None));

        ex.Message.ShouldContain("401");
        _factory.DownstreamConnections.ShouldBe(before);
    }

    [Theory]
    [InlineData("/hub/whiteboard")]
    [InlineData("/hub/badges")]
    public async Task Upgrade_WithGarbageToken_IsRejected401(string path)
    {
        var client = _factory.Server.CreateWebSocketClient();

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => client.ConnectAsync(Url(path, "not.a.jwt"), CancellationToken.None));

        ex.Message.ShouldContain("401");
    }

    [Theory]
    [InlineData("/hub/whiteboard")]
    [InlineData("/hub/badges")]
    public async Task Upgrade_WithValidQueryToken_IsProxiedToDownstream(string path)
    {
        var before = _factory.DownstreamConnections;
        var client = _factory.Server.CreateWebSocketClient();

        using var ws = await client.ConnectAsync(Url(path, _factory.CreateToken()), CancellationToken.None);

        ws.State.ShouldBe(WebSocketState.Open);
        await ws.SendAsync(Encoding.UTF8.GetBytes("ping"), WebSocketMessageType.Text, true, CancellationToken.None);
        var buf = new byte[16];
        var r = await ws.ReceiveAsync(buf, CancellationToken.None);
        Encoding.UTF8.GetString(buf, 0, r.Count).ShouldBe("echo:ping");
        _factory.DownstreamConnections.ShouldBe(before + 1);
    }

    [Fact]
    public async Task Upgrade_OnUnlistedHubPath_IsRejected400()
    {
        var client = _factory.Server.CreateWebSocketClient();

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => client.ConnectAsync(Url("/hub/other", _factory.CreateToken()), CancellationToken.None));

        ex.Message.ShouldContain("400");
    }

    public static IEnumerable<object[]> OcelotFiles() =>
        new[] { "ocelot.json", "ocelot.Development.json", "ocelot.Production.json" }.Select(f => new object[] { f });

    [Theory]
    [MemberData(nameof(OcelotFiles))]
    public void EveryWebSocketRoute_InRealOcelotFiles_CarriesBearerAuthenticationOptions(string file)
    {
        var path = Path.Combine(GatewayFactory.FindGatewayDir(), file);
        var routes = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!["Routes"]!.AsArray();

        var wsRoutes = routes.Where(r => r!["UseWebSockets"]?.GetValue<bool>() == true).ToList();
        wsRoutes.ShouldNotBeEmpty();
        foreach (var r in wsRoutes)
            r!["AuthenticationOptions"]?["AuthenticationProviderKey"]?.GetValue<string>()
                .ShouldBe("Bearer", $"{file} {r["UpstreamPathTemplate"]}");
    }

    [Fact]
    public async Task Upgrade_OnNonHubPath_IsRejected400()
    {
        var client = _factory.Server.CreateWebSocketClient();

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => client.ConnectAsync(Url("/exam-api/anything", _factory.CreateToken()), CancellationToken.None));

        ex.Message.ShouldContain("400");
    }

    public sealed class GatewayFactory : WebApplicationFactory<Program>, IAsyncLifetime
    {
        private const string Issuer = "http://localhost:5678/realms/exam-realm"; // appsettings.json Server:BaseUrl + Keycloak:Realm
        private readonly SymmetricSecurityKey _key = new(Encoding.UTF8.GetBytes("gateway-tests-signing-key-0123456789-abcdef"));
        private readonly WebApplication _downstream;
        private int _connections;
        private readonly string _oldCwd = Directory.GetCurrentDirectory();
        private readonly string _tempDir;

        public int DownstreamConnections => Volatile.Read(ref _connections);

        public GatewayFactory()
        {
            _downstream = BuildDownstream();
            var port = new Uri(_downstream.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.First()).Port;

            // Program.cs reads ocelot.{env}.json / ocelot.json relative to the CWD.
            _tempDir = Directory.CreateTempSubdirectory("gateway-tests-").FullName;
            File.WriteAllText(Path.Combine(_tempDir, "ocelot.json"), OcelotJson(port));
            Directory.SetCurrentDirectory(_tempDir);
        }

        public static string FindGatewayDir()
        {
            var d = new DirectoryInfo(AppContext.BaseDirectory);
            while (d is not null && !File.Exists(Path.Combine(d.FullName, "ExamApp.slnx"))) d = d.Parent;
            return d is null
                ? throw new DirectoryNotFoundException("ExamApp.slnx not found above " + AppContext.BaseDirectory)
                : Path.Combine(d.FullName, "Services", "Gateway");
        }

        public ValueTask InitializeAsync() => ValueTask.CompletedTask;

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await _downstream.DisposeAsync();
            Directory.SetCurrentDirectory(_oldCwd);
            try { Directory.Delete(_tempDir, true); } catch (IOException) { }
        }

        private WebApplication BuildDownstream()
        {
            var b = WebApplication.CreateBuilder();
            b.WebHost.UseUrls("http://127.0.0.1:0");
            var app = b.Build();
            app.UseWebSockets();
            app.Map("/hub/{name}", async ctx =>
            {
                if (!ctx.WebSockets.IsWebSocketRequest) { ctx.Response.StatusCode = 400; return; }
                using var ws = await ctx.WebSockets.AcceptWebSocketAsync();
                Interlocked.Increment(ref _connections);
                var buf = new byte[256];
                var r = await ws.ReceiveAsync(buf, ctx.RequestAborted);
                var reply = Encoding.UTF8.GetBytes("echo:" + Encoding.UTF8.GetString(buf, 0, r.Count));
                await ws.SendAsync(reply, WebSocketMessageType.Text, true, ctx.RequestAborted);
                await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
            });
            app.Start();
            return app;
        }

        private static string OcelotJson(int port) => $$"""
        {
          "Routes": [
            { "DownstreamPathTemplate": "/hub/whiteboard", "DownstreamScheme": "ws",
              "DownstreamHostAndPorts": [ { "Host": "127.0.0.1", "Port": {{port}} } ],
              "UpstreamPathTemplate": "/hub/whiteboard", "UpstreamHttpMethod": [ "GET" ],
              "UseWebSockets": true, "AuthenticationOptions": { "AuthenticationProviderKey": "Bearer" } },
            { "DownstreamPathTemplate": "/hub/badges", "DownstreamScheme": "ws",
              "DownstreamHostAndPorts": [ { "Host": "127.0.0.1", "Port": {{port}} } ],
              "UpstreamPathTemplate": "/hub/badges", "UpstreamHttpMethod": [ "GET" ],
              "UseWebSockets": true, "AuthenticationOptions": { "AuthenticationProviderKey": "Bearer" } }
          ],
          "GlobalConfiguration": { "BaseUrl": "http://localhost" }
        }
        """;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseContentRoot(FindGatewayDir());
            builder.ConfigureServices(services =>
            {
                // No Keycloak in tests: validate against a local signing key, same issuer/audience as Program.cs.
                services.PostConfigure<JwtBearerOptions>("Bearer", o =>
                {
                    o.MetadataAddress = null!;
                    o.Authority = null;
                    o.Configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                    o.Configuration.SigningKeys.Add(_key);
                    o.TokenValidationParameters.IssuerSigningKey = _key;
                    o.TokenValidationParameters.ValidIssuer = Issuer;
                    o.TokenValidationParameters.ValidAudience = "account";
                });
            });
        }

        public string CreateToken()
        {
            var jwt = new JwtSecurityToken(Issuer, "account",
                [new Claim("sub", "user-1")], DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddMinutes(10),
                new SigningCredentials(_key, SecurityAlgorithms.HmacSha256));
            return new JwtSecurityTokenHandler().WriteToken(jwt);
        }
    }
}
