using System.Net;
using System.Text;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services;
using Microsoft.Extensions.Options;

namespace AuthApi.Tests.Services;

/// <summary>
/// Issue #231: token uç noktası hataları <see cref="KeycloakFailureKind"/> ile sınıflandırılır ki controller
/// kötü kimlik bilgisini (401) Keycloak'a ulaşılamamasından (503) ayırabilsin. Sahte <see cref="HttpMessageHandler"/>
/// ile (KeycloakServiceSearchTests ile aynı yaklaşım).
/// </summary>
public class KeycloakServiceTokenErrorTests
{
    private const string ExamplePassword = "wrong-pw-example"; // example credential, test-only

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_respond(request));
    }

    private static KeycloakService Build(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var client = new HttpClient(new FakeHandler(respond));
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient().Returns(client);
        factory.CreateClient(Arg.Any<string>()).Returns(client);
        var settings = new KeycloakSettings
        {
            Host = "http://keycloak.test",
            TokenUrl = "realms/exam-realm/protocol/openid-connect/token",
            RedirectUri = "http://app.test/callback",
            ClientId = "exam-client",
            ClientSecret = "example-secret-value",
            GrantType = "password",
        };
        return new KeycloakService(factory, Options.Create(settings));
    }

    private static HttpResponseMessage Body(HttpStatusCode code, string body, string mediaType = "application/json")
        => new(code) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    [Fact]
    public async Task Login_invalid_grant_is_classified_as_InvalidGrant()
    {
        var service = Build(_ => Body(HttpStatusCode.Unauthorized,
            """{"error":"invalid_grant","error_description":"Invalid user credentials"}"""));

        var ex = await Should.ThrowAsync<KeycloakException>(() => service.LoginAsync("bad@test.local", ExamplePassword));

        ex.Kind.ShouldBe(KeycloakFailureKind.InvalidGrant);
        ex.StatusCode.ShouldBe(401);
        ex.Message.ShouldNotContain(ExamplePassword);
    }

    [Fact]
    public async Task Login_network_failure_is_classified_as_ProviderUnavailable()
    {
        var service = Build(_ => throw new HttpRequestException("Connection refused"));

        var ex = await Should.ThrowAsync<KeycloakException>(() => service.LoginAsync("a@test.local", ExamplePassword));

        ex.Kind.ShouldBe(KeycloakFailureKind.ProviderUnavailable);
        ex.StatusCode.ShouldBe(503);
    }

    [Fact]
    public async Task Login_timeout_is_classified_as_ProviderUnavailable()
    {
        var service = Build(_ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));

        var ex = await Should.ThrowAsync<KeycloakException>(() => service.LoginAsync("a@test.local", ExamplePassword));

        ex.Kind.ShouldBe(KeycloakFailureKind.ProviderUnavailable);
    }

    [Fact]
    public async Task Login_resilience_timeout_is_classified_as_ProviderUnavailable()
    {
        // ServiceDefaults standart resilience handler'ının deneme/toplam zaman aşımı.
        var service = Build(_ => throw new Polly.Timeout.TimeoutRejectedException());

        var ex = await Should.ThrowAsync<KeycloakException>(() => service.LoginAsync("a@test.local", ExamplePassword));

        ex.Kind.ShouldBe(KeycloakFailureKind.ProviderUnavailable);
        ex.StatusCode.ShouldBe(503);
    }

    [Fact]
    public async Task Login_cancelled_by_the_caller_is_not_reported_as_provider_unavailable()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var service = Build(_ => throw new TaskCanceledException());

        await Should.ThrowAsync<OperationCanceledException>(() => service.LoginAsync("a@test.local", ExamplePassword, cts.Token));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadGateway, "<html><body>502 Bad Gateway</body></html>", "text/html")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "", "text/plain")]
    [InlineData(HttpStatusCode.InternalServerError, """{"error":"unknown_error"}""", "application/json")]
    public async Task Login_5xx_or_non_json_error_is_classified_as_ProviderUnavailable(HttpStatusCode code, string body, string mediaType)
    {
        var service = Build(_ => Body(code, body, mediaType));

        var ex = await Should.ThrowAsync<KeycloakException>(() => service.LoginAsync("a@test.local", ExamplePassword));

        ex.Kind.ShouldBe(KeycloakFailureKind.ProviderUnavailable);
    }

    [Fact]
    public async Task Login_invalid_client_is_unexpected_not_a_user_error()
    {
        var service = Build(_ => Body(HttpStatusCode.Unauthorized,
            """{"error":"invalid_client","error_description":"Invalid client or Invalid client credentials"}"""));

        var ex = await Should.ThrowAsync<KeycloakException>(() => service.LoginAsync("a@test.local", ExamplePassword));

        ex.Kind.ShouldBe(KeycloakFailureKind.Unexpected);
        ex.StatusCode.ShouldBe(500);
    }

    [Fact]
    public async Task Exchange_expired_code_is_classified_as_InvalidGrant()
    {
        var service = Build(_ => Body(HttpStatusCode.BadRequest,
            """{"error":"invalid_grant","error_description":"Code not valid"}"""));

        var ex = await Should.ThrowAsync<KeycloakException>(() => service.ExchangeTokenAsync("code-example"));

        ex.Kind.ShouldBe(KeycloakFailureKind.InvalidGrant);
    }

    [Fact]
    public async Task Refresh_inactive_token_is_classified_as_InvalidGrant()
    {
        var service = Build(_ => Body(HttpStatusCode.BadRequest,
            """{"error":"invalid_grant","error_description":"Token is not active"}"""));

        var ex = await Should.ThrowAsync<KeycloakException>(() => service.RefreshTokenAsync("refresh-example"));

        ex.Kind.ShouldBe(KeycloakFailureKind.InvalidGrant);
    }

    [Fact]
    public async Task Login_success_returns_the_token()
    {
        var service = Build(_ => Body(HttpStatusCode.OK,
            """{"access_token":"at-example","refresh_token":"rt-example","token_type":"Bearer","expires_in":300,"refresh_expires_in":1800}"""));

        var token = await service.LoginAsync("a@test.local", ExamplePassword);

        token.AccessToken.ShouldBe("at-example");
    }
}
