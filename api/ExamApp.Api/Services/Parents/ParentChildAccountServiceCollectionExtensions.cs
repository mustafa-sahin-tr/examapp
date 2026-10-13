using Microsoft.Extensions.DependencyInjection;

namespace ExamApp.Api.Services.Parents;

public static class ParentChildAccountServiceCollectionExtensions
{
    /// <summary>
    /// Issue #438: veli panelinden çocuk hesabı. auth-api'ye giden <see cref="AuthApiChildAccountDirectory.HttpClientName"/> client'ı
    /// ServiceDefaults'un standart resilience handler'ından MUAF (RemoveAllResilienceHandlers): hesap oluşturma idempotent değil —
    /// 10 sn attempt timeout + retry, Keycloak'ta tamamlanmış ilk POST'u yeniden gönderip 409 + yetim hesap üretirdi (2026-09-22 seed
    /// olayı). Tek sınır <see cref="AuthApiChildAccountDirectory.Timeout"/>. AddServiceDefaults()'tan SONRA çağrılmalı.
    /// </summary>
    public static IServiceCollection AddParentChildAccounts(this IServiceCollection services)
    {
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers "evaluation" işaretli; ConfigureHttpClientDefaults'u geri almanın tek resmi yolu.
        services
            .AddHttpClient(AuthApiChildAccountDirectory.HttpClientName, client => client.Timeout = AuthApiChildAccountDirectory.Timeout)
            .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
        services.AddScoped<IChildAccountDirectory, AuthApiChildAccountDirectory>();
        services.AddScoped<IParentChildAccountService, ParentChildAccountService>();
        return services;
    }
}
