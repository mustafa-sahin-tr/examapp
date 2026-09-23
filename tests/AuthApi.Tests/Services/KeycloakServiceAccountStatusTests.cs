using System.Net;
using System.Text;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services;
using Microsoft.Extensions.Options;

namespace AuthApi.Tests.Services;

/// <summary>
/// Issue #152: <see cref="KeycloakService.GetUsersEnabledAsync"/> — kullanıcı başı <c>GET /users/{id}</c>, <c>enabled</c>
/// doğru eşlenir; 404/5xx/bozuk yanıt/zaman aşımı olan kullanıcı sözlükte yer almaz (fail-soft, çağıran null'a çevirir);
/// Authorization istek bazında gönderilir; admin token alınamazsa <see cref="KeycloakException"/>.
/// </summary>
public class KeycloakServiceAccountStatusTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;
        private readonly object _gate = new();
        public List<HttpRequestMessage> Requests { get; } = new();
        public int InFlight;
        public int MaxInFlight;

        public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            lock (_gate) Requests.Add(request);
            var now = Interlocked.Increment(ref InFlight);
            lock (_gate) MaxInFlight = Math.Max(MaxInFlight, now);
            try
            {
                return await _respond(request, cancellationToken);
            }
            finally
            {
                Interlocked.Decrement(ref InFlight);
            }
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body)
        => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static bool IsToken(HttpRequestMessage req)
        => req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.EndsWith("/token");

    /// <summary>Dönen Handler = retry'sız admin named client'ın handler'ı (hesap durumu GET'leri buradan gider).</summary>
    private static (KeycloakService Service, FakeHandler Handler) Build(
        Func<string, CancellationToken, Task<HttpResponseMessage>> userResponse,
        Func<HttpResponseMessage>? tokenResponse = null,
        KeycloakAdminTokenCache? tokenCache = null)
    {
        var (service, adminHandler, _) = BuildWithDefaultClient(userResponse, tokenResponse, tokenCache);
        return (service, adminHandler);
    }

    /// <summary>
    /// Varsayılan client (token isteği; prod'da standart resilience'lı) ve retry'sız admin named client ayrı handler'larla,
    /// hesap durumu GET'lerinin admin client'tan gittiği doğrulanabilsin diye.
    /// </summary>
    private static (KeycloakService Service, FakeHandler AdminHandler, FakeHandler DefaultHandler) BuildWithDefaultClient(
        Func<string, CancellationToken, Task<HttpResponseMessage>> userResponse,
        Func<HttpResponseMessage>? tokenResponse = null,
        KeycloakAdminTokenCache? tokenCache = null)
    {
        Task<HttpResponseMessage> Respond(HttpRequestMessage req, CancellationToken ct)
        {
            if (IsToken(req))
                return Task.FromResult(tokenResponse?.Invoke()
                    ?? Json(HttpStatusCode.OK, """{"access_token":"admin-token-example","expires_in":300}"""));
            var id = Uri.UnescapeDataString(req.RequestUri!.AbsolutePath.Split('/').Last());
            return userResponse(id, ct);
        }
        var adminHandler = new FakeHandler(Respond);
        var defaultHandler = new FakeHandler(Respond);
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(Arg.Any<string>()).Returns(_ => new HttpClient(defaultHandler, disposeHandler: false));
        factory.CreateClient(KeycloakService.AdminHttpClientName).Returns(_ => new HttpClient(adminHandler, disposeHandler: false));
        var settings = new KeycloakSettings
        {
            Host = "http://keycloak.test",
            TokenUrl = "realms/exam-realm/protocol/openid-connect/token",
            UserUrl = "admin/realms/exam-realm/users",
            AdminClientId = "exam-admin",
            AdminClientSecret = "example-secret-value"
        };
        return (new KeycloakService(factory, Options.Create(settings), tokenCache), adminHandler, defaultHandler);
    }

    [Fact]
    public async Task Maps_enabled_per_user_and_skips_unreadable_users()
    {
        var (service, handler) = Build((id, _) => Task.FromResult(id switch
        {
            "kc-on" => Json(HttpStatusCode.OK, """{"id":"kc-on","username":"on","enabled":true}"""),
            "kc-off" => Json(HttpStatusCode.OK, """{"id":"kc-off","username":"off","enabled":false}"""),
            "kc-500" => Json(HttpStatusCode.InternalServerError, "{}"),
            "kc-bad" => Json(HttpStatusCode.OK, "not-json"),
            "kc-noflag" => Json(HttpStatusCode.OK, """{"id":"kc-noflag"}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        }));

        var result = await service.GetUsersEnabledAsync(["kc-on", "kc-off", "kc-missing", "kc-500", "kc-bad", "kc-noflag", "kc-on"]);

        result.Count.ShouldBe(2);
        result["kc-on"].ShouldBeTrue();
        result["kc-off"].ShouldBeFalse();

        var userGets = handler.Requests.Where(r => r.Method == HttpMethod.Get).ToList();
        userGets.Count.ShouldBe(6); // tekrarlanan id bir kez okunur
        userGets.ShouldAllBe(r => r.RequestUri!.AbsolutePath.StartsWith("/admin/realms/exam-realm/users/"));
        userGets.ShouldAllBe(r => r.Headers.Authorization!.Scheme == "Bearer" && r.Headers.Authorization.Parameter == "admin-token-example");
    }

    [Fact]
    public async Task Status_reads_use_the_retryless_admin_client_and_token_comes_from_default_client()
    {
        var (service, adminHandler, defaultHandler) = BuildWithDefaultClient((id, _) =>
            Task.FromResult(Json(HttpStatusCode.OK, $$"""{"id":"{{id}}","enabled":true}""")));

        await service.GetUsersEnabledAsync(["kc-1", "kc-2"]);

        adminHandler.Requests.Count(r => r.Method == HttpMethod.Get).ShouldBe(2);
        adminHandler.Requests.Count(IsToken).ShouldBe(0);
        defaultHandler.Requests.Count(IsToken).ShouldBe(1);
        defaultHandler.Requests.Count(r => r.Method == HttpMethod.Get).ShouldBe(0);
    }

    private static Exception Rejection(string kind) => kind == "timeout"
        ? new Polly.Timeout.TimeoutRejectedException("attempt timeout")
        : new Polly.CircuitBreaker.BrokenCircuitException("circuit open");

    [Theory]
    [InlineData("timeout")]
    [InlineData("circuit")]
    public async Task Resilience_rejection_for_one_user_skips_only_that_user(string kind)
    {
        var rejection = Rejection(kind);
        var (service, _) = Build((id, _) => id == "kc-bad"
            ? Task.FromException<HttpResponseMessage>(rejection)
            : Task.FromResult(Json(HttpStatusCode.OK, $$"""{"id":"{{id}}","enabled":false}""")));

        var result = await service.GetUsersEnabledAsync(["kc-ok", "kc-bad"]);

        result.Keys.ShouldBe(["kc-ok"]);
        result["kc-ok"].ShouldBeFalse();
    }

    private static Func<HttpResponseMessage> CountingToken(Action onRequest) => () =>
    {
        onRequest();
        return Json(HttpStatusCode.OK, """{"access_token":"admin-token-example","expires_in":300}""");
    };

    private static Task<HttpResponseMessage> EnabledOk(string id, CancellationToken _)
        => Task.FromResult(Json(HttpStatusCode.OK, $$"""{"id":"{{id}}","enabled":true}"""));

    [Fact]
    public async Task Admin_token_is_shared_across_service_instances_via_the_singleton_cache()
    {
        var cache = new KeycloakAdminTokenCache();
        var tokenRequests = 0;
        var token = CountingToken(() => Interlocked.Increment(ref tokenRequests));

        var (first, _) = Build(EnabledOk, token, cache);
        var (second, _) = Build(EnabledOk, token, cache); // ayrı scope / HTTP isteği
        await first.GetUsersEnabledAsync(["kc-1"]);
        await second.GetUsersEnabledAsync(["kc-2"]);

        tokenRequests.ShouldBe(1);
    }

    [Fact]
    public async Task Without_a_shared_cache_each_instance_caches_its_own_token_as_before()
    {
        var tokenRequests = 0;
        var token = CountingToken(() => Interlocked.Increment(ref tokenRequests));

        var (first, _) = Build(EnabledOk, token);
        var (second, _) = Build(EnabledOk, token);
        await first.GetUsersEnabledAsync(["kc-1"]);
        await first.GetUsersEnabledAsync(["kc-3"]); // aynı örnek → önbellekten
        await second.GetUsersEnabledAsync(["kc-2"]);

        tokenRequests.ShouldBe(2);
    }

    [Fact]
    public async Task Parallelism_is_bounded()
    {
        var (service, handler) = Build(async (id, ct) =>
        {
            await Task.Delay(20, ct);
            return Json(HttpStatusCode.OK, $$"""{"id":"{{id}}","enabled":true}""");
        });
        var ids = Enumerable.Range(1, 40).Select(i => "kc-" + i).ToList();

        var result = await service.GetUsersEnabledAsync(ids);

        result.Count.ShouldBe(40);
        handler.MaxInFlight.ShouldBeLessThanOrEqualTo(KeycloakService.AccountStatusMaxParallelism + 1); // +1: token isteği
    }

    [Fact]
    public async Task Cancellation_mid_way_returns_what_was_read_so_far_without_throwing()
    {
        using var cts = new CancellationTokenSource();
        var (service, _) = Build(async (id, ct) =>
        {
            if (id != "kc-fast")
                await Task.Delay(Timeout.Infinite, ct);
            return Json(HttpStatusCode.OK, $$"""{"id":"{{id}}","enabled":true}""");
        });
        cts.CancelAfter(TimeSpan.FromMilliseconds(200));

        var result = await service.GetUsersEnabledAsync(["kc-fast", "kc-slow-1", "kc-slow-2"], cts.Token);

        result.Keys.ShouldBe(["kc-fast"]);
    }

    [Fact]
    public async Task Empty_input_makes_no_calls()
    {
        var (service, handler) = Build((_, _) => throw new InvalidOperationException("should not be called"));

        (await service.GetUsersEnabledAsync([" ", ""])).ShouldBeEmpty();
        handler.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Admin_token_failure_throws_keycloak_exception()
    {
        var (service, _) = Build(
            (_, _) => throw new InvalidOperationException("should not be called"),
            tokenResponse: () => Json(HttpStatusCode.Unauthorized, """{"error":"unauthorized_client"}"""));

        await Should.ThrowAsync<KeycloakException>(() => service.GetUsersEnabledAsync(["kc-1"]));
    }
}
