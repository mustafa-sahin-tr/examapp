using System.Net;
using System.Text;
using System.Text.Json;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services;
using ExamApp.Api.Tests.Support;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Issue #156: exam API KeycloakService — reset-password gövdesi, logout, etkin realm + realm-management client
/// rolleri, admin token'ın yeniden kullanımı; hata mesajları/loglar Keycloak yanıt gövdesini / şifreyi içermez.
/// </summary>
public class KeycloakServicePasswordResetTests
{
    private const string Sub = "0b9c7c4e-1a2b-4c3d-8e9f-001122334455";
    private const string ServiceAccountId = "5a5a5a5a-0000-4000-8000-000000000001";
    private const string RealmMgmtUuid = "01ee7203-9cf6-49e7-b4f3-1b0cfeac1fcb";

    // realm-management UUID'si Host başına süreç içinde önbelleklenir → her test kendi Host'unu kullanır.
    private readonly string _host = $"http://kc-{Guid.NewGuid():N}";
    private readonly List<string> _requestBodies = new();

    private string UsersUrl => $"{_host}/admin/realms/exam-realm/users";

    private KeycloakSettings Settings => new()
    {
        Host = _host,
        UserUrl = "admin/realms/exam-realm/users",
        TokenUrl = "realms/exam-realm/protocol/openid-connect/token",
        AdminClientId = "exam-admin"
    };

