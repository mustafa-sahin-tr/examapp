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

    public virtual async Task<UserProfileDto?> GetAsync(string keycloakId)
    {
        var json = await _cache.GetStringAsync(keycloakId);
        return json is not null
            ? JsonSerializer.Deserialize<UserProfileDto>(json)
            : null;
    }

    public virtual async Task SetAsync(string keycloakId, UserProfileDto profile, TimeSpan? expiration = null)
    {
        var json = JsonSerializer.Serialize(profile);
        var options = new DistributedCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration ?? TimeSpan.FromHours(1)
        };

        await _cache.SetStringAsync(keycloakId, json, options);
    }

    /// <summary>
    /// Bir kullanıcının cache'lenmiş profilini siler. issue #189 / #194: kullanıcının okulu
    /// değiştiğinde (transfer vb.) bu metod çağrılmalı ki bir sonraki istekte SchoolId DB'den
    /// yeniden doğrulanıp cache'lensin — #194'ün invalidation noktası budur. Register akışında
    /// (Teacher/StudentController) artık tek seferlik SetAsync ile güncel Role+SchoolId
    /// yazıldığı için oradan çağrılmıyor; okul transferi gibi asenkron değişikliklerde kullanılacak.
    /// </summary>
    public virtual Task RemoveAsync(string keycloakId) => _cache.RemoveAsync(keycloakId);

    public virtual async Task<UserProfileDto> GetOrSetAsync(string keycloakId, Func<Task<UserProfileDto>> loader, TimeSpan? expiration = null)
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
