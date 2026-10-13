using System.Net;
using System.Text;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services.Parents;
using ExamApp.Api.Services.StudentReset;
using ExamApp.Api.Tests.Support;
using ExamApp.Foundation.Contracts;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #438: exam API → auth-api çocuk hesabı istemcisi. Servis token'ıyla doğru uçlara gider, HTTP durumlarını sonuç türüne
/// eşler, hiçbir hata yolunda parolayı loglamaz; named client ServiceDefaults resilience handler'ından muaftır (oluşturma idempotent
/// değil — yeniden gönderilen POST yetim hesap üretirdi). Gateway'in üzerine yazdığı X-Forwarded-For'dan rıza IP'si okunur.
/// </summary>
public class AuthApiChildAccountDirectoryTests
{
    private const string Pw = "Kalem2026x"; // test-only example

    private sealed class Captured
    {
        public HttpMethod? Method;
        public string? Url;
        public string? Auth;
        public string? Body;
    }

    private static (AuthApiChildAccountDirectory Directory, Captured Captured, ChildAccountTestLogger<AuthApiChildAccountDirectory> Logger)
        Build(Func<HttpResponseMessage> respond, IServiceTokenProvider? tokens = null)
    {
        var captured = new Captured();
        var http = new StubHttp(req =>
        {
            captured.Method = req.Method;
            captured.Url = req.RequestUri!.ToString();
            captured.Auth = req.Headers.Authorization?.ToString();
            captured.Body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return respond();
        });
        if (tokens == null)
        {
            tokens = Substitute.For<IServiceTokenProvider>();
            tokens.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("svc-token");
        }
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AuthApiBaseUrl"] = "http://auth-api:5079/" })
            .Build();
        var logger = new ChildAccountTestLogger<AuthApiChildAccountDirectory>();
        return (new AuthApiChildAccountDirectory(http, tokens, config, logger), captured, logger);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body)
        => new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static CreateChildAccountRequest Request()
    {
        var r = new CreateChildAccountRequest { Username = "ali.kaya", FirstName = "Ali", LastName = "Kaya" };
        r.Password = Pw;
        return r;
    }

    [Fact]
    public async Task Create_posts_to_auth_api_with_the_service_token_and_reads_the_created_ids()
    {
        var (directory, captured, _) = Build(() => Json(HttpStatusCode.Created, """{"userId":77,"keycloakId":"kc-77"}"""));

        var result = await directory.CreateAsync(Request());

        result.Status.ShouldBe(ChildAccountCallStatus.Success);
        result.Created!.UserId.ShouldBe(77);
        result.Created.KeycloakId.ShouldBe("kc-77");
        captured.Method.ShouldBe(HttpMethod.Post);
        captured.Url.ShouldBe("http://auth-api:5079/api/auth/child-accounts");
        captured.Auth.ShouldBe("Bearer svc-token");
        captured.Body.ShouldContain("\"username\":\"ali.kaya\"");
    }

    [Theory]
    [InlineData(HttpStatusCode.Conflict, """{"errorCode":"UsernameTaken"}""", ChildAccountCallStatus.Conflict, "UsernameTaken")]
    [InlineData(HttpStatusCode.BadRequest, """{"errorCode":"PasswordRejected"}""", ChildAccountCallStatus.Invalid, "PasswordRejected")]
    [InlineData(HttpStatusCode.NotFound, """{"errorCode":"NotFound"}""", ChildAccountCallStatus.NotFound, "NotFound")]
    [InlineData(HttpStatusCode.BadGateway, """{"errorCode":"Unavailable"}""", ChildAccountCallStatus.Unavailable, "Unavailable")]
    [InlineData(HttpStatusCode.Forbidden, "", ChildAccountCallStatus.Unavailable, "Unavailable")]
    [InlineData(HttpStatusCode.InternalServerError, "<html>boom</html>", ChildAccountCallStatus.Unavailable, "Unavailable")]
    public async Task Http_statuses_map_to_call_results_without_logging_the_password(
        HttpStatusCode status, string body, ChildAccountCallStatus expected, string expectedCode)
    {
        var (directory, _, logger) = Build(() => Json(status, body));

        var result = await directory.CreateAsync(Request());

        result.Status.ShouldBe(expected);
        result.ErrorCode.ShouldBe(expectedCode);
        logger.Lines.ShouldAllBe(l => !l.Contains(Pw));
    }

