using System.Net;
using System.Text;
using System.Text.Json;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services;
using Microsoft.Extensions.Options;

namespace AuthApi.Tests.Services;

/// <summary>
/// KeycloakService temizlik yardımcıları (issue #218), sahte <see cref="HttpMessageHandler"/> ile: e-posta/kullanıcı adı
/// infix araması boş sayfa gelene kadar sayfalar (>1 sayfa), `search=` (prefix) kullanılmaz; silmede 404 → false, 204 → true.
/// </summary>
public class KeycloakServiceSearchTests
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
            AdminClientId = "exam-admin",
            AdminClientSecret = "example-secret-value"
        };
        return (new KeycloakService(factory, Options.Create(settings)), handler);
    }

    private static string Page(IEnumerable<int> ids)
        => JsonSerializer.Serialize(ids.Select(i => new { id = "kc-" + i, username = $"seed.t.{i}@seed.examapp.local", email = $"seed.t.{i}@seed.examapp.local" }));

    private static Dictionary<string, string> Query(HttpRequestMessage req)
        => req.RequestUri!.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries)
            .Select(kv => kv.Split('=', 2))
            .ToDictionary(p => Uri.UnescapeDataString(p[0]), p => Uri.UnescapeDataString(p.Length > 1 ? p[1] : ""));

    [Fact]
    public async Task Search_pages_until_empty_page_with_email_infix_filter()
    {
        // 501 kullanıcı: sayfa 1 = 500, sayfa 2 = 1, sayfa 3 = boş → dur.
        var (service, handler) = Build(req =>
        {
            var q = Query(req);
            var first = int.Parse(q["first"]);
            var max = int.Parse(q["max"]);
            var ids = Enumerable.Range(first, Math.Max(0, Math.Min(max, 501 - first)));
            return Json(HttpStatusCode.OK, Page(ids));
        });

        var users = await service.SearchUsersAsync("@seed.examapp.local", KeycloakUserSearchField.Email);

        users.Count.ShouldBe(501);
        users.Select(u => u.Id).ShouldBeUnique();
        var searches = handler.Requests.Where(r => r.Method == HttpMethod.Get).ToList();
        searches.Count.ShouldBe(3);
        searches.Select(r => Query(r)["first"]).ShouldBe(["0", "500", "501"]);
        searches.ShouldAllBe(r => Query(r)["email"] == "@seed.examapp.local" && Query(r)["exact"] == "false" && !Query(r).ContainsKey("search"));
        handler.Requests.Count(r => r.Method == HttpMethod.Post).ShouldBe(1); // admin token bir kez
    }

    [Fact]
    public async Task Username_field_uses_username_query_parameter()
    {
        var (service, handler) = Build(req => Json(HttpStatusCode.OK, Query(req)["first"] == "0" ? Page([7]) : "[]"));

        var users = await service.SearchUsersAsync("@seed.examapp.local", KeycloakUserSearchField.Username);

        users.Single().Username.ShouldBe("seed.t.7@seed.examapp.local");
        var q = Query(handler.Requests.First(r => r.Method == HttpMethod.Get));
        q["username"].ShouldBe("@seed.examapp.local");
        q.ContainsKey("email").ShouldBeFalse();
    }

    [Fact]
    public async Task Search_error_throws_keycloak_exception()
    {
        var (service, _) = Build(_ => Json(HttpStatusCode.InternalServerError, "boom"));
        await Should.ThrowAsync<KeycloakException>(() => service.SearchUsersAsync("@seed.examapp.local", KeycloakUserSearchField.Email));
    }

    [Fact]
    public async Task TryDelete_returns_true_on_204_false_on_404_and_throws_otherwise()
    {
        var (service, handler) = Build(req => req.RequestUri!.AbsolutePath.EndsWith("/kc-ok")
            ? new HttpResponseMessage(HttpStatusCode.NoContent)
            : req.RequestUri.AbsolutePath.EndsWith("/kc-gone")
                ? new HttpResponseMessage(HttpStatusCode.NotFound)
                : Json(HttpStatusCode.Forbidden, "no"));

        (await service.TryDeleteUserAsync("kc-ok")).ShouldBeTrue();
        (await service.TryDeleteUserAsync("kc-gone")).ShouldBeFalse();
        await Should.ThrowAsync<KeycloakException>(() => service.TryDeleteUserAsync("kc-forbidden"));
        handler.Requests.Where(r => r.Method == HttpMethod.Delete).Select(r => r.RequestUri!.AbsolutePath)
            .ShouldBe(["/admin/realms/exam-realm/users/kc-ok", "/admin/realms/exam-realm/users/kc-gone", "/admin/realms/exam-realm/users/kc-forbidden"]);
    }
}
