using System.Net.Http.Headers;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Respawn;

namespace ExamApp.Api.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<IntegrationApiFactory>
{
    public const string Name = "integration";
}

[Collection(IntegrationCollection.Name)]
public abstract class IntegrationTestBase : IAsyncLifetime
{
    protected readonly IntegrationApiFactory Factory;
    private Respawner _respawner = null!;

    protected IntegrationTestBase(IntegrationApiFactory factory) => Factory = factory;

    public async ValueTask InitializeAsync()
    {
        // Touch the app once so the host builds and migrations run.
        _ = Factory.Services;

        await using var conn = new NpgsqlConnection(Factory.ConnectionString);
        await conn.OpenAsync();
        _respawner = await Respawner.CreateAsync(conn, new RespawnerOptions
        {
            DbAdapter = DbAdapter.Postgres,
            SchemasToInclude = ["public"],
            TablesToIgnore = ["__EFMigrationsHistory"],
        });
        await _respawner.ResetAsync(conn);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    // ---- data access ----

    protected async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> work)
    {
        using var scope = Factory.Services.CreateScope();
        return await work(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    protected Task WithDbAsync(Func<AppDbContext, Task> work) =>
        WithDbAsync(async db => { await work(db); return 0; });

    // ---- HTTP clients ----

    protected HttpClient Anonymous() => Factory.CreateClient();

    /// <summary>An authenticated client. Also seeds the user-profile cache so
    /// BaseController.GetAuthenticatedUserAsync resolves without calling auth-api.</summary>
    protected async Task<HttpClient> ClientAsAsync(
        int userId, string role, string keycloakId = "kc-test", params string[] realmRoles)
    {
        var cache = Factory.Services.GetRequiredService<IDistributedCache>();
        var profile = new UserProfileDto
        {
            Id = userId, KeycloakId = keycloakId, Role = role, FullName = "Test User", Email = "t@t.local",
        };
        await cache.SetStringAsync(keycloakId, JsonSerializer.Serialize(profile));

        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Auth", keycloakId);
        client.DefaultRequestHeaders.Add("X-Test-Username", keycloakId);
        if (realmRoles.Length > 0)
            client.DefaultRequestHeaders.Add("X-Test-Roles", string.Join(",", realmRoles));
        return client;
    }

    /// <summary>A service-to-service client (client-credentials style: azp = exam-admin).</summary>
    protected HttpClient ServiceClient()
    {
        var client = Factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Test-Auth", "service-account-exam-admin");
        client.DefaultRequestHeaders.Add("X-Test-Username", "service-account-exam-admin");
        client.DefaultRequestHeaders.Add("X-Test-Azp", "exam-admin");
        return client;
    }

    protected static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
}
