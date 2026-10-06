using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.WebUtilities;

namespace Gateway.Tests;

/// <summary>
/// Issue #347: <c>/oidc-login</c> state'i kendisi kurmaz (eski <c>"&lt;host&gt;~intent"</c> open redirect'e yol
/// açıyordu); istemcinin ürettiği rastgele state + S256 code_challenge'ı doğrulayıp Keycloak'a iletir.
/// Eksik/geçersiz istek Keycloak'a hiç gitmez, state/PKCE'yi üretecek <c>/app/login</c>'e döner.
/// </summary>
public sealed class OidcLoginRedirectTests
{
    private const string Host = "http://localhost:5678";
    private const string RedirectUri = "http://localhost:5678/app/callback";
    private static readonly string State = new('S', 43);
    private const string Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM"; // RFC 7636 App. B

    private static string Build(string queryString)
    {
        var query = new QueryCollection(QueryHelpers.ParseQuery(queryString));
        return OidcLoginRedirect.BuildRedirect(query, Host, "exam-realm", "exam-client", RedirectUri);
    }

    private static Dictionary<string, string> QueryOf(string url)
    {
        var q = new Uri(url).Query;
        return QueryHelpers.ParseQuery(q).ToDictionary(kv => kv.Key, kv => kv.Value.ToString());
    }

    private static string Valid(string extra = "") =>
        $"?state={State}&code_challenge={Challenge}&code_challenge_method=S256{extra}";

    [Fact]
    public void Valid_state_and_challenge_are_forwarded_to_keycloak_auth_endpoint()
    {
        var url = Build(Valid());

        url.ShouldStartWith($"{Host}/auth/realms/exam-realm/protocol/openid-connect/auth?");
        var q = QueryOf(url);
        q["client_id"].ShouldBe("exam-client");
        q["redirect_uri"].ShouldBe(RedirectUri);
        q["response_type"].ShouldBe("code");
        q["scope"].ShouldBe("openid");
        q["state"].ShouldBe(State);
        q["code_challenge"].ShouldBe(Challenge);
        q["code_challenge_method"].ShouldBe("S256");
        q.ShouldNotContainKey("ui_locales");
    }

    [Theory]
    [InlineData("student")]
    [InlineData("Teacher")]
    [InlineData(" parent ")]
    public void Register_intent_uses_registrations_endpoint_and_is_not_embedded_in_state(string intent)
    {
        var url = Build(Valid($"&intent={Uri.EscapeDataString(intent)}"));

        url.ShouldContain("/protocol/openid-connect/registrations?");
        QueryOf(url)["state"].ShouldBe(State);
        url.ShouldNotContain("~");
    }

    [Fact]
    public void Unknown_intent_falls_back_to_auth_endpoint()
    {
        Build(Valid("&intent=admin")).ShouldContain("/protocol/openid-connect/auth?");
    }

    [Fact]
    public void Valid_ui_locales_is_forwarded()
    {
        QueryOf(Build(Valid("&ui_locales=en")))["ui_locales"].ShouldBe("en");
    }

    [Fact]
    public void Ui_locales_with_markup_is_dropped()
    {
        QueryOf(Build(Valid("&ui_locales=en%22%3E%3Cscript%3E"))).ShouldNotContainKey("ui_locales");
    }

    [Fact]
    public void Ui_locales_with_trailing_newline_is_dropped()
    {
        QueryOf(Build(Valid("&ui_locales=en%0A"))).ShouldNotContainKey("ui_locales");
    }

    [Fact]
    public void Injected_extra_parameter_is_not_forwarded_and_locale_part_stays_valid()
    {
        // "tr&evil=1" query'de iki ayrı parametredir: ui_locales=tr geçerli, evil hiç iletilmez.
        var q = QueryOf(Build(Valid("&ui_locales=tr&evil=1")));

        q["ui_locales"].ShouldBe("tr");
        q.ShouldNotContainKey("evil");
    }

    [Theory]
    [InlineData("")]                                                                       // hiç parametre yok (eski bağlantı)
    [InlineData("?intent=student")]                                                        // eski kayıt bağlantısı
    [InlineData("?state=http%3A%2F%2Fevil.example~student&code_challenge=" + Challenge + "&code_challenge_method=S256")] // eski biçim / dış URL
    [InlineData("?state=short&code_challenge=" + Challenge + "&code_challenge_method=S256")]
    [InlineData("?state=SSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSS&code_challenge_method=S256")]  // challenge yok
    [InlineData("?state=SSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSS&code_challenge=" + Challenge)]  // method yok
    [InlineData("?state=SSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSS&code_challenge=" + Challenge + "&code_challenge_method=plain")]
    [InlineData("?state=SSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSS&code_challenge=abc&code_challenge_method=S256")]
    public void Missing_or_invalid_pkce_parameters_redirect_to_login_page_instead_of_keycloak(string query)
    {
        var url = Build(query);

        url.ShouldStartWith(OidcLoginRedirect.LoginPagePath);
        url.ShouldNotContain("/realms/");
        url.ShouldNotContain("evil");
    }

    [Theory]
    [InlineData("%0A")]   // .NET regex `$` sondaki satır sonundan önce de eşleşirdi; `\z` ile reddedilir
    [InlineData("%0D%0A")]
    public void State_or_challenge_with_trailing_newline_redirects_to_login_page(string suffix)
    {
        Build($"?state={State}{suffix}&code_challenge={Challenge}&code_challenge_method=S256")
            .ShouldBe(OidcLoginRedirect.LoginPagePath);
        Build($"?state={State}&code_challenge={Challenge}{suffix}&code_challenge_method=S256")
            .ShouldBe(OidcLoginRedirect.LoginPagePath);
    }

