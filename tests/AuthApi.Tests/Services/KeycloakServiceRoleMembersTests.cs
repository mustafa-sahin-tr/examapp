using System.Net;
using System.Text;
using System.Text.Json;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services;
using Microsoft.Extensions.Options;

namespace AuthApi.Tests.Services;

/// <summary>
/// <see cref="KeycloakService.GetUsersInRoleAsync"/> (issue #267), sahte <see cref="HttpMessageHandler"/> ile:
/// <c>/roles/{role}/users</c> boş sayfa gelene kadar first/max ile sayfalanır, tam temsil istenir, alanlar çözülür;
/// rol yok (404 + rol listesi okunur) → <see cref="KeycloakRoleNotFoundException"/>, diğer 404/5xx → <see cref="KeycloakException"/>.
/// </summary>
public class KeycloakServiceRoleMembersTests
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
        public List<HttpRequestMessage> Requests { get; } = new();
        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return Task.FromResult(_respond(request));
        }
    }

    private static HttpResponseMessage Json(HttpStatusCode code, string body)
        => new(code) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static (KeycloakService Service, FakeHandler Handler) Build(Func<HttpRequestMessage, HttpResponseMessage?> respond)
    {
        var handler = new FakeHandler(req =>
        {
            if (req.Method == HttpMethod.Post && req.RequestUri!.AbsolutePath.EndsWith("/token"))
                return Json(HttpStatusCode.OK, """{"access_token":"admin-token-example","expires_in":300}""");
            return respond(req) ?? new HttpResponseMessage(HttpStatusCode.NotFound);
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
        return (new KeycloakService(factory, Options.Create(settings)), handler);
    }

    private static Dictionary<string, string> Query(HttpRequestMessage req)
        => req.RequestUri!.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(kv => kv.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p.Length > 1 ? p[1] : ""));

    private static string Page(IEnumerable<int> ids)
        => JsonSerializer.Serialize(ids.Select(i => new
        {
            id = "kc-" + i,
            username = $"user{i}@example.com",
            email = $"user{i}@example.com",
            enabled = i % 2 == 0,
            createdTimestamp = 1_700_000_000_000L + i
        }));

    [Fact]
    public async Task Pages_with_first_max_until_empty_page_and_requests_full_representation()
    {
        const int total = 250;
        var (service, handler) = Build(req =>
        {
            var q = Query(req);
            var first = int.Parse(q["first"]);
            var max = int.Parse(q["max"]);
            return Json(HttpStatusCode.OK, Page(Enumerable.Range(first, Math.Max(0, Math.Min(max, total - first)))));
        });

        var members = await service.GetUsersInRoleAsync("Admin");

        members.Count.ShouldBe(total);
        members.Select(m => m.Id).ShouldBeUnique();
        var gets = handler.Requests.Where(r => r.Method == HttpMethod.Get).ToList();
        gets.ShouldAllBe(r => r.RequestUri!.AbsolutePath == "/admin/realms/exam-realm/roles/Admin/users");
        gets.Select(r => Query(r)["first"]).ShouldBe(["0", "100", "200", "250"]); // son sayfa boş → dur
        gets.ShouldAllBe(r => Query(r)["max"] == KeycloakService.RoleMembersPageSize.ToString());
        gets.ShouldAllBe(r => Query(r)["briefRepresentation"] == "false");
        gets.ShouldAllBe(r => r.Headers.Authorization!.Parameter == "admin-token-example");
        handler.Requests.Count(r => r.Method == HttpMethod.Post).ShouldBe(1); // admin token bir kez
    }

    [Fact]
    public async Task Continues_when_server_clamps_page_below_max()
    {
        // Keycloak max'ı kırparsa (sayfa < max) erken durmamalı: yalnızca boş sayfa bitiştir.
        var (service, handler) = Build(req =>
        {
            var first = int.Parse(Query(req)["first"]);
            return Json(HttpStatusCode.OK, Page(Enumerable.Range(first, Math.Max(0, Math.Min(30, 75 - first)))));
        });

        var members = await service.GetUsersInRoleAsync("exam-service");

        members.Count.ShouldBe(75);
        handler.Requests.Where(r => r.Method == HttpMethod.Get).Select(r => Query(r)["first"]).ShouldBe(["0", "30", "60", "75"]);
    }

    [Fact]
    public async Task Parses_enabled_created_timestamp_service_account_client_and_attributes()
    {
        const string body = """
            [
              {"id":"sa-1","username":"service-account-exam-admin","enabled":true,"createdTimestamp":1700000000000,"serviceAccountClientId":"exam-admin"},
              {"id":"u-1","username":"alice@example.com","email":"alice@example.com","enabled":false,"attributes":{"seed_origin":["examapp-seed"]}}
            ]
            """;
        var (service, _) = Build(req => Json(HttpStatusCode.OK, Query(req)["first"] == "0" ? body : "[]"));

        var members = await service.GetUsersInRoleAsync("exam-service");

        var sa = members.Single(m => m.Id == "sa-1");
        sa.ServiceAccountClientId.ShouldBe("exam-admin");
        sa.Enabled.ShouldBeTrue();
        sa.Email.ShouldBeNull();
        sa.CreatedAt.ShouldBe(DateTimeOffset.FromUnixTimeMilliseconds(1700000000000));

        var user = members.Single(m => m.Id == "u-1");
        user.Enabled.ShouldBeFalse();
        user.CreatedAt.ShouldBeNull();
        user.ServiceAccountClientId.ShouldBeNull();
        user.Attribute("seed_origin").ShouldBe("examapp-seed");
    }

    [Fact]
    public async Task Role_name_is_escaped_in_path()
    {
        var (service, handler) = Build(_ => Json(HttpStatusCode.OK, "[]"));

        (await service.GetUsersInRoleAsync("a b/c")).ShouldBeEmpty();

        handler.Requests.Single(r => r.Method == HttpMethod.Get).RequestUri!.AbsoluteUri
            .ShouldContain("/roles/a%20b%2Fc/users?");
    }

    [Fact]
    public async Task Missing_role_is_distinguished_from_wrong_realm_by_probing_role_list()
    {
        // Rol yok: /roles/X/users 404, realm rol listesi 200 → KeycloakRoleNotFoundException.
        var (missing, handler) = Build(req => req.RequestUri!.AbsolutePath.EndsWith("/users")
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : Json(HttpStatusCode.OK, "[]"));
        var ex = await Should.ThrowAsync<KeycloakRoleNotFoundException>(() => missing.GetUsersInRoleAsync("exam-service"));
        ex.RoleName.ShouldBe("exam-service");
        handler.Requests.Last().RequestUri!.AbsolutePath.ShouldBe("/admin/realms/exam-realm/roles");

        // Realm/URL yanlış: rol listesi de 404 → genel hata (denetim sessizce "temiz" görünmesin).
        var (wrongRealm, _) = Build(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        var generic = await Should.ThrowAsync<KeycloakException>(() => wrongRealm.GetUsersInRoleAsync("Admin"));
        generic.ShouldNotBeOfType<KeycloakRoleNotFoundException>();
    }

    [Fact]
    public async Task Other_errors_throw_keycloak_exception()
    {
        var (failing, _) = Build(_ => Json(HttpStatusCode.Forbidden, "no"));
        await Should.ThrowAsync<KeycloakException>(() => failing.GetUsersInRoleAsync("Admin"));

        var (blank, _) = Build(_ => Json(HttpStatusCode.OK, "[]"));
        await Should.ThrowAsync<ArgumentException>(() => blank.GetUsersInRoleAsync(" "));
    }

    [Fact]
    public async Task Credential_and_federated_identity_reads_fail_closed_on_404()
    {
        var (service, handler) = Build(req => req.RequestUri!.AbsolutePath switch
        {
            "/admin/realms/exam-realm/users/u-1/credentials" => Json(HttpStatusCode.OK, """[{"id":"c1","type":"password"},{"id":"c2","type":"otp"}]"""),
            "/admin/realms/exam-realm/users/sa-1/credentials" => Json(HttpStatusCode.OK, "[]"),
            "/admin/realms/exam-realm/users/boom/credentials" => Json(HttpStatusCode.InternalServerError, "x"),
            "/admin/realms/exam-realm/users/u-1/federated-identity" => Json(HttpStatusCode.OK, """[{"identityProvider":"google","userId":"g1"}]"""),
            "/admin/realms/exam-realm/users/sa-1/federated-identity" => Json(HttpStatusCode.OK, "[]"),
            _ => null // 404
        });

        (await service.GetUserCredentialTypesAsync("u-1")).ShouldBe(["password", "otp"]);
        (await service.GetUserCredentialTypesAsync("sa-1")).ShouldBeEmpty();
        (await Should.ThrowAsync<KeycloakException>(() => service.GetUserCredentialTypesAsync("gone"))).StatusCode.ShouldBe(404);
        await Should.ThrowAsync<KeycloakException>(() => service.GetUserCredentialTypesAsync("boom"));

        (await service.GetUserFederatedIdentityProvidersAsync("u-1")).ShouldBe(["google"]);
        (await service.GetUserFederatedIdentityProvidersAsync("sa-1")).ShouldBeEmpty();
        await Should.ThrowAsync<KeycloakException>(() => service.GetUserFederatedIdentityProvidersAsync("gone"));

        handler.Requests.Where(r => r.Method != HttpMethod.Get).ShouldAllBe(r => r.RequestUri!.AbsolutePath.EndsWith("/token")); // salt okunur
    }

    [Fact]
    public async Task Stops_when_server_ignores_first_and_repeats_the_same_page()
    {
        // Sunucu `first`'i yok sayıp hep aynı sayfayı dönerse sonsuz döngü olmamalı: yeni id yoksa dur.
        var (service, handler) = Build(_ => Json(HttpStatusCode.OK, Page(Enumerable.Range(0, 100))));

        (await service.GetUsersInRoleAsync("Admin")).Count.ShouldBe(100);
        handler.Requests.Count(r => r.Method == HttpMethod.Get).ShouldBe(2);

        var (search, searchHandler) = Build(_ => Json(HttpStatusCode.OK, Page(Enumerable.Range(0, 3))));
        (await search.SearchUsersAsync("@example.com", ExamApp.Api.Models.Dtos.KeycloakUserSearchField.Email)).Count.ShouldBe(3);
        searchHandler.Requests.Count(r => r.Method == HttpMethod.Get).ShouldBe(2);
    }

    [Fact]
    public async Task Page_limit_is_enforced_fail_closed()
    {
        // Her sayfada yeni id → üst sınıra kadar dolaşır, sonra sessizce kesmek yerine hata.
        var (service, handler) = Build(req =>
        {
            var first = int.Parse(Query(req)["first"]);
            return Json(HttpStatusCode.OK, Page([first]));
        });

        var ex = await Should.ThrowAsync<KeycloakException>(() => service.GetUsersInRoleAsync("Admin"));
        ex.Message.ShouldContain("page limit");
        handler.Requests.Count(r => r.Method == HttpMethod.Get).ShouldBe(KeycloakService.MaxAdminPages);
    }

    [Fact]
    public async Task Groups_in_role_members_and_children_use_expected_endpoints()
    {
        var (service, handler) = Build(req =>
        {
            var q = Query(req);
            if (q["first"] != "0") return Json(HttpStatusCode.OK, "[]");
            return req.RequestUri!.AbsolutePath switch
            {
                "/admin/realms/exam-realm/roles/Admin/groups" => Json(HttpStatusCode.OK, """[{"id":"g1","name":"ops","path":"/ops"}]"""),
                "/admin/realms/exam-realm/groups/g1/members" => Json(HttpStatusCode.OK, """[{"id":"u1","username":"a@example.com","email":"a@example.com","enabled":true}]"""),
                "/admin/realms/exam-realm/groups/g1/children" => Json(HttpStatusCode.OK, """[{"id":"g2","name":"night"}]"""),
                _ => null
            };
        });

        (await service.GetGroupsInRoleAsync("Admin")).ShouldBe([new ExamApp.Api.Models.Dtos.KeycloakGroupRef("g1", "ops", "/ops")]);
        (await service.GetGroupMembersAsync("g1")).Single().Id.ShouldBe("u1");
        (await service.GetSubGroupsAsync("g1")).Single().ShouldBe(new ExamApp.Api.Models.Dtos.KeycloakGroupRef("g2", "night", "/night"));
    }

    [Fact]
    public async Task Groups_in_missing_role_throw_role_not_found()
    {
        var (service, _) = Build(req => req.RequestUri!.AbsolutePath.EndsWith("/groups")
            ? new HttpResponseMessage(HttpStatusCode.NotFound)
            : Json(HttpStatusCode.OK, "[]"));

        await Should.ThrowAsync<KeycloakRoleNotFoundException>(() => service.GetGroupsInRoleAsync("exam-service"));
    }

    [Fact]
    public async Task Realm_role_composites_are_read_for_composite_roles_only()
    {
        var (service, handler) = Build(req =>
        {
            var path = req.RequestUri!.AbsolutePath;
            if (path == "/admin/realms/exam-realm/roles")
                return Json(HttpStatusCode.OK, Query(req)["first"] == "0"
                    ? """[{"id":"r1","name":"default-roles-exam-realm","composite":true},{"id":"r2","name":"Student","composite":false},{"id":"r3","name":"Admin","composite":false}]"""
                    : "[]");
            if (path == "/admin/realms/exam-realm/roles/default-roles-exam-realm/composites/realm")
                return Json(HttpStatusCode.OK, """[{"name":"offline_access"},{"name":"Admin"}]""");
            return null;
        });

        var composites = await service.GetRealmRoleCompositesAsync();

        composites.Keys.ShouldBe(["default-roles-exam-realm"]);
        composites["default-roles-exam-realm"].ShouldBe(["offline_access", "Admin"]);
        handler.Requests.ShouldNotContain(r => r.RequestUri!.AbsolutePath.Contains("/Student/composites"));

        // Kompozit okunamazsa hata (denetim exit 3).
        var (failing, _) = Build(req => req.RequestUri!.AbsolutePath == "/admin/realms/exam-realm/roles"
            ? Json(HttpStatusCode.OK, Query(req)["first"] == "0" ? """[{"id":"r1","name":"x","composite":true}]""" : "[]")
            : Json(HttpStatusCode.Forbidden, "no"));
        await Should.ThrowAsync<KeycloakException>(() => failing.GetRealmRoleCompositesAsync());
    }
}
