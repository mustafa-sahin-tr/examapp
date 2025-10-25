using System.Text.Json;
using System.Text.Json.Serialization;
using ExamApp.Api.Models.Dtos;
using Microsoft.Extensions.Caching.Distributed;

public class UserProfileCacheService
{
    private readonly IDistributedCache _cache;
    private readonly ILogger<UserProfileCacheService> _logger;

    public UserProfileCacheService(IDistributedCache cache, ILogger<UserProfileCacheService> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    public async Task<UserProfileDto?> GetAsync(string keycloakId)
    {
        var json = await _cache.GetStringAsync(keycloakId);
        return json is not null
            ? JsonSerializer.Deserialize<UserProfileDto>(json)
            : null;
    }

    public async Task SetAsync(string keycloakId, UserProfileDto profile, TimeSpan? expiration = null)
    {
        var json = JsonSerializer.Serialize(profile);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration ?? TimeSpan.FromHours(1)
        };

        await _cache.SetStringAsync(keycloakId, json, options);
    }    

    public async Task<UserProfileDto> GetOrSetAsync(string keycloakId, Func<Task<UserProfileDto>> loader, TimeSpan? expiration = null)
    {
        var cached = await GetAsync(keycloakId);
        if (cached != null)
        {
            _logger.LogInformation("User profile hit from Redis: {KeycloakId}", keycloakId);
            return cached;
        }

        _logger.LogInformation("User profile cache miss, loading from DB: {KeycloakId}", keycloakId);
        var profile = await loader();

        await SetAsync(keycloakId, profile, expiration);
        return profile;
    }
}
