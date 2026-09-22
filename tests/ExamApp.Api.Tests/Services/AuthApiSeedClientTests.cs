using System.Net;
using System.Text;
using System.Text.Json;
using ExamApp.Api.Services.StudentReset;
using ExamApp.Api.Services.Teachers.Seed;
using ExamApp.Foundation.Contracts;
using Microsoft.Extensions.Configuration;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// seed-teachers auth-api istemcisi (issue #217): sahte <see cref="HttpMessageHandler"/> ile istek şekli,
/// servis token'ı, 404/401/403/500 açıklayıcı hata, bozuk JSON, BaseUrl eksik, zaman aşımı.
/// </summary>
public class AuthApiSeedClientTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }

        public FakeHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond) => _respond = respond;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            return await _respond(request, cancellationToken);
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body) =>
        new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (AuthApiSeedClient Client, FakeHandler Handler) Build(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond,
        string? baseUrl = "http://auth-api.test",
        TimeSpan? timeout = null,
        IServiceTokenProvider? tokenProvider = null)
    {
        var handler = new FakeHandler(respond);
        var http = new HttpClient(handler) { Timeout = timeout ?? TimeSpan.FromSeconds(100) };
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient(AuthApiSeedClient.HttpClientName).Returns(http);

        var values = new Dictionary<string, string?>();
        if (baseUrl is not null) values[AuthApiSeedClient.BaseUrlConfigKey] = baseUrl;
        var config = new ConfigurationBuilder().AddInMemoryCollection(values).Build();

        var tokens = tokenProvider ?? Substitute.For<IServiceTokenProvider>();
        if (tokenProvider is null)
            tokens.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("svc-token-example");

        return (new AuthApiSeedClient(factory, config, tokens), handler);
    }

    private static DevSeedUsersRequest Request() => new()
    {
        Password = "example-pw-123",
        Users = [new DevSeedUserItem { Email = "seed.t.1.matematik.1@seed.examapp.local", FirstName = "A", LastName = "B", SchoolId = 7 }]
    };

    [Fact]
    public async Task Cleanup_posts_to_cleanup_url_with_dry_run_and_exclude_ids_in_body()
    {
        var responseBody = JsonSerializer.Serialize(new DevSeedCleanupResponse
        {
            DryRun = true, KeycloakMissing = 1,
            Users = [new DevSeedCleanupUser { Email = "seed.i.kars.matematik.1@seed.examapp.local", UserId = 42, IdentityIds = [42], KeycloakStatus = "Planned", IdentityStatus = "Planned" }]
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var (client, handler) = Build((_, _) => Task.FromResult(Json(HttpStatusCode.OK, responseBody)));

        var result = await client.CleanupSeedUsersAsync(new DevSeedCleanupRequest { DryRun = true, ExcludeUserIds = [7, 9] });

        result.DryRun.ShouldBeTrue();
        result.Users.Single().UserId.ShouldBe(42);
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Post);
        handler.LastRequest.RequestUri!.ToString().ShouldBe("http://auth-api.test/api/auth/dev/seed-users/cleanup");
        handler.LastRequest.Headers.Authorization!.Parameter.ShouldBe("svc-token-example");
        using var body = JsonDocument.Parse(handler.LastBody!);
        body.RootElement.GetProperty("dryRun").GetBoolean().ShouldBeTrue();
        body.RootElement.GetProperty("excludeUserIds").EnumerateArray().Select(e => e.GetInt32()).ShouldBe([7, 9]);
    }

    [Fact]
    public async Task Cleanup_timeout_hint_talks_about_rerun_not_batch_size()
    {
        var (client, _) = Build(async (_, ct) => { await Task.Delay(Timeout.Infinite, ct); throw new InvalidOperationException(); }, timeout: TimeSpan.FromMilliseconds(50));

        var ex = await Should.ThrowAsync<TeacherSeedAuthApiException>(() => client.CleanupSeedUsersAsync(new DevSeedCleanupRequest { DryRun = false }));

        ex.Message.ShouldContain("zaman aşımı");
        ex.Message.ShouldContain("tekrar koşu");
        ex.Message.ShouldNotContain("--batch-size");
    }

    [Fact]
    public async Task Posts_json_with_bearer_service_token_and_parses_response()
    {
        var responseBody = JsonSerializer.Serialize(new DevSeedUsersResponse
        {
            Mode = "admin-api", KeycloakElapsedMs = 12, IdentityDbElapsedMs = 3,
            Results = [new DevSeedUserResult { Email = "seed.t.1.matematik.1@seed.examapp.local", KeycloakId = "kc-1", UserId = 99, KeycloakStatus = "Created", IdentityStatus = "Created" }]
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var (client, handler) = Build((_, _) => Task.FromResult(Json(HttpStatusCode.OK, responseBody)));

        var result = await client.SeedUsersAsync(Request());

        result.Results.Single().UserId.ShouldBe(99);
        result.KeycloakElapsedMs.ShouldBe(12);
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Post);
        handler.LastRequest.RequestUri!.ToString().ShouldBe("http://auth-api.test/api/auth/dev/seed-users");
        handler.LastRequest.Headers.Authorization!.Scheme.ShouldBe("Bearer");
        handler.LastRequest.Headers.Authorization.Parameter.ShouldBe("svc-token-example");
        handler.LastBody.ShouldContain("\"schoolId\":7");
        handler.LastBody.ShouldContain("\"role\":\"Teacher\"");
        handler.LastBody.ShouldNotContain("attributes");
    }

    [Fact]
    public async Task Trailing_slash_in_base_url_is_tolerated()
    {
        var (client, handler) = Build((_, _) => Task.FromResult(Json(HttpStatusCode.OK, "{\"results\":[]}")), baseUrl: "http://auth-api.test/");

        await client.SeedUsersAsync(Request());

        handler.LastRequest!.RequestUri!.ToString().ShouldBe("http://auth-api.test/api/auth/dev/seed-users");
    }

    [Fact]
    public async Task Missing_base_url_fails_before_any_http_call()
    {
        var (client, handler) = Build((_, _) => throw new InvalidOperationException("should not be called"), baseUrl: null);

        var ex = await Should.ThrowAsync<TeacherSeedAuthApiException>(() => client.SeedUsersAsync(Request()));

        ex.Message.ShouldContain(AuthApiSeedClient.BaseUrlConfigKey);
        handler.LastRequest.ShouldBeNull();
    }

    [Fact]
    public async Task Token_provider_failure_is_wrapped()
    {
        var tokens = Substitute.For<IServiceTokenProvider>();
        tokens.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns<string>(_ => throw new InvalidOperationException("Keycloak client credentials missing"));
        var (client, handler) = Build((_, _) => throw new InvalidOperationException("should not be called"), tokenProvider: tokens);

        var ex = await Should.ThrowAsync<TeacherSeedAuthApiException>(() => client.SeedUsersAsync(Request()));

        ex.Message.ShouldContain("Servis token");
        ex.Message.ShouldContain("client credentials missing");
        handler.LastRequest.ShouldBeNull();
    }

    [Fact]
    public async Task Not_found_explains_environment_or_stale_build()
    {
        var (client, _) = Build((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound)));

        var ex = await Should.ThrowAsync<TeacherSeedAuthApiException>(() => client.SeedUsersAsync(Request()));

        ex.Message.ShouldContain("404");
        ex.Message.ShouldContain("Development/Staging");
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task Unauthorized_and_forbidden_point_at_service_client_config(HttpStatusCode code)
    {
        var (client, _) = Build((_, _) => Task.FromResult(new HttpResponseMessage(code)));

        var ex = await Should.ThrowAsync<TeacherSeedAuthApiException>(() => client.SeedUsersAsync(Request()));

        ex.Message.ShouldContain(((int)code).ToString());
        ex.Message.ShouldContain("ServiceClients");
    }

    [Fact]
    public async Task Server_error_body_is_truncated_to_500_chars()
    {
        var longBody = new string('x', 2000);
        var (client, _) = Build((_, _) => Task.FromResult(Json(HttpStatusCode.InternalServerError, longBody)));

        var ex = await Should.ThrowAsync<TeacherSeedAuthApiException>(() => client.SeedUsersAsync(Request()));

        ex.Message.ShouldContain("500");
        ex.Message.Length.ShouldBeLessThan(700);
        ex.Message.ShouldContain("…");
    }

    [Fact]
    public async Task Bad_request_message_is_surfaced()
    {
        var (client, _) = Build((_, _) => Task.FromResult(Json(HttpStatusCode.BadRequest, "{\"message\":\"Yalnızca seed alanı e-postaları kabul edilir\"}")));

        var ex = await Should.ThrowAsync<TeacherSeedAuthApiException>(() => client.SeedUsersAsync(Request()));

        ex.Message.ShouldContain("400");
        ex.Message.ShouldContain("seed alanı");
    }

    [Fact]
    public async Task Malformed_json_is_wrapped()
    {
        var (client, _) = Build((_, _) => Task.FromResult(Json(HttpStatusCode.OK, "{not json")));

        var ex = await Should.ThrowAsync<TeacherSeedAuthApiException>(() => client.SeedUsersAsync(Request()));

        ex.Message.ShouldContain("çözümlenemedi");
        ex.InnerException.ShouldBeOfType<JsonException>();
    }

    [Fact]
    public async Task Connection_failure_is_wrapped_with_url()
    {
        var (client, _) = Build((_, _) => throw new HttpRequestException("No connection could be made"));

        var ex = await Should.ThrowAsync<TeacherSeedAuthApiException>(() => client.SeedUsersAsync(Request()));

        ex.Message.ShouldContain("ulaşılamadı");
        ex.Message.ShouldContain("http://auth-api.test/api/auth/dev/seed-users");
    }

    [Fact]
    public async Task Http_client_timeout_becomes_explanatory_error_with_batch_hint()
    {
        var (client, _) = Build(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(5), ct); // HttpClient.Timeout iptal eder
            return Json(HttpStatusCode.OK, "{}");
        }, timeout: TimeSpan.FromMilliseconds(100));

        var ex = await Should.ThrowAsync<TeacherSeedAuthApiException>(() => client.SeedUsersAsync(Request()));

        ex.Message.ShouldContain("zaman aşımı");
        ex.Message.ShouldContain("--batch-size");
        ex.Message.ShouldContain("1 hesap");
    }

    [Fact]
    public async Task User_cancellation_propagates_as_cancellation_not_timeout_error()
    {
        using var cts = new CancellationTokenSource();
        var (client, _) = Build(async (_, ct) =>
        {
            cts.Cancel();
            await Task.Delay(TimeSpan.FromSeconds(5), ct);
            return Json(HttpStatusCode.OK, "{}");
        });

        await Should.ThrowAsync<OperationCanceledException>(() => client.SeedUsersAsync(Request(), cts.Token));
    }
}
