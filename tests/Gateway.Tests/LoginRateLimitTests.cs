using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Gateway.Tests;

// Issue #368: giriş uçlarında IP başına 30/dk; GET, refresh ve statik kaynaklar limit dışı.
public class LoginRateLimitTests
{
    private static async Task<HttpClient> CreateClientAsync(int? permit = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.WebHost.UseTestServer();
        if (permit is not null)
            builder.Configuration["RateLimiting:LoginEntry:PermitLimit"] = permit.ToString();
        var app = builder.Build();
        app.UseLoginRateLimit(app.Configuration);
        app.Run(async ctx =>
        {
            // Downstream'in (Ocelot) gövdeyi hâlâ okuyabildiğini doğrular.
            if (ctx.Request.HasFormContentType)
            {
                try { await ctx.Request.ReadFormAsync(); } catch (Exception ex) when (ex is InvalidDataException or IOException) { }
            }
            ctx.Response.StatusCode = StatusCodes.Status200OK;
        });
        await app.StartAsync();
        return app.GetTestClient();
    }

    private static HttpRequestMessage Post(string path, string? form = null) =>
        new(HttpMethod.Post, path)
        {
            Content = new FormUrlEncodedContent(
                (form ?? "grant_type=password&username=u&password=p").Split('&')
                    .Select(p => p.Split('=')).ToDictionary(p => p[0], p => p[1]))
        };

