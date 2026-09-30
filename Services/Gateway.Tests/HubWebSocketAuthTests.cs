using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Net.WebSockets;

namespace Gateway.Tests;

public class HubWebSocketAuthTests
{
    private const string ValidToken = "valid";

    private static async Task<HttpClient> CreateClientAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, FakeBearerHandler>("Bearer", _ => { });
        var app = builder.Build();

        // TestServer gerçek bir upgrade üretmez; "X-Test-WebSocket" başlığını WS isteği olarak işaretle.
        app.Use((ctx, next) =>
        {
            if (ctx.Request.Headers.ContainsKey("X-Test-WebSocket"))
                ctx.Features.Set<IHttpWebSocketFeature>(new FakeWebSocketFeature());
            return next();
        });
        app.UseHubWebSocketAuth();
        app.Run(ctx =>
        {
            ctx.Response.StatusCode = StatusCodes.Status200OK;
            return Task.CompletedTask;
        });

        await app.StartAsync();
        return app.GetTestClient();
    }

    private static HttpRequestMessage Request(string path, bool ws, string? token = null)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, path);
        if (ws) req.Headers.Add("X-Test-WebSocket", "1");
        if (token is not null) req.Headers.Add("X-Test-Token", token);
        return req;
    }

    [Theory]
    [InlineData("/hub/whiteboard")]
    [InlineData("/hub/badges")]
    public async Task HubUpgrade_WithoutToken_Returns401WithChallenge(string path)
    {
        using var client = await CreateClientAsync();
        var res = await client.SendAsync(Request(path, ws: true));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Contains(res.Headers.WwwAuthenticate, h => h.Scheme == "Bearer");
    }

    [Fact]
    public async Task HubUpgrade_WithInvalidToken_Returns401()
    {
        using var client = await CreateClientAsync();
        var res = await client.SendAsync(Request("/hub/badges", ws: true, token: "garbage"));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task HubUpgrade_WithValidToken_PassesThrough()
    {
        using var client = await CreateClientAsync();
        var res = await client.SendAsync(Request("/hub/whiteboard", ws: true, token: ValidToken));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Theory]
    [InlineData("/api/exam")]
    [InlineData("/ng-cli-ws")]
    [InlineData("/hubx")]
    public async Task NonHubUpgrade_Returns400_EvenWithValidToken(string path)
    {
        using var client = await CreateClientAsync();
        var res = await client.SendAsync(Request(path, ws: true, token: ValidToken));
        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task NonWebSocketRequest_IsNotAffected()
    {
        using var client = await CreateClientAsync();
        var res = await client.SendAsync(Request("/api/exam", ws: false));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    private sealed class FakeWebSocketFeature : IHttpWebSocketFeature
    {
        public bool IsWebSocketRequest => true;
        public Task<WebSocket> AcceptAsync(WebSocketAcceptContext context) => throw new NotSupportedException();
    }

    private sealed class FakeBearerHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers["X-Test-Token"] != ValidToken)
                return Task.FromResult(AuthenticateResult.NoResult());

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.Name, "u")], "Bearer");
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), "Bearer")));
        }

        protected override Task HandleChallengeAsync(AuthenticationProperties properties)
        {
            Response.StatusCode = StatusCodes.Status401Unauthorized;
            Response.Headers.WWWAuthenticate = "Bearer";
            return Task.CompletedTask;
        }
    }
}
