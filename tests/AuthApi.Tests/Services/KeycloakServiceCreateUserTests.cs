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
}