    [Fact]
    public async Task Created_without_ids_is_unavailable()
    {
        var (directory, _, _) = Build(() => Json(HttpStatusCode.Created, """{"userId":0}"""));

        (await directory.CreateAsync(Request())).Status.ShouldBe(ChildAccountCallStatus.Unavailable);
    }

    [Fact]
    public async Task Network_error_and_token_failure_are_unavailable_and_do_not_leak_the_password()
    {
        var (directory, _, logger) = Build(() => throw new HttpRequestException("connection refused"));
        (await directory.ResetPasswordAsync(5, Pw)).Status.ShouldBe(ChildAccountCallStatus.Unavailable);
        logger.Lines.ShouldAllBe(l => !l.Contains(Pw));

        var tokens = Substitute.For<IServiceTokenProvider>();
        tokens.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns<string>(_ => throw new InvalidOperationException("no secret"));
        var (noToken, captured, _) = Build(() => Json(HttpStatusCode.NoContent, ""), tokens);
        (await noToken.CreateAsync(Request())).Status.ShouldBe(ChildAccountCallStatus.Unavailable);
        captured.Url.ShouldBeNull(); // token yoksa auth-api'ye hiç gidilmez
    }

    [Fact]
    public async Task Delete_and_reset_use_the_user_scoped_urls()
    {
        var (directory, captured, _) = Build(() => new HttpResponseMessage(HttpStatusCode.NoContent));

        (await directory.DeleteAsync(42)).Status.ShouldBe(ChildAccountCallStatus.Success);
        captured.Method.ShouldBe(HttpMethod.Delete);
        captured.Url.ShouldBe("http://auth-api:5079/api/auth/child-accounts/42");

        (await directory.ResetPasswordAsync(42, Pw)).Status.ShouldBe(ChildAccountCallStatus.Success);
        captured.Method.ShouldBe(HttpMethod.Put);
        captured.Url.ShouldBe("http://auth-api:5079/api/auth/child-accounts/42/password");
        captured.Body.ShouldContain(Pw); // yalnız istek gövdesinde
    }

    [Fact]
    public void Named_client_has_no_resilience_handler_while_defaults_do()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler());
        services.AddParentChildAccounts();
        services.AddHttpClient("control");
        using var provider = services.BuildServiceProvider();
        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();

        static IEnumerable<Type> Chain(HttpMessageHandler? h)
        {
            for (var current = h; current != null; current = (current as DelegatingHandler)?.InnerHandler)
                yield return current.GetType();
        }

        Chain(factory.CreateHandler(AuthApiChildAccountDirectory.HttpClientName))
            .ShouldNotContain(t => t.Name.Contains("Resilience"));
        Chain(factory.CreateHandler("control")).ShouldContain(t => t.Name.Contains("Resilience"));
    }

    [Theory]
    [InlineData("198.51.100.4", "10.0.0.9", "198.51.100.4")]
    [InlineData("198.51.100.4, 10.0.0.1", "10.0.0.9", "198.51.100.4")]
    [InlineData("not-an-ip", "10.0.0.9", "10.0.0.9")]
    [InlineData(null, "::ffff:192.0.2.5", "192.0.2.5")]
    public void Client_ip_prefers_the_gateway_forwarded_header(string? forwarded, string remote, string expected)
    {
        var context = new DefaultHttpContext();
        if (forwarded != null)
            context.Request.Headers["X-Forwarded-For"] = forwarded;
        context.Connection.RemoteIpAddress = IPAddress.Parse(remote);

        ClientIpAddress.Resolve(context).ShouldBe(expected);
        ClientIpAddress.Resolve(null).ShouldBeNull();
    }
}
