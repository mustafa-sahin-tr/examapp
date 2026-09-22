using System;
using ExamApp.Api.Services.Seed;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ExamApp.Api.Services.Teachers.Seed;

public static class TeacherSeedServiceCollectionExtensions
{
    /// <summary>
    /// <c>seed-teachers</c> aracını kaydeder (issue #217). YALNIZCA Development/Staging'de kayıt yapar;
    /// Production'da servis DI'da hiç yer almaz — ortam guard'ının ilk katmanı budur.
    /// <see cref="ExamApp.Api.Services.StudentReset.IServiceTokenProvider"/> Program.cs'te zaten kayıtlıdır.
    /// </summary>
    public static IServiceCollection AddTeacherSeed(this IServiceCollection services, IHostEnvironment environment)
    {
        if (!SeedCommands.IsAllowedEnvironment(environment))
            return services;

        // Partial import'ta 500 hesap tek istek + Keycloak; varsayılan 100 sn yetmeyebilir.
        services.AddHttpClient(AuthApiSeedClient.HttpClientName, client => client.Timeout = TimeSpan.FromMinutes(10));
        services.AddScoped<IAuthApiSeedClient, AuthApiSeedClient>();
        services.AddScoped<ITeacherSeedService, TeacherSeedService>();
        return services;
    }
}
