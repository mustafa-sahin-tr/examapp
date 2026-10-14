using System.Net;
using System.Text;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services;
using Microsoft.Extensions.Options;

namespace AuthApi.Tests.Services;

/// <summary>
/// Issue #240: Keycloak kullanıcı oluşturma 409 (kullanıcı adı/e-posta çakışması) ile reddederse
/// <see cref="KeycloakFailureKind.Conflict"/> olarak sınıflandırılır ki register bunu "zaten kayıtlı" diye
/// sızdırmadan genel kabul yanıtına eşleyebilsin. Sahte <see cref="HttpMessageHandler"/> ile
/// (KeycloakServiceSearchTests ile aynı yaklaşım).
/// </summary>
public class KeycloakServiceCreateUserTests
{
    private const string ExamplePassword = "pw-example-123"; // example credential, test-only

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(_respond(request));
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body)
        => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static KeycloakService Build(Func<HttpRequestMessage, HttpResponseMessage> respondToCreate)
    {
        var handler = new FakeHandler(req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.EndsWith("/token"))
                return Json(HttpStatusCode.OK, """{"access_token":"admin-token-example","expires_in":300}""");
            return respondToCreate(req);
        });
        var factory = Substitute.For<IHttpClientFactory>();
        factory.CreateClient().Returns(new HttpClient(handler));
        factory.CreateClient(Arg.Any<string>()).Returns(new HttpClient(handler));
        var settings = new KeycloakSettings
        {
            Host = "http://keycloak.test",
            TokenUrl = "realms/exam-realm/protocol/openid-connect/token",
            UserUrl = "admin/realms/exam-realm/users",
            RealmRolesUrl = "admin/realms/exam-realm/roles",
            AdminClientId = "exam-admin",
            AdminClientSecret = "example-secret-value"
        };
        return new KeycloakService(factory, Options.Create(settings));
    }

    [Fact]
    public async Task CreateUser_409_is_classified_as_Conflict()
    {
        var service = Build(_ => Json(HttpStatusCode.Conflict, """{"errorMessage":"User exists with same username"}"""));

        var ex = await Should.ThrowAsync<KeycloakException>(() =>
            service.CreateUserAsync("taken@test.local", ExamplePassword, "taken@test.local", "A", "B"));

        ex.Kind.ShouldBe(KeycloakFailureKind.Conflict);
        ex.StatusCode.ShouldBe(409);
    }

    [Fact]
    public async Task CreateUser_400_is_classified_as_Validation()
    {
        var service = Build(_ => Json(HttpStatusCode.BadRequest, """{"errorMessage":"invalidPasswordMinLengthMessage"}"""));

        var ex = await Should.ThrowAsync<KeycloakException>(() =>
            service.CreateUserAsync("new@test.local", ExamplePassword, "new@test.local", "A", "B"));

        ex.Kind.ShouldBe(KeycloakFailureKind.Validation);
        ex.StatusCode.ShouldBe(400);
    }

    [Fact]
    public async Task CreateUser_other_failure_stays_Unexpected()
    {
        var service = Build(_ => Json(HttpStatusCode.InternalServerError, """{"error":"unknown_error"}"""));

        var ex = await Should.ThrowAsync<KeycloakException>(() =>
            service.CreateUserAsync("new@test.local", ExamplePassword, "new@test.local", "A", "B"));

        ex.Kind.ShouldBe(KeycloakFailureKind.Unexpected);
    }

    [Fact]
    public async Task CreateUser_success_returns_id_from_location_header()
    {
        var service = Build(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Created);
            response.Headers.Location = new Uri("http://keycloak.test/admin/realms/exam-realm/users/kc-123");
            return response;
        });

        var id = await service.CreateUserAsync("new@test.local", ExamplePassword, "new@test.local", "A", "B");

        id.ShouldBe("kc-123");
    }

    // ---- Issue #438: veli tarafından açılan çocuk hesabı ----

    [Fact]
    public async Task CreateParentManagedUser_sends_no_email_no_credentials_no_required_actions_and_the_marker()
    {
        string? body = null;
        var service = Build(req =>
        {
            body = req.Content!.ReadAsStringAsync().GetAwaiter().GetResult();
            var response = new HttpResponseMessage(HttpStatusCode.Created);
            response.Headers.Location = new Uri("http://keycloak.test/admin/realms/exam-realm/users/kc-child");
            return response;
        });

        var requestId = Guid.NewGuid();
        var id = await service.CreateParentManagedUserAsync("ali.kaya", "Ali", "Kaya", requestId);

        id.ShouldBe("kc-child");
        using var doc = System.Text.Json.JsonDocument.Parse(body!);
        var root = doc.RootElement;
        root.GetProperty("username").GetString().ShouldBe("ali.kaya");
        root.TryGetProperty("email", out _).ShouldBeFalse();
        root.TryGetProperty("credentials", out _).ShouldBeFalse();
        root.GetProperty("requiredActions").GetArrayLength().ShouldBe(0);
        root.GetProperty("enabled").GetBoolean().ShouldBeTrue();
        root.GetProperty("attributes").GetProperty("parent_managed")[0].GetString().ShouldBe("true");
        root.GetProperty("attributes").GetProperty("child_request_id")[0].GetString().ShouldBe(requestId.ToString("D"));
    }

    [Fact]
    public async Task CreateParentManagedUser_requires_a_request_id()
        => await Should.ThrowAsync<ArgumentException>(() =>
            Build(_ => new HttpResponseMessage(HttpStatusCode.Created)).CreateParentManagedUserAsync("ali.kaya", "Ali", "Kaya", Guid.Empty));

    [Theory]
    [InlineData(HttpStatusCode.Conflict, KeycloakFailureKind.Conflict)]
    [InlineData(HttpStatusCode.BadRequest, KeycloakFailureKind.Validation)]
    [InlineData(HttpStatusCode.InternalServerError, KeycloakFailureKind.Unexpected)]
    public async Task CreateParentManagedUser_classifies_failures(HttpStatusCode status, KeycloakFailureKind kind)
    {
        var service = Build(_ => Json(status, """{"errorMessage":"x"}"""));

        var ex = await Should.ThrowAsync<KeycloakException>(() => service.CreateParentManagedUserAsync("ali.kaya", "Ali", "Kaya", Guid.NewGuid()));

        ex.Kind.ShouldBe(kind);
        ex.StatusCode.ShouldBe((int)status);
    }

    [Fact]
    public async Task ResetPassword_400_is_Validation_and_404_keeps_the_status()
    {
        var policy = Build(_ => Json(HttpStatusCode.BadRequest, """{"error":"invalidPasswordMinLengthMessage"}"""));
        var ex = await Should.ThrowAsync<KeycloakException>(() => policy.ResetPasswordAsync("kc-1", ExamplePassword));
        ex.Kind.ShouldBe(KeycloakFailureKind.Validation);
        ex.Message.ShouldNotContain(ExamplePassword);

        var missing = Build(_ => Json(HttpStatusCode.NotFound, """{"error":"User not found"}"""));
        (await Should.ThrowAsync<KeycloakException>(() => missing.ResetPasswordAsync("kc-1", ExamplePassword))).StatusCode.ShouldBe(404);
    }

    // ---- #438 review: rol kümesi daraltma + requestId araması ----

    private const string AccountClientUuid = "acc-uuid";

    /// <summary>
    /// Keycloak 26 dev realm'inde canlı doğrulanan yanıt biçimleri (default-roles kompoziti: manage-account, view-profile,
    /// uma_authorization, offline_access). <paramref name="stillMappedAfterDelete"/>: son okumada kompozit hâlâ eşli.
    /// </summary>
    private static (KeycloakService Service, List<(string Method, string Path, string? Body)> Calls) BuildRealm(bool stillMappedAfterDelete = false)
    {
        var calls = new List<(string, string, string?)>();
        var service = Build(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            var body = req.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            calls.Add((req.Method.Method, path, body));
            return (req.Method.Method, path) switch
            {
                ("GET", "/admin/realms/exam-realm") => Json(HttpStatusCode.OK,
                    """{"realm":"exam-realm","defaultRole":{"id":"def-id","name":"default-roles-exam-realm"}}"""),
                ("GET", "/admin/realms/exam-realm/roles/default-roles-exam-realm") => Json(HttpStatusCode.OK,
                    """{"id":"def-id","name":"default-roles-exam-realm","composite":true,"clientRole":false,"containerId":"realm-id"}"""),
                ("GET", "/admin/realms/exam-realm/roles/default-roles-exam-realm/composites") => Json(HttpStatusCode.OK, $$"""
                    [{"id":"ma","name":"manage-account","composite":true,"clientRole":true,"containerId":"{{AccountClientUuid}}"},
                     {"id":"vp","name":"view-profile","composite":false,"clientRole":true,"containerId":"{{AccountClientUuid}}"},
                     {"id":"uma","name":"uma_authorization","composite":false,"clientRole":false,"containerId":"realm-id"},
                     {"id":"off","name":"offline_access","composite":false,"clientRole":false,"containerId":"realm-id"}]
                    """),
                ("GET", "/admin/realms/exam-realm/roles/Student") => Json(HttpStatusCode.OK,
                    """{"id":"stu","name":"Student","composite":false,"clientRole":false,"containerId":"realm-id"}"""),
                ("GET", "/admin/realms/exam-realm/users/kc-child/role-mappings/realm") => Json(HttpStatusCode.OK, stillMappedAfterDelete
                    ? """[{"id":"def-id","name":"default-roles-exam-realm"},{"id":"stu","name":"Student"}]"""
                    : """[{"id":"stu","name":"Student"},{"id":"off","name":"offline_access"},{"id":"uma","name":"uma_authorization"}]"""),
                _ => new HttpResponseMessage(HttpStatusCode.NoContent)
            };
        });
        return (service, calls);
    }

    private static string[] RoleNames(string? json)
        => System.Text.Json.JsonDocument.Parse(json!).RootElement.EnumerateArray().Select(e => e.GetProperty("name").GetString()!).ToArray();

    [Fact]
    public async Task ApplyParentManagedRoles_replaces_the_default_composite_with_the_minimal_set()
    {
        var (service, calls) = BuildRealm();

        await service.ApplyParentManagedRolesAsync("kc-child");

        var writes = calls.Where(c => c.Method != "GET").ToList();
        writes.Count.ShouldBe(3);

        writes[0].Method.ShouldBe("POST");
        writes[0].Path.ShouldBe("/admin/realms/exam-realm/users/kc-child/role-mappings/realm");
        RoleNames(writes[0].Body).OrderBy(n => n).ShouldBe(new[] { "offline_access", "Student", "uma_authorization" }.OrderBy(n => n));

        writes[1].Method.ShouldBe("POST");
        writes[1].Path.ShouldBe($"/admin/realms/exam-realm/users/kc-child/role-mappings/clients/{AccountClientUuid}");
        RoleNames(writes[1].Body).ShouldBe(new[] { "view-profile" }); // manage-account YOK

        writes[2].Method.ShouldBe("DELETE");
        writes[2].Path.ShouldBe("/admin/realms/exam-realm/users/kc-child/role-mappings/realm");
        RoleNames(writes[2].Body).ShouldBe(new[] { "default-roles-exam-realm" });

        // view-clients gerektiren client listesi hiç çağrılmaz.
        calls.ShouldNotContain(c => c.Path.EndsWith("/clients", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ApplyParentManagedRoles_fails_closed_when_the_default_composite_is_still_mapped()
    {
        var (service, _) = BuildRealm(stillMappedAfterDelete: true);

        await Should.ThrowAsync<KeycloakException>(() => service.ApplyParentManagedRolesAsync("kc-child"));
    }

    [Fact]
    public async Task ApplyParentManagedRoles_fails_when_a_mapping_call_is_rejected()
    {
        var service = Build(req => req.Method == HttpMethod.Delete
            ? Json(HttpStatusCode.Forbidden, """{"error":"HTTP 403 Forbidden"}""")
            : BuildRealmResponse(req));

        (await Should.ThrowAsync<KeycloakException>(() => service.ApplyParentManagedRolesAsync("kc-child"))).StatusCode.ShouldBe(403);
    }

    private static HttpResponseMessage BuildRealmResponse(HttpRequestMessage req)
    {
        // Okumalar (BuildRealm'in sadeleştirilmiş verisi); yazma çağrıları 204.
        return req.RequestUri!.AbsolutePath switch
        {
            "/admin/realms/exam-realm" => Json(HttpStatusCode.OK, """{"realm":"exam-realm","defaultRole":{"name":"default-roles-exam-realm"}}"""),
            "/admin/realms/exam-realm/roles/default-roles-exam-realm" => Json(HttpStatusCode.OK, """{"id":"def-id","name":"default-roles-exam-realm"}"""),
            "/admin/realms/exam-realm/roles/default-roles-exam-realm/composites" => Json(HttpStatusCode.OK,
                $$"""[{"id":"vp","name":"view-profile","clientRole":true,"containerId":"{{AccountClientUuid}}"}]"""),
            "/admin/realms/exam-realm/roles/Student" => Json(HttpStatusCode.OK, """{"id":"stu","name":"Student"}"""),
            _ => new HttpResponseMessage(HttpStatusCode.NoContent)
        };
    }

    [Fact]
    public async Task FindUserIdsByAttribute_matches_the_attribute_value_exactly()
    {
        string? query = null;
        var service = Build(req =>
        {
            query = req.RequestUri!.Query;
            return Json(HttpStatusCode.OK, """
                [{"id":"kc-1","attributes":{"child_request_id":["abc"]}},
                 {"id":"kc-2","attributes":{"child_request_id":["abcd"]}},
                 {"id":"kc-3"}]
                """);
        });

        var ids = await service.FindUserIdsByAttributeAsync("child_request_id", "abc");

        ids.ShouldBe(new[] { "kc-1" });
        Uri.UnescapeDataString(query!).ShouldContain("q=child_request_id:abc");
        query.ShouldContain("briefRepresentation=false");
    }
}