    private static string FakeJwt(string sub)
    {
        static string B64(string json) => Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        return $"{B64("{\"alg\":\"none\"}")}.{B64($"{{\"sub\":\"{sub}\"}}")}.sig";
    }

    private static HttpResponseMessage Ok(string json) => new(HttpStatusCode.OK) { Content = new StringContent(json) };

    private StubHttp Stub(Func<HttpRequestMessage, HttpResponseMessage> onAdminCall, string? tokenResponse = null) => new(request =>
    {
        _requestBodies.Add(request.Content?.ReadAsStringAsync().GetAwaiter().GetResult() ?? string.Empty);
        if (request.RequestUri!.AbsolutePath.EndsWith("/token"))
            return Ok(tokenResponse ?? $"{{\"access_token\":\"{FakeJwt(ServiceAccountId)}\",\"expires_in\":300}}");
        return onAdminCall(request);
    });

    private KeycloakService Service(StubHttp http, ILogger<KeycloakService>? logger = null)
        => new(http, Options.Create(Settings), logger ?? NullLogger<KeycloakService>.Instance);

    private static int TokenCalls(StubHttp http) => http.Requests.Count(r => r.RequestUri!.AbsolutePath.EndsWith("/token"));

    [Fact]
    public async Task ResetPassword_puts_temporary_password_with_admin_token_and_returns_it()
    {
        var http = Stub(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        var issued = await Service(http).ResetPasswordAsync(Sub);

        issued.Length.ShouldBe(16);
        var put = http.Requests.Single(r => r.Method == HttpMethod.Put);
        put.RequestUri!.ToString().ShouldBe($"{UsersUrl}/{Sub}/reset-password");
        put.Headers.Authorization!.Scheme.ShouldBe("Bearer");

        using var body = JsonDocument.Parse(_requestBodies[http.Requests.IndexOf(put)]);
        body.RootElement.GetProperty("type").GetString().ShouldBe("password");
        body.RootElement.GetProperty("value").GetString().ShouldBe(issued);
        body.RootElement.GetProperty("temporary").GetBoolean().ShouldBeTrue();
    }

    [Fact]
    public async Task ResetPassword_failure_throws_status_only_and_never_logs_body_or_password()
    {
        var http = Stub(_ => new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = new StringContent("{\"error\":\"invalidPasswordMinSpecialCharsMessage\",\"echo\":\"SENSITIVE-BODY\"}")
        });
        var logger = new CapturingLogger<KeycloakService>();

        var ex = await Should.ThrowAsync<KeycloakException>(() => Service(http, logger).ResetPasswordAsync(Sub));

        ex.StatusCode.ShouldBe(400);
        ex.Message.ShouldNotContain("SENSITIVE-BODY");
        var sentValue = JsonDocument.Parse(_requestBodies.Last()).RootElement.GetProperty("value").GetString()!;
        ex.Message.ShouldNotContain(sentValue);
        logger.Entries.ShouldAllBe(e => !e.Contains(sentValue) && !e.Contains("SENSITIVE-BODY"));
    }

    [Fact]
    public async Task ResetPassword_keycloak_404_surfaces_status_404()
    {
        var http = Stub(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        (await Should.ThrowAsync<KeycloakException>(() => Service(http).ResetPasswordAsync(Sub))).StatusCode.ShouldBe(404);
    }

    [Fact]
    public async Task LogoutUserSessions_posts_to_user_logout()
    {
        var http = Stub(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        await Service(http).LogoutUserSessionsAsync(Sub);

        var post = http.Requests.Single(r => r.RequestUri!.AbsolutePath.EndsWith("/logout"));
        post.Method.ShouldBe(HttpMethod.Post);
        post.RequestUri!.ToString().ShouldBe($"{UsersUrl}/{Sub}/logout");
    }

    [Fact]
    public async Task LogoutUserSessions_failure_throws_KeycloakException()
    {
        var http = Stub(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));

        (await Should.ThrowAsync<KeycloakException>(() => Service(http).LogoutUserSessionsAsync(Sub))).StatusCode.ShouldBe(500);
    }

    // ---- issue #155: SetEnabledAsync (GET tam temsil → enabled → PUT) ----

    private const string UserJson =
        "{\"id\":\"" + Sub + "\",\"username\":\"ayse\",\"email\":\"ayse@example.test\",\"firstName\":\"Ayşe\"," +
        "\"lastName\":\"Yılmaz\",\"enabled\":true,\"emailVerified\":true,\"attributes\":{\"school_id\":[\"5\"]}," +
        "\"userProfileMetadata\":{\"attributes\":[]},\"access\":{\"manage\":true}}";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SetEnabled_gets_full_representation_and_puts_it_back_with_only_enabled_changed(bool enabled)
    {
        var http = Stub(r => r.Method == HttpMethod.Get ? Ok(UserJson) : new HttpResponseMessage(HttpStatusCode.NoContent));

        await Service(http).SetEnabledAsync(Sub, enabled);

        var get = http.Requests.Single(r => r.Method == HttpMethod.Get);
        get.RequestUri!.ToString().ShouldBe($"{UsersUrl}/{Sub}");
        var put = http.Requests.Single(r => r.Method == HttpMethod.Put);
        put.RequestUri!.ToString().ShouldBe($"{UsersUrl}/{Sub}");
        put.Headers.Authorization!.Scheme.ShouldBe("Bearer");

        using var body = JsonDocument.Parse(_requestBodies[http.Requests.IndexOf(put)]);
        var root = body.RootElement;
        root.GetProperty("enabled").GetBoolean().ShouldBe(enabled);
        root.GetProperty("email").GetString().ShouldBe("ayse@example.test");
        root.GetProperty("firstName").GetString().ShouldBe("Ayşe");
        root.GetProperty("lastName").GetString().ShouldBe("Yılmaz");
        root.GetProperty("emailVerified").GetBoolean().ShouldBeTrue();
        root.GetProperty("attributes").GetProperty("school_id")[0].GetString().ShouldBe("5");
        root.TryGetProperty("userProfileMetadata", out _).ShouldBeFalse();
        root.TryGetProperty("access", out _).ShouldBeFalse();
    }

    [Fact]
    public async Task SetEnabled_user_missing_on_get_throws_404_and_does_not_put()
    {
        var http = Stub(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        (await Should.ThrowAsync<KeycloakException>(() => Service(http).SetEnabledAsync(Sub, false))).StatusCode.ShouldBe(404);
        http.Requests.ShouldNotContain(r => r.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task SetEnabled_put_failure_throws_status_only_without_body()
    {
        var http = Stub(r => r.Method == HttpMethod.Get
            ? Ok(UserJson)
            : new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = new StringContent("{\"echo\":\"SENSITIVE-BODY\"}") });
        var logger = new CapturingLogger<KeycloakService>();

        var ex = await Should.ThrowAsync<KeycloakException>(() => Service(http, logger).SetEnabledAsync(Sub, false));

        ex.StatusCode.ShouldBe(400);
        ex.Message.ShouldNotContain("SENSITIVE-BODY");
        logger.Entries.ShouldAllBe(e => !e.Contains("SENSITIVE-BODY"));
    }

    [Fact]
    public async Task GetUserRoles_reads_realm_composite_and_realm_management_composite_via_service_account_uuid()
    {
        var http = Stub(r => r.RequestUri!.AbsolutePath switch
        {
            var p when p.EndsWith($"/users/{Sub}/role-mappings/realm/composite") => Ok("[{\"id\":\"1\",\"name\":\"Teacher\"}]"),
            var p when p.EndsWith($"/users/{ServiceAccountId}/role-mappings") =>
                Ok($"{{\"clientMappings\":{{\"realm-management\":{{\"id\":\"{RealmMgmtUuid}\",\"client\":\"realm-management\",\"mappings\":[{{\"id\":\"x\",\"name\":\"manage-users\"}}]}}}}}}"),
            var p when p.EndsWith($"/users/{Sub}/role-mappings/clients/{RealmMgmtUuid}/composite") => Ok("[{\"id\":\"2\",\"name\":\"realm-admin\"}]"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });
        var service = Service(http);

        var roles = await service.GetUserRolesAsync(Sub);
        roles.RealmRoles.ShouldBe(["Teacher"]);
        roles.RealmManagementRoles.ShouldBe(["realm-admin"]);

        // UUID önbellekte: ikinci çağrı servis hesabı mapping'ini tekrar okumaz; admin token da tek kez alınır.
        await service.GetUserRolesAsync(Sub);
        http.Requests.Count(r => r.RequestUri!.AbsolutePath.EndsWith($"/users/{ServiceAccountId}/role-mappings")).ShouldBe(1);
        TokenCalls(http).ShouldBe(1);
    }

    [Fact]
    public async Task GetUserRoles_falls_back_to_direct_client_mappings_when_uuid_cannot_be_resolved()
    {
        var http = Stub(r => r.RequestUri!.AbsolutePath switch
        {
            var p when p.EndsWith("/role-mappings/realm/composite") => Ok("[]"),
            var p when p.EndsWith($"/users/{ServiceAccountId}/role-mappings") => new HttpResponseMessage(HttpStatusCode.Forbidden),
            var p when p.EndsWith($"/users/{Sub}/role-mappings") =>
                Ok("{\"clientMappings\":{\"realm-management\":{\"id\":\"u\",\"mappings\":[{\"id\":\"x\",\"name\":\"manage-users\"}]}}}"),
            _ => new HttpResponseMessage(HttpStatusCode.NotFound)
        });

        var roles = await Service(http).GetUserRolesAsync(Sub);

        roles.RealmManagementRoles.ShouldBe(["manage-users"]);
    }

    [Fact]
    public async Task GetUserRoles_unknown_user_is_KeycloakException_404()
    {
        var http = Stub(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        (await Should.ThrowAsync<KeycloakException>(() => Service(http).GetUserRolesAsync(Sub))).StatusCode.ShouldBe(404);
    }

    [Fact]
    public async Task Admin_token_is_fetched_once_per_instance_for_roles_reset_and_logout()
    {
        var http = Stub(r => r.Method == HttpMethod.Get
            ? Ok(r.RequestUri!.AbsolutePath.EndsWith($"/users/{ServiceAccountId}/role-mappings")
                ? $"{{\"clientMappings\":{{\"realm-management\":{{\"id\":\"{RealmMgmtUuid}\",\"mappings\":[]}}}}}}"
                : "[]")
            : new HttpResponseMessage(HttpStatusCode.NoContent));
        var service = Service(http);

        await service.GetUserRolesAsync(Sub);
        await service.ResetPasswordAsync(Sub);
        await service.LogoutUserSessionsAsync(Sub);

        TokenCalls(http).ShouldBe(1);
    }

    [Theory]
    [InlineData("{\"token_type\":\"Bearer\"}")]
    [InlineData("{\"access_token\":\"\"}")]
    public async Task Token_response_without_access_token_is_KeycloakException_502(string tokenResponse)
    {
        var http = Stub(_ => new HttpResponseMessage(HttpStatusCode.NoContent), tokenResponse);

        var ex = await Should.ThrowAsync<KeycloakException>(() => Service(http).ResetPasswordAsync(Sub));

        ex.StatusCode.ShouldBe(502);
        http.Requests.ShouldNotContain(r => r.Method == HttpMethod.Put);
    }

    [Fact]
    public async Task User_id_is_path_escaped_and_empty_id_is_rejected()
    {
        var http = Stub(_ => new HttpResponseMessage(HttpStatusCode.NoContent));

        await Service(http).LogoutUserSessionsAsync("../roles");
        http.Requests.Last().RequestUri!.AbsolutePath.ShouldBe("/admin/realms/exam-realm/users/..%2Froles/logout");

        await Should.ThrowAsync<KeycloakException>(() => Service(http).ResetPasswordAsync(" "));
    }
}

/// <summary>Formatlanmış mesaj + exception metnini toplayan test logger'ı.</summary>
public sealed class CapturingLogger<T> : ILogger<T>
{
    public List<string> Entries { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
        => Entries.Add($"{logLevel}: {formatter(state, exception)} {exception}");
}
