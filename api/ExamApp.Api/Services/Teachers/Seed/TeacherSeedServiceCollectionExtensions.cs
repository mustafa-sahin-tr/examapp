using System;
using ExamApp.Api.Services.Seed;
using ExamApp.Api.Services.Seed.Cleanup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ExamApp.Api.Services.Teachers.Seed;

public static class TeacherSeedServiceCollectionExtensions
{
    /// <summary>
    /// <c>seed-teachers</c> (issue #217), <c>seed-tutors</c> ve <c>seed-cleanup</c> (issue #218) araçlarını kaydeder.
    /// YALNIZCA Development/Staging'de kayıt yapar; Production'da servisler DI'da hiç yer almaz — ortam guard'ının
    /// ilk katmanı budur. <see cref="ExamApp.Api.Services.StudentReset.IServiceTokenProvider"/> Program.cs'te zaten kayıtlıdır.
    /// </summary>
    public static IServiceCollection AddTeacherSeed(this IServiceCollection services, IHostEnvironment environment)
    {
        if (!SeedCommands.IsAllowedEnvironment(environment))
            return services;

        // Partial import'ta 500 hesap tek istek + Keycloak; temizlikte on binlerce Keycloak silme — varsayılan 100 sn yetmez.
        // RemoveAllResilienceHandlers: AddServiceDefaults() tüm HttpClient'lara standart resilience handler'ı
        // (attempt timeout 10 sn + 3 retry) ekler ve client.Timeout bunu EZMEZ. 500'lük partial-import isteği 10 sn'yi
        // aşınca istek iptal edilip aynı parti yeniden gönderiliyor, Keycloak ilk transaction'ı bitirdiği için ikinci
        // istek 409 alıyor ve identity/exam yazılmadan binlerce yetim Keycloak kullanıcısı kalıyordu (2026-09-22 koşusu).
        // Seed uçları idempotent değil → yeniden deneme YOK; tek sınır client.Timeout.
#pragma warning disable EXTEXP0001 // RemoveAllResilienceHandlers "evaluation" işaretli; ConfigureHttpClientDefaults'u geri almanın tek resmi yolu.
        services
            .AddHttpClient(AuthApiSeedClient.HttpClientName, client => client.Timeout = AuthApiSeedClient.Timeout)
            .RemoveAllResilienceHandlers();
#pragma warning restore EXTEXP0001
        services.AddScoped<IAuthApiSeedClient, AuthApiSeedClient>();
        services.AddScoped<ITeacherSeedService, TeacherSeedService>();
        services.AddScoped<ITutorSeedService, TutorSeedService>();
        services.AddScoped<ISeedCleanupService, SeedCleanupService>();
        return services;
    }
}
