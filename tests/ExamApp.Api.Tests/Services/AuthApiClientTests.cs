using System.Net;
using System.Text;
using System.Text.Json;
using ExamApp.Api.Services;
using ExamApp.Api.Services.StudentReset;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #219: <see cref="AuthApiClient.GetUsersByIdsAsync"/> auth-api'nin artık servis-only olan
/// <c>users/lookup</c> ucuna kullanıcının token'ını forward etmemeli; client_credentials servis token'ı
/// ile gitmeli. <see cref="AuthApiClient.GetUserProfileAsync"/> ise "kendi profilim" ucu olduğu için
/// kullanıcı token'ı ile kalmaya devam eder.
/// </summary>
public class AuthApiClientTests
{
    private const string UserBearer = "Bearer user-token-example";
    private const string ServiceToken = "svc-token-example";

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _respond;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }
        public int Calls { get; private set; }

        public FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return await _respond(request);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (AuthApiClient Client, FakeHandler Handler, IServiceTokenProvider Tokens, ILogger<AuthApiClient> Logger) Build(
        Func<HttpRequestMessage, Task<HttpResponseMessage>> respond,
        IServiceTokenProvider? tokenProvider = null,
        bool withUserRequest = true)
    {
        var handler = new FakeHandler(respond);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AuthApiBaseUrl"] = "http://auth-api.test" })
            .Build();

        var accessor = Substitute.For<IHttpContextAccessor>();
        if (withUserRequest)
        {
            var httpContext = new DefaultHttpContext();
            httpContext.Request.Headers.Authorization = UserBearer;
            accessor.HttpContext.Returns(httpContext);
        }
        else
        {
            accessor.HttpContext.Returns((HttpContext?)null);
        }

        var tokens = tokenProvider ?? Substitute.For<IServiceTokenProvider>();
        if (tokenProvider is null)
            tokens.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns(ServiceToken);

        var logger = Substitute.For<ILogger<AuthApiClient>>();
        var client = new AuthApiClient(config, accessor, factory, tokens, logger);
        return (client, handler, tokens, logger);
    }

    private static string LookupBody(params (int Id, string FullName)[] users) =>
        JsonSerializer.Serialize(users.Select(u => new { u.Id, u.FullName, KeycloakId = "kc-" + u.Id, Email = u.FullName.ToLowerInvariant() + "@test.local", Avatar = "", Role = "Student" }));

    [Fact]
    public async Task GetUsersByIds_sends_service_token_not_the_users_authorization_header()
    {
        var (client, handler, _, _) = Build(_ => Task.FromResult(Json(HttpStatusCode.OK, LookupBody((7, "Ayse"), (9, "Ali")))));

        var result = await client.GetUsersByIdsAsync([7, 9, 7]);

        result.Count.ShouldBe(2);
        result.Single(u => u.Id == 7).FullName.ShouldBe("Ayse");
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Post);
        handler.LastRequest.RequestUri!.ToString().ShouldBe("http://auth-api.test/api/auth/users/lookup");
        handler.LastRequest.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.ShouldBe(ServiceToken);
        handler.LastRequest.Headers.Authorization.ToString().ShouldNotBe(UserBearer);
        handler.LastRequest.Headers.GetValues("Authorization").ShouldHaveSingleItem();
        using var body = JsonDocument.Parse(handler.LastBody!);
        body.RootElement.GetProperty("UserIds").EnumerateArray().Select(e => e.GetInt32()).ShouldBe([7, 9]);
    }

    [Fact]
    public async Task GetUsersByIds_works_without_an_http_context()
    {
        // Hangfire/CLI gibi request dışı bağlamlarda da servis token'ı ile çalışmalı.
        var (client, handler, _, _) = Build(_ => Task.FromResult(Json(HttpStatusCode.OK, LookupBody((1, "A")))), withUserRequest: false);

        var result = await client.GetUsersByIdsAsync([1]);

        result.ShouldHaveSingleItem();
        handler.LastRequest!.Headers.Authorization!.Parameter.ShouldBe(ServiceToken);
    }

    [Fact]
    public async Task GetUsersByIds_returns_empty_without_calling_auth_api_when_service_token_cannot_be_obtained()
    {
        var tokens = Substitute.For<IServiceTokenProvider>();
        tokens.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new InvalidOperationException("Keycloak client credentials missing"));
        var (client, handler, _, _) = Build(_ => throw new InvalidOperationException("should not be called"), tokenProvider: tokens);

        var result = await client.GetUsersByIdsAsync([1, 2]);

        result.ShouldBeEmpty();
        handler.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task GetUsersByIds_returns_empty_when_keycloak_is_unreachable()
    {
        var tokens = Substitute.For<IServiceTokenProvider>();
        tokens.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new HttpRequestException("connection refused"));
        var (client, handler, _, _) = Build(_ => throw new InvalidOperationException("should not be called"), tokenProvider: tokens);

        var result = await client.GetUsersByIdsAsync([1]);

        result.ShouldBeEmpty();
        handler.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task GetUsersByIds_still_throws_on_non_success_from_auth_api()
    {
        // Çağıran yerler HttpRequestException'ı zaten yakalıyor; davranış korunur. 403'te ayrıca
        // yanlış yapılandırmayı işaret eden teşhis uyarısı loglanır.
        var (client, _, _, logger) = Build(_ => Task.FromResult(Json(HttpStatusCode.Forbidden, "{}")));

        await Should.ThrowAsync<HttpRequestException>(() => client.GetUsersByIdsAsync([1]));

        logger.Received(1).Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Is<object>(state => state.ToString()!.Contains("servis token'ını reddetti (403)") && state.ToString()!.Contains("exam-service")),
            Arg.Any<Exception?>(),
            Arg.Any<Func<object, Exception?, string>>());
    }

    [Fact]
    public async Task GetUsersByIds_returns_empty_when_keycloak_token_request_times_out()
    {
        // HttpClient zaman aşımı TaskCanceledException üretir; kullanıcı iptali değilse best-effort boş liste.
        var tokens = Substitute.For<IServiceTokenProvider>();
        tokens.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout"));
        var (client, handler, _, _) = Build(_ => throw new InvalidOperationException("should not be called"), tokenProvider: tokens);

        var result = await client.GetUsersByIdsAsync([1]);

        result.ShouldBeEmpty();
        handler.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task GetUsersByIds_propagates_caller_cancellation()
    {
        using var cts = new CancellationTokenSource();
        var tokens = Substitute.For<IServiceTokenProvider>();
        tokens.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns<string>(_ => { cts.Cancel(); throw new TaskCanceledException(); });
        var (client, _, _, _) = Build(_ => throw new InvalidOperationException("should not be called"), tokenProvider: tokens);

        await Should.ThrowAsync<TaskCanceledException>(() => client.GetUsersByIdsAsync([1], cts.Token));
    }

    [Fact]
    public async Task GetUsersByIds_with_empty_ids_makes_no_call_and_requests_no_token()
    {
        var (client, handler, tokens, _) = Build(_ => throw new InvalidOperationException("should not be called"));

        var result = await client.GetUsersByIdsAsync([]);

        result.ShouldBeEmpty();
        handler.Calls.ShouldBe(0);
        await tokens.DidNotReceive().GetAccessTokenAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetUserProfile_still_forwards_the_users_authorization_header()
    {
        var (client, handler, tokens, _) = Build(_ => Task.FromResult(Json(HttpStatusCode.OK, "{\"id\":5,\"keycloakId\":\"kc-5\",\"fullName\":\"Ben\"}")));

        var profile = await client.GetUserProfileAsync();

        profile.Id.ShouldBe(5);
        handler.LastRequest!.RequestUri!.ToString().ShouldBe("http://auth-api.test/api/auth/user-profile");
        handler.LastRequest.Headers.Authorization!.ToString().ShouldBe(UserBearer);
        await tokens.DidNotReceive().GetAccessTokenAsync(Arg.Any<CancellationToken>());
    }
}
