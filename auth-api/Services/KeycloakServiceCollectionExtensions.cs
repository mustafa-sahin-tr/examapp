using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.Services;

public static class KeycloakServiceCollectionExtensions
{
    /// <summary>
    /// Keycloak admin toplu işlemleri (partialImport, sayfalı arama, seed onarımı) için named client
    /// (<see cref="KeycloakService.AdminHttpClientName"/>). <c>AddServiceDefaults()</c>'un tüm client'lara uyguladığı
    /// standart resilience handler (10 sn attempt timeout + 3 retry) BU client'tan kaldırılır: partialImport idempotent
    /// değil — 10 sn'de iptal edilip yeniden gönderilen parti Keycloak'ta 409 üretiyordu. Yeniden deneme yok, tek sınır
    /// <see cref="KeycloakService.AdminHttpClientTimeout"/>. Login/token akışları varsayılan client'ta kalır (resilience açık).
    /// <c>AddServiceDefaults()</c>'tan SONRA çağrılmalı (handler kaldırma eylemi kayıt sırasına göre çalışır).
    /// </summary>
    public static IServiceCollection AddKeycloakAdminHttpClient(this IServiceCollection services)
    {
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers "evaluation" işaretli; ConfigureHttpClientDefaults'u geri almanın tek resmi yolu.
        services
            .AddHttpClient(KeycloakService.AdminHttpClientName, client => client.Timeout = KeycloakService.AdminHttpClientTimeout)
            .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
        return services;
    }
}
