using System.Net;
using System.Text;
using System.Text.Json;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services;
using ExamApp.Api.Services.StudentReset;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
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
        public bool LastCallWasCancelled { get; private set; }

        public FakeHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            LastCallWasCancelled = cancellationToken.IsCancellationRequested;
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
        bool withUserRequest = true,
        HttpContext? requestContext = null)
    {
        var handler = new FakeHandler(respond);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(handler));

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["AuthApiBaseUrl"] = "http://auth-api.test" })
            .Build();

        var accessor = Substitute.For<IHttpContextAccessor>();
        if (requestContext is not null)
        {
            accessor.HttpContext.Returns(requestContext);
        }
        else if (withUserRequest)
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
    public async Task GetUsersByIds_does_not_ask_for_account_status()
    {
        var (client, handler, _, _) = Build(_ => Task.FromResult(Json(HttpStatusCode.OK, LookupBody((1, "A")))));

        var result = await client.GetUsersByIdsAsync([1]);

        result.Single().Enabled.ShouldBeNull();
        using var body = JsonDocument.Parse(handler.LastBody!);
        body.RootElement.TryGetProperty("IncludeAccountStatus", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task GetUsersWithAccountStatusByIds_sends_flag_and_maps_enabled()
    {
        // Issue #152: auth-api yanıtındaki "enabled" (camelCase, null olabilir) UserLookupResultDto.Enabled'a eşlenir.
        const string response = """
            [{"id":1,"keycloakId":"kc-1","fullName":"A","email":"a@test.local","avatar":"","role":"Teacher","enabled":true},
             {"id":2,"keycloakId":"kc-2","fullName":"B","email":"b@test.local","avatar":"","role":"Teacher","enabled":false},
             {"id":3,"keycloakId":"kc-3","fullName":"C","email":"c@test.local","avatar":"","role":"Teacher","enabled":null}]
            """;
        var (client, handler, _, _) = Build(_ => Task.FromResult(Json(HttpStatusCode.OK, response)));

        var result = await client.GetUsersWithAccountStatusByIdsAsync([1, 2, 3]);

        result.Single(u => u.Id == 1).Enabled.ShouldBe(true);
        result.Single(u => u.Id == 2).Enabled.ShouldBe(false);
        result.Single(u => u.Id == 3).Enabled.ShouldBeNull();
        handler.LastRequest!.RequestUri!.ToString().ShouldBe("http://auth-api.test/api/auth/users/lookup");
        handler.LastRequest.Headers.Authorization!.Parameter.ShouldBe(ServiceToken);
        using var body = JsonDocument.Parse(handler.LastBody!);
        body.RootElement.GetProperty("IncludeAccountStatus").GetBoolean().ShouldBeTrue();
        body.RootElement.GetProperty("UserIds").EnumerateArray().Select(e => e.GetInt32()).ShouldBe([1, 2, 3]);
    }

    [Fact]
    public async Task GetUsersWithAccountStatusByIds_returns_empty_when_service_token_cannot_be_obtained()
    {
        var tokens = Substitute.For<IServiceTokenProvider>();
        tokens.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new HttpRequestException("connection refused"));
        var (client, handler, _, _) = Build(_ => throw new InvalidOperationException("should not be called"), tokenProvider: tokens);

        (await client.GetUsersWithAccountStatusByIdsAsync([1])).ShouldBeEmpty();
        handler.Calls.ShouldBe(0);
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

    // ---- GetUserProfile: iletilen token = doğrulanan token (security review O1) ----

    private const string ProfileBody = "{\"id\":5,\"keycloakId\":\"kc-5\",\"fullName\":\"Ben\"}";
    private const string QueryToken = "ws-query-token-example";

    /// <summary>
    /// Kimliklenmiş istek taklidi: <paramref name="scheme"/> ile başarılı authenticate sonucu; <paramref name="savedToken"/>
    /// JwtBearer'ın <c>SaveToken</c> ile sakladığı (doğruladığı) token. <paramref name="scheme"/> null → kimliksiz istek.
    /// </summary>
    private static DefaultHttpContext AuthenticatedContext(string? scheme, string? savedToken = null, string? authorizationHeader = null)
    {
        var auth = Substitute.For<IAuthenticationService>();
        auth.AuthenticateAsync(Arg.Any<HttpContext>(), Arg.Any<string?>()).Returns(_ =>
        {
            if (scheme is null)
                return AuthenticateResult.NoResult();
            var properties = new AuthenticationProperties();
            if (savedToken is not null)
                properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = savedToken }]);
            var principal = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "kc-5")], scheme));
            return AuthenticateResult.Success(new AuthenticationTicket(principal, properties, scheme));
        });

        var context = new DefaultHttpContext
        {
            RequestServices = new ServiceCollection().AddSingleton(auth).BuildServiceProvider()
        };
        if (authorizationHeader is not null)
            context.Request.Headers.Authorization = authorizationHeader;
        return context;
    }

    private static DefaultHttpContext HubContext(string? savedToken)
    {
        var context = AuthenticatedContext(savedToken is null ? null : "Bearer", savedToken);
        context.Request.Path = "/hub/whiteboard";
        context.Request.QueryString = QueryString.Create(SignalRQueryToken.QueryParameter, savedToken ?? string.Empty);
        return context;
    }

    [Fact]
    public async Task GetUserProfile_still_forwards_the_users_authorization_header()
    {
        // JwtBearer kimliği, token saklanmamış (SaveToken kapalı) → doğrulanan token header'dakidir.
        var (client, handler, tokens, _) = Build(_ => Task.FromResult(Json(HttpStatusCode.OK, ProfileBody)),
            requestContext: AuthenticatedContext("Bearer", authorizationHeader: UserBearer));

        var profile = await client.GetUserProfileAsync();

        profile.Id.ShouldBe(5);
        handler.LastRequest!.RequestUri!.ToString().ShouldBe("http://auth-api.test/api/auth/user-profile");
        handler.LastRequest.Headers.Authorization!.ToString().ShouldBe(UserBearer);
        await tokens.DidNotReceive().GetAccessTokenAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetUserProfile_uses_the_validated_query_token_when_there_is_no_authorization_header()
    {
        var (client, handler, tokens, _) = Build(
            _ => Task.FromResult(Json(HttpStatusCode.OK, ProfileBody)), requestContext: HubContext(QueryToken));

        var profile = await client.GetUserProfileAsync();

        profile.Id.ShouldBe(5);
        handler.LastRequest!.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.ShouldBe(QueryToken);
        handler.LastRequest.Headers.GetValues("Authorization").ShouldHaveSingleItem();
        await tokens.DidNotReceive().GetAccessTokenAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetUserProfile_forwards_the_validated_token_not_a_different_authorization_header()
    {
        // Kimliği A belirledi (JwtBearer'ın sakladığı), header'da başka bir B var → auth-api'ye A gider.
        var context = AuthenticatedContext("Bearer", savedToken: "token-a-example", authorizationHeader: "Bearer token-b-example");
        var (client, handler, _, _) = Build(_ => Task.FromResult(Json(HttpStatusCode.OK, ProfileBody)), requestContext: context);

        await client.GetUserProfileAsync();

        handler.LastRequest!.Headers.Authorization!.Parameter.ShouldBe("token-a-example");
        handler.LastRequest.Headers.GetValues("Authorization").ShouldHaveSingleItem();
    }

    [Fact]
    public async Task GetUserProfile_does_not_forward_an_unvalidated_header_when_identity_is_not_from_jwt_bearer()
    {
        // /hangfire: kimlik cookie'den; header'daki token doğrulanmadı → iletilmez, açık hata.
        var context = AuthenticatedContext("HangfireCookie", authorizationHeader: UserBearer);
        var (client, handler, _, _) = Build(_ => throw new InvalidOperationException("should not be called"), requestContext: context);

        await Should.ThrowAsync<CallerAccessTokenMissingException>(() => client.GetUserProfileAsync());
        handler.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task GetUserProfile_without_any_caller_token_throws_a_clear_error_and_does_not_call_auth_api()
    {
        var (client, handler, _, _) = Build(
            _ => throw new InvalidOperationException("should not be called"), requestContext: HubContext(savedToken: null));

        await Should.ThrowAsync<CallerAccessTokenMissingException>(() => client.GetUserProfileAsync());
        handler.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task GetUserProfile_without_an_http_context_throws_a_clear_error()
    {
        var (client, handler, _, _) = Build(_ => throw new InvalidOperationException("should not be called"), withUserRequest: false);

        await Should.ThrowAsync<CallerAccessTokenMissingException>(() => client.GetUserProfileAsync());
        handler.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task GetUserProfile_passes_the_cancellation_token()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var (client, handler, _, _) = Build(_ => Task.FromResult(Json(HttpStatusCode.OK, ProfileBody)),
            requestContext: AuthenticatedContext("Bearer", savedToken: QueryToken));

        try { await client.GetUserProfileAsync(cts.Token); } catch (OperationCanceledException) { }

        // HttpClient çağıranın token'ını kendi zaman aşımıyla bağlar; iptal isteği handler'a ulaşmalı.
        handler.LastCallWasCancelled.ShouldBeTrue();
    }

    // ---- issue #156: fail-soft olmayan varyant ----

    [Fact]
    public async Task GetUsersByIdsOrThrow_throws_instead_of_empty_when_service_token_cannot_be_obtained()
    {
        var tokens = Substitute.For<IServiceTokenProvider>();
        tokens.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new InvalidOperationException("Keycloak client credentials missing"));
        var (client, handler, _, _) = Build(_ => throw new InvalidOperationException("should not be called"), tokenProvider: tokens);

        var ex = await Should.ThrowAsync<HttpRequestException>(() => client.GetUsersByIdsOrThrowAsync([1]));

        ex.InnerException.ShouldBeOfType<InvalidOperationException>();
        handler.Calls.ShouldBe(0);
    }

    [Fact]
    public async Task GetUsersByIdsOrThrow_returns_users_like_the_fail_soft_variant()
    {
        var (client, _, _, _) = Build(_ => Task.FromResult(Json(HttpStatusCode.OK, LookupBody((1, "A")))));

        var users = await client.GetUsersByIdsOrThrowAsync([1]);

        users.Single().KeycloakId.ShouldBe("kc-1");
    }
}
