using System.Net;
using BadgeService.Services;
using BadgeService.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace BadgeService.Tests;

/// <summary>
/// Issue #372: service-to-service tokens come from the dedicated exam-service client,
/// never from exam-admin (Keycloak admin roles) or exam-client.
/// </summary>
public class ServiceTokenProviderTests
{
    private const string ExampleSvcValue = "example-svc-value";
    private const string ExampleAdminValue = "example-admin-value";
    private const string ExampleClientValue = "example-client-value";

    private static ServiceTokenProvider NewProvider(StubHttp http, Dictionary<string, string?> config)
        => new(http, new ConfigurationBuilder().AddInMemoryCollection(config).Build(),
            NullLogger<ServiceTokenProvider>.Instance);

    private static Dictionary<string, string?> Base() => new()
    {
        ["Keycloak:Host"] = "http://kc:8080/",
        ["Keycloak:TokenUrl"] = "/realms/exam/protocol/openid-connect/token",
    };

    private static StubHttp Ok() =>
        new StubHttp().On("/token", HttpStatusCode.OK, """{"access_token":"tok","expires_in":300}""");

    [Fact]
    public async Task Requests_the_token_with_the_exam_service_client()
    {
        var http = Ok();
        var config = Base();
        config["Keycloak:ServiceClientSecret"] = ExampleSvcValue;
        // Admin/exam-client credentials are configured too and must be ignored.
        config["Keycloak:AdminClientId"] = "exam-admin";
        config["Keycloak:AdminClientSecret"] = ExampleAdminValue;
        config["Keycloak:ClientId"] = "exam-client";
        config["Keycloak:ClientSecret"] = ExampleClientValue;

        (await NewProvider(http, config).GetAccessTokenAsync(default)).ShouldBe("tok");

        var body = http.BodyMatching("/token");
        body.ShouldContain("client_id=exam-service");
        body.ShouldContain($"client_secret={ExampleSvcValue}");
        body.ShouldContain("grant_type=client_credentials");
        body.ShouldNotContain("exam-admin");
        body.ShouldNotContain(ExampleAdminValue);
        body.ShouldNotContain(ExampleClientValue);
    }

    [Fact]
    public async Task Honours_an_explicit_service_client_id()
    {
        var http = Ok();
        var config = Base();
        config["Keycloak:ServiceClientId"] = "exam-service-staging";
        config["Keycloak:ServiceClientSecret"] = ExampleSvcValue;

        await NewProvider(http, config).GetAccessTokenAsync(default);

        http.BodyMatching("/token").ShouldContain("client_id=exam-service-staging");
    }

    [Fact]
    public async Task Does_not_fall_back_to_admin_or_exam_client_when_service_secret_is_missing()
    {
        var http = Ok();
        var config = Base();
        config["Keycloak:AdminClientId"] = "exam-admin";
        config["Keycloak:AdminClientSecret"] = ExampleAdminValue;
        config["Keycloak:ClientId"] = "exam-client";
        config["Keycloak:ClientSecret"] = ExampleClientValue;

        await Should.ThrowAsync<InvalidOperationException>(
            () => NewProvider(http, config).GetAccessTokenAsync(default));
        http.Requests.ShouldBeEmpty();
    }
}