    [Fact]
    public void Login_page_fallback_keeps_valid_intent_and_locale_only()
    {
        Build("?intent=teacher&ui_locales=en").ShouldBe("/app/login?intent=teacher&ui_locales=en");
        Build("?intent=hacker&ui_locales=%3Cx%3E").ShouldBe("/app/login");
    }
}

/// <summary>
/// Issue #347: <c>/oidc-login</c> gerçek Program.cs pipeline'ında (Ocelot + JwtBearer + rate limit) bağlı mı —
/// Ocelot'a düşmeden gateway middleware'i 302 üretmeli.
/// </summary>
[Collection("Gateway")]
public sealed class OidcLoginPipelineTests
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        private readonly string _oldCwd = Directory.GetCurrentDirectory();
        private readonly string _tempDir;

        public Factory()
        {
            // Program.cs ocelot.json'ı CWD'den okur; /oidc-login'i karşılayan bir Ocelot route'u bilinçli olarak YOK.
            _tempDir = Directory.CreateTempSubdirectory("gateway-oidc-tests-").FullName;
            File.WriteAllText(Path.Combine(_tempDir, "ocelot.json"), """
                { "Routes": [ { "DownstreamPathTemplate": "/x", "DownstreamScheme": "http",
                    "DownstreamHostAndPorts": [ { "Host": "127.0.0.1", "Port": 9 } ],
                    "UpstreamPathTemplate": "/unused-route", "UpstreamHttpMethod": [ "GET" ] } ],
                  "GlobalConfiguration": { "BaseUrl": "http://localhost" } }
                """);
            Directory.SetCurrentDirectory(_tempDir);
        }

        public override async ValueTask DisposeAsync()
        {
            await base.DisposeAsync();
            Directory.SetCurrentDirectory(_oldCwd);
            try { Directory.Delete(_tempDir, true); } catch (IOException) { }
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseContentRoot(GatewayWebSocketAuthTests.GatewayFactory.FindGatewayDir());
        }
    }

    private const string Challenge = "E9Melhoa2OwvFrEMTJguCHaoeK1t8URWbuGJSstw-cM";
    private static readonly string State = new('S', 43);

    private static HttpClient NoRedirectClient(Factory f) =>
        f.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

    [Fact]
    public async Task Valid_request_redirects_to_keycloak_with_state_and_challenge()
    {
        await using var factory = new Factory();
        var client = NoRedirectClient(factory);

        var res = await client.GetAsync(
            $"/oidc-login?state={State}&code_challenge={Challenge}&code_challenge_method=S256&intent=teacher&ui_locales=en");

        res.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        var location = res.Headers.Location!.ToString();
        // appsettings.json: Server:BaseUrl + Keycloak:Realm/ClientId/RedirectUri
        location.ShouldStartWith("http://localhost:5678/auth/realms/exam-realm/protocol/openid-connect/registrations?");
        location.ShouldContain($"state={State}");
        location.ShouldContain($"code_challenge={Challenge}");
        location.ShouldContain("code_challenge_method=S256");
        location.ShouldContain("redirect_uri=" + Uri.EscapeDataString("http://localhost:5678/app/callback"));
        location.ShouldContain("ui_locales=en");
    }

    [Theory]
    [InlineData("/oidc-login")]
    [InlineData("/oidc-login?intent=student")]
    [InlineData("/oidc-login?state=SSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSSS%0A&code_challenge=" + Challenge + "&code_challenge_method=S256")]
    public async Task Request_without_valid_pkce_redirects_to_app_login_not_500(string url)
    {
        await using var factory = new Factory();
        var client = NoRedirectClient(factory);

        var res = await client.GetAsync(url);

        res.StatusCode.ShouldBe(HttpStatusCode.Redirect);
        res.Headers.Location!.ToString().ShouldStartWith("/app/login");
        res.Headers.Location!.ToString().ShouldNotContain("/realms/");
    }
}

/// <summary>Issue #347: dev realm export'unda exam-client PKCE S256 zorunlu ve redirect URI'ları jokersiz.</summary>
public sealed class ExamClientRealmExportTests
{
    [Fact]
    public void ExamClient_EnforcesPkceS256_AndOnlyAllowsAppCallbackRedirects()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !File.Exists(Path.Combine(dir, "deploy", "keycloak", "dev-import", "realm-export.json")))
            dir = Path.GetDirectoryName(dir);
        dir.ShouldNotBeNull();

        using var doc = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(dir, "deploy", "keycloak", "dev-import", "realm-export.json")));
        var client = doc.RootElement.GetProperty("clients").EnumerateArray()
            .Single(c => c.GetProperty("clientId").GetString() == "exam-client");

        client.GetProperty("attributes").GetProperty("pkce.code.challenge.method").GetString().ShouldBe("S256");
        var uris = client.GetProperty("redirectUris").EnumerateArray().Select(u => u.GetString()!).ToList();
        uris.ShouldContain("http://localhost:5678/app/callback");
        uris.ShouldAllBe(u => u.EndsWith("/app/callback") && !u.Contains('*'));
    }
}
