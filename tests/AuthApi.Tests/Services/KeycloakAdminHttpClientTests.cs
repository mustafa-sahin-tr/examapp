using ExamApp.Api.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http.Resilience;

namespace AuthApi.Tests.Services;

/// <summary>
/// auth-api → Keycloak admin client: ServiceDefaults'un tüm client'lara eklediği standart resilience handler'ı
/// (10 sn attempt timeout + 3 retry; partialImport idempotent değil → 409) <see cref="KeycloakService.AdminHttpClientName"/>
/// pipeline'ında OLMAMALI; varsayılan client'ta kalmalı.
/// </summary>
public class KeycloakAdminHttpClientTests
{
    private static List<Type> HandlerChain(HttpMessageHandler handler)
    {
        var chain = new List<Type>();
        var current = handler;
        while (current is not null)
        {
            chain.Add(current.GetType());
            current = (current as DelegatingHandler)?.InnerHandler;
        }
        return chain;
    }

    [Fact]
    public void Admin_client_has_no_resilience_handler_default_client_still_does()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.ConfigureHttpClientDefaults(http => http.AddStandardResilienceHandler()); // AddServiceDefaults ile aynı
        services.AddHttpClient();
        services.AddKeycloakAdminHttpClient();
        using var provider = services.BuildServiceProvider();

        var factory = provider.GetRequiredService<IHttpMessageHandlerFactory>();
        var adminChain = HandlerChain(factory.CreateHandler(KeycloakService.AdminHttpClientName));
        var defaultChain = HandlerChain(factory.CreateHandler(string.Empty));

        adminChain.ShouldNotContain(t => t == typeof(ResilienceHandler) || t.IsSubclassOf(typeof(ResilienceHandler)));
        defaultChain.ShouldContain(t => t == typeof(ResilienceHandler) || t.IsSubclassOf(typeof(ResilienceHandler)));

        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(KeycloakService.AdminHttpClientName);
        client.Timeout.ShouldBe(KeycloakService.AdminHttpClientTimeout);
        client.Timeout.ShouldBeGreaterThanOrEqualTo(TimeSpan.FromMinutes(30));
    }
}
