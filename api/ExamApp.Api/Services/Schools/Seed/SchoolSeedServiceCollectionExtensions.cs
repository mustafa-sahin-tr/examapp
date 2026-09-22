using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ExamApp.Api.Services.Schools.Seed;

public static class SchoolSeedServiceCollectionExtensions
{
    /// <summary>
    /// <c>seed-schools</c> aracını kaydeder (issue #216). YALNIZCA Development/Staging'de kayıt yapar;
    /// Production'da servis DI'da hiç yer almaz — ortam guard'ının ilk katmanı budur.
    /// </summary>
    public static IServiceCollection AddSchoolSeed(this IServiceCollection services, IHostEnvironment environment)
    {
        if (!SchoolSeedService.IsAllowedEnvironment(environment))
            return services;

        services.AddHttpClient(nameof(GitHubSchoolSeedSourceProvider));
        services.AddScoped<ISchoolSeedSourceProvider, GitHubSchoolSeedSourceProvider>();
        services.AddScoped<ISchoolSeedService, SchoolSeedService>();
        return services;
    }
}