    [Theory]
    [InlineData("/api/auth/login")]
    [InlineData("/token")]
    [InlineData("/realms/exam-realm/login-actions/authenticate")]
    [InlineData("/auth/realms/exam-realm/login-actions/authenticate")]
    [InlineData("/realms/exam-realm/login-actions/reset-credentials")]
    [InlineData("/realms/exam-realm/protocol/openid-connect/token")]
    [InlineData("/realms/exam-realm/login-actions/registration")]
    [InlineData("/realms/exam-realm/login-actions/required-action")]
    [InlineData("/realms/exam-realm/login-actions/action-token")]
    [InlineData("/realms/exam-realm/protocol/openid-connect/token/introspect")]
    [InlineData("/realms/exam-realm/protocol/openid-connect/revoke")]
    [InlineData("/realms/exam-realm/login-actions/authenticate;x=1")]
    [InlineData("/realms/exam-realm;jsessionid=a/login-actions/authenticate")]
    [InlineData("/realms/exam-realm%2Flogin-actions%2Fauthenticate")]
    [InlineData("/realms/exam-realm%5clogin-actions/authenticate")]
    [InlineData("/REALMS/exam-realm/Login-Actions/Authenticate")]
    [InlineData("/realms/exam-realm/login-actions/authenticate/")]
    [InlineData("/Token/")]
    [InlineData("/token;x")]
    [InlineData("/API/Auth/Login")]
    [InlineData("/realms/exam-realm/foo/../login-actions/authenticate")]
    [InlineData("/api/exam/parent-links/redeem")] // issue #419
    [InlineData("/API/Exam/Parent-Links/Redeem/")]
    [InlineData("/api/exam/parent-links;x=1/redeem")]
    [InlineData("/api/exam/parent-links%2Fredeem")]
    [InlineData("/api/exam/parent-links%252Fredeem")] // çift kodlanmış ayraç
    [InlineData("/api/exam/parent-links/%2572edeem")] // çift kodlanmış harf
    [InlineData("/api/v2/parent-links/redeem")]
    public async Task LoginEntry_101stRequestWithinWindow_Returns429WithRetryAfter(string path)
    {
        using var client = await CreateClientAsync();
        for (var i = 0; i < 100; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Post(path))).StatusCode);

        var res = await client.SendAsync(Post(path));
        Assert.Equal(HttpStatusCode.TooManyRequests, res.StatusCode);
        Assert.NotNull(res.Headers.RetryAfter?.Delta);
    }

    [Fact]
    public async Task LoginEntries_ShareOneBudgetPerIp()
    {
        using var client = await CreateClientAsync(permit: 2);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Post("/token"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Post("/api/auth/login"))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests,
            (await client.SendAsync(Post("/realms/exam-realm/login-actions/authenticate"))).StatusCode);
    }

    [Fact]
    public async Task ParentInviteRedeem_SharesTheLoginBudget_OtherParentLinkEndpointsAreNotCounted()
    {
        using var client = await CreateClientAsync(permit: 2);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Post("/api/exam/parent-links/redeem"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Post("/api/auth/login"))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(Post("/api/exam/parent-links/redeem"))).StatusCode);
        // Kod üretme / onay / koparma ve okuma uçları sayılmaz.
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Post("/api/exam/parent-links/invite-code"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Post("/api/exam/parent-links/5/approve"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/exam/parent-links/redeem")).StatusCode);
    }

    [Theory]
    [InlineData("/token")]
    [InlineData("/realms/exam-realm/protocol/openid-connect/token")]
    public async Task RefreshGrant_IsNeverLimited(string path)
    {
        using var client = await CreateClientAsync();
        for (var i = 0; i < 100; i++)
            Assert.Equal(HttpStatusCode.OK,
                (await client.SendAsync(Post(path, "grant_type=refresh_token&refresh_token=x"))).StatusCode);
    }

    [Fact]
    public async Task AuthApiRefreshToken_IsNeverLimited()
    {
        using var client = await CreateClientAsync();
        for (var i = 0; i < 100; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/auth/refresh-token", null)).StatusCode);
    }

    [Theory]
    [InlineData("/realms/exam-realm/protocol/openid-connect/auth")]
    [InlineData("/auth/realms/exam-realm/login-actions/authenticate")]
    [InlineData("/realms/exam-realm/login-actions/reset-credentials")]
    [InlineData("/resources/abc/login/keycloak/css/login.css")]
    [InlineData("/userinfo")]
    [InlineData("/token")]
    [InlineData("/api/auth/login")]
    public async Task Get_IsNeverLimited(string path)
    {
        using var client = await CreateClientAsync();
        for (var i = 0; i < 100; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData("/realms/exam-realm/login-actions/restart")]
    [InlineData("/Realms/exam-realm/login-actions/restart/")]
    [InlineData("/realms/exam-realm/login-actions/restart;x")]
    public async Task AllowlistedRestart_IsNotLimited(string path)
    {
        using var client = await CreateClientAsync();
        for (var i = 0; i < 150; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Post(path))).StatusCode);
    }

    [Fact]
    public async Task OversizedRefreshBody_FailsClosed_IsCounted()
    {
        using var client = await CreateClientAsync(permit: 2);
        var big = "grant_type=refresh_token&refresh_token=" + new string('a', 17 * 1024);
        HttpRequestMessage Req() => new(HttpMethod.Post, "/token")
        {
            Content = new StringContent(big, System.Text.Encoding.UTF8, "application/x-www-form-urlencoded")
        };
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Req())).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Req())).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(Req())).StatusCode);
    }

    private sealed class UnknownLengthContent(string body) : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) =>
            stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(body)).AsTask();
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }

    [Fact]
    public async Task RefreshWithoutContentLength_FailsClosed_IsCounted()
    {
        using var client = await CreateClientAsync(permit: 1);
        HttpRequestMessage Req()
        {
            var r = new HttpRequestMessage(HttpMethod.Post, "/token") { Content = new UnknownLengthContent("grant_type=refresh_token") };
            r.Content.Headers.ContentType = new("application/x-www-form-urlencoded");
            return r;
        }
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Req())).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(Req())).StatusCode);
    }

    [Fact]
    public async Task MalformedMultipartBody_IsCountedAndPassedThrough_Not500()
    {
        using var client = await CreateClientAsync(permit: 1);
        HttpRequestMessage Req()
        {
            var r = new HttpRequestMessage(HttpMethod.Post, "/token") { Content = new StringContent("garbage--no-boundary") };
            r.Content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("multipart/form-data; boundary=zzz");
            return r;
        }
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Req())).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(Req())).StatusCode);
    }

    [Theory]
    [InlineData("/realms/exam-realm/login-actions/restart/authenticate/..;")]
    [InlineData("/realms/exam-realm/login-actions/restart/authenticate%2F..")]
    [InlineData("/realms/exam-realm/login-actions/restart/authenticate%5c..;x")]
    public async Task TraversalAfterNormalization_SkipsExemption_IsCounted(string path)
    {
        using var client = await CreateClientAsync(permit: 2);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Post(path))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(Post(path))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(Post(path))).StatusCode);
    }

    private static async Task<HttpClient> CreateForwardedClientAsync(params string[] knownProxies)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.WebHost.UseTestServer();
        builder.Configuration["RateLimiting:LoginEntry:PermitLimit"] = "1";
        for (var i = 0; i < knownProxies.Length; i++)
            builder.Configuration[$"ForwardedHeaders:KnownProxies:{i}"] = knownProxies[i];
        var app = builder.Build();
        app.Use((ctx, next) =>
        {
            ctx.Connection.RemoteIpAddress = IPAddress.Parse(ctx.Request.Headers["X-Test-Remote"].ToString());
            return next();
        });
        app.UseTrustedForwardedFor(app.Configuration);
        app.UseLoginRateLimit(app.Configuration);
        app.Run(ctx => { ctx.Response.StatusCode = 200; return Task.CompletedTask; });
        await app.StartAsync();
        return app.GetTestClient();
    }

    private static HttpRequestMessage ForwardedPost(string remote, string xff)
    {
        var r = Post("/api/auth/login");
        r.Headers.Add("X-Test-Remote", remote);
        r.Headers.Add("X-Forwarded-For", xff);
        return r;
    }

    [Fact]
    public async Task UntrustedSource_ForwardedFor_IsIgnored()
    {
        using var client = await CreateForwardedClientAsync("10.0.0.1");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(ForwardedPost("10.0.0.9", "1.1.1.1"))).StatusCode);
        // Farklı XFF ile bypass denemesi: anahtar hâlâ 10.0.0.9.
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(ForwardedPost("10.0.0.9", "2.2.2.2"))).StatusCode);
    }

    [Fact]
    public async Task TrustedProxy_ForwardedFor_IsUsedAsLimiterKey()
    {
        using var client = await CreateForwardedClientAsync("10.0.0.1");
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(ForwardedPost("10.0.0.1", "1.1.1.1"))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.SendAsync(ForwardedPost("10.0.0.1", "2.2.2.2"))).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.SendAsync(ForwardedPost("10.0.0.1", "1.1.1.1"))).StatusCode);
    }

    [Theory]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "0.0.0.0/0")]
    [InlineData("ForwardedHeaders:KnownNetworks:0", "::/0")]
    [InlineData("ForwardedHeaders:KnownProxies:0", "0.0.0.0")]
    public void CatchAllTrust_IsRejectedAtStartup(string key, string value)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { ContentRootPath = AppContext.BaseDirectory });
        builder.WebHost.UseTestServer();
        builder.Configuration[key] = value;
        var app = builder.Build();
        Assert.Throws<InvalidOperationException>(() => app.UseTrustedForwardedFor(app.Configuration));
    }

    [Theory]
    [InlineData("/Realms/X/;a/login-actions//authenticate/", "/realms/x/login-actions/authenticate")]
    [InlineData("/a%2Fb%5Cc", "/a/b/c")]
    [InlineData("/a/./b/../c;p", "/a/c")]
    public void Canonicalize_Normalizes(string raw, string expected) =>
        Assert.Equal(expected, LoginRateLimitExtensions.Canonicalize(raw));

    [Theory]
    [InlineData("10.1.2.3", "10.1.2.3")]
    [InlineData("::ffff:10.1.2.3", "10.1.2.3")]
    [InlineData("2001:db8:1:2:aaaa:bbbb:cccc:dddd", "2001:db8:1:2::/64")]
    [InlineData("2001:db8:1:2:1:2:3:4", "2001:db8:1:2::/64")]
    public void ClientKey_MapsV4AndGroupsV6By64(string ip, string expected) =>
        Assert.Equal(expected, LoginRateLimitExtensions.ClientKey(IPAddress.Parse(ip)));
}

public class RealmExportTests
{
    [Fact]
    public void DevRealmExport_HasTemporaryBruteForceProtection()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "deploy", "keycloak", "dev-import", "realm-export.json")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);

        using var doc = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(dir!, "deploy", "keycloak", "dev-import", "realm-export.json")));
        var r = doc.RootElement;
        Assert.True(r.GetProperty("bruteForceProtected").GetBoolean());
        Assert.False(r.GetProperty("permanentLockout").GetBoolean());
        Assert.Equal(5, r.GetProperty("failureFactor").GetInt32());
        Assert.InRange(r.GetProperty("maxFailureWaitSeconds").GetInt32(), 60, 900);
    }
}

/// <summary>
/// Issue #368: aynı kontroller gerçek Program.cs pipeline'ı (Ocelot + JwtBearer) üzerinden; downstream sahte HTTP sunucu.
/// </summary>
[Collection("Gateway")]
public sealed class GatewayLoginRateLimitPipelineTests
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly WebApplication _downstream;
        private readonly string _oldCwd = Directory.GetCurrentDirectory();
        private readonly string _tempDir;

        public Factory()
        {
            var b = WebApplication.CreateBuilder();
            b.WebHost.UseUrls("http://127.0.0.1:0");
            _downstream = b.Build();
            _downstream.Run(async ctx =>
            {
                if (ctx.Request.HasFormContentType) await ctx.Request.ReadFormAsync(); // gövde Ocelot'tan sağlam geçmeli
                await ctx.Response.WriteAsync("downstream-ok");
            });
            _downstream.Start();
            var port = new Uri(_downstream.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.First()).Port;

            _tempDir = Directory.CreateTempSubdirectory("gateway-rl-tests-").FullName;
            File.WriteAllText(Path.Combine(_tempDir, "ocelot.json"), OcelotJson(port));
            Directory.SetCurrentDirectory(_tempDir);
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            await _downstream.DisposeAsync();
            Directory.SetCurrentDirectory(_oldCwd);
            try { Directory.Delete(_tempDir, true); } catch (IOException) { }
        }

        private static string Route(string down, string up, string methods, int port) => $$"""
            { "DownstreamPathTemplate": "{{down}}", "DownstreamScheme": "http",
              "DownstreamHostAndPorts": [ { "Host": "127.0.0.1", "Port": {{port}} } ],
              "UpstreamPathTemplate": "{{up}}", "UpstreamHttpMethod": [ {{methods}} ] }
            """;

        private static string OcelotJson(int port) => "{ \"Routes\": [" + string.Join(",", new[]
        {
            Route("/api/auth/{everything}", "/api/auth/{everything}", "\"GET\",\"POST\"", port),
            Route("/realms/exam-realm/protocol/openid-connect/token", "/token", "\"POST\"", port),
            Route("/realms/{everything}", "/realms/{everything}", "\"GET\",\"POST\"", port),
        }) + "], \"GlobalConfiguration\": { \"BaseUrl\": \"http://localhost\" } }";

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseContentRoot(GatewayWebSocketAuthTests.GatewayFactory.FindGatewayDir());
        }
    }

    private static FormUrlEncodedContent Form(string grant) =>
        new(new Dictionary<string, string> { ["grant_type"] = grant, ["username"] = "u" });

    [Fact]
    public async Task Login_101stRequest_Returns429WithRetryAfter_ThroughRealPipeline()
    {
        await using var factory = new Factory();
        var client = factory.CreateClient();

        for (var i = 0; i < 100; i++)
        {
            var ok = await client.PostAsync("/api/auth/login", Form("password"));
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Equal("downstream-ok", await ok.Content.ReadAsStringAsync());
        }

        var res = await client.PostAsync("/api/auth/login", Form("password"));
        Assert.Equal(HttpStatusCode.TooManyRequests, res.StatusCode);
        Assert.NotNull(res.Headers.RetryAfter?.Delta);
    }

    [Fact]
    public async Task TokenAndRealmLoginAction_ShareBudget_ThroughRealPipeline()
    {
        await using var factory = new Factory();
        var client = factory.CreateClient();

        for (var i = 0; i < 50; i++)
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/token", Form("authorization_code"))).StatusCode);
        for (var i = 0; i < 50; i++)
            Assert.Equal(HttpStatusCode.OK,
                (await client.PostAsync("/realms/exam-realm/login-actions/authenticate", Form("x"))).StatusCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.PostAsync("/token", Form("password"))).StatusCode);
    }

    [Fact]
    public async Task Refresh_And_GetPageLoads_AreNotLimited_ThroughRealPipeline()
    {
        await using var factory = new Factory();
        var client = factory.CreateClient();

        for (var i = 0; i < 40; i++)
        {
            var refresh = await client.PostAsync("/token", Form("refresh_token"));
            Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsync("/api/auth/refresh-token", null)).StatusCode);
            Assert.Equal(HttpStatusCode.OK,
                (await client.GetAsync("/realms/exam-realm/protocol/openid-connect/auth")).StatusCode);
        }
    }
}
