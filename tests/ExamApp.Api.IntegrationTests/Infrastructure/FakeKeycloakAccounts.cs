using System.Collections.Concurrent;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;

namespace ExamApp.Api.IntegrationTests.Infrastructure;

/// <summary>Sahte Keycloak hesabının hata modu (issue #156).</summary>
public enum FakeKeycloakFailure
{
    None,
    /// <summary>Kullanıcı Keycloak'ta yok: rol sorgusu 404.</summary>
    UserMissing,
    /// <summary>Rol sorgusu 500.</summary>
    RoleLookupFails,
    /// <summary>reset-password 500 (şifre değişmez).</summary>
    ResetFails,
    /// <summary>reset-password 404.</summary>
    ResetNotFound,
    /// <summary>Şifre değişir / hesap kapanır, logout 500.</summary>
    LogoutFails,
    /// <summary>issue #155: <c>PUT users/{id}</c> (enabled) 500 — durum değişmez.</summary>
    StatusChangeFails,
    /// <summary>issue #155: <c>PUT users/{id}</c> (enabled) 404.</summary>
    StatusChangeNotFound
}

/// <summary>
/// issue #156: testlerin Keycloak hesabı (sub → etkin realm rolleri + realm-management client rolleri + hata modu)
/// kaydedebildiği bellek içi sahte. Reset/logout çağrıları sayılır. Kayıtsız sub → gerçek KeycloakService.
/// Respawn bunu sıfırlamaz: testler benzersiz sub kullanmalı.
/// </summary>
public sealed class FakeKeycloakAccounts
{
    private readonly ConcurrentDictionary<string, Account> _accounts = new();

    public ConcurrentDictionary<string, int> ResetCalls { get; } = new();
    public ConcurrentDictionary<string, int> LogoutCalls { get; } = new();
    public ConcurrentDictionary<string, int> SetEnabledCalls { get; } = new();

    /// <summary>issue #155: hesabın Keycloak <c>enabled</c> durumu (kayıtta varsayılan true).</summary>
    public ConcurrentDictionary<string, bool> Enabled { get; } = new();

    public void Add(string sub, string[] realmRoles, string[]? clientRoles = null, FakeKeycloakFailure failure = FakeKeycloakFailure.None)
    {
        _accounts[sub] = new Account(realmRoles, clientRoles ?? [], failure);
        Enabled[sub] = true;
    }

    public bool TryGet(string sub, out Account account) => _accounts.TryGetValue(sub, out account!);

    public sealed record Account(string[] RealmRoles, string[] ClientRoles, FakeKeycloakFailure Failure);
}

/// <summary>
/// <see cref="IKeycloakService"/> dekoratörü: #156 metotları kayıtlı sub'larda <see cref="FakeKeycloakAccounts"/>'a,
/// geri kalan her şey gerçek servise gider. Geçici şifre gerçek üretici ile üretilir. Hatalar gerçek servisle aynı
/// biçimde (<see cref="KeycloakException"/> + durum kodu) fırlatılır.
/// </summary>
public sealed class FakeKeycloakAccountsService(IKeycloakService inner, FakeKeycloakAccounts accounts) : IKeycloakService
{
    public Task<KeycloakUserRolesDto> GetUserRolesAsync(string keycloakUserId, CancellationToken ct = default)
    {
        if (!accounts.TryGet(keycloakUserId, out var a))
            return inner.GetUserRolesAsync(keycloakUserId, ct);
        return a.Failure switch
        {
            FakeKeycloakFailure.UserMissing => throw new KeycloakException("Keycloak role lookup failed: 404", 404),
            FakeKeycloakFailure.RoleLookupFails => throw new KeycloakException("Keycloak role lookup failed: 500", 500),
            _ => Task.FromResult(new KeycloakUserRolesDto(a.RealmRoles, a.ClientRoles))
        };
    }

    public Task<string> ResetPasswordAsync(string keycloakUserId, CancellationToken ct = default)
    {
        if (!accounts.TryGet(keycloakUserId, out var a))
            return inner.ResetPasswordAsync(keycloakUserId, ct);
        accounts.ResetCalls.AddOrUpdate(keycloakUserId, 1, (_, n) => n + 1);
        return a.Failure switch
        {
            FakeKeycloakFailure.ResetFails => throw new KeycloakException("Keycloak password reset failed: 500", 500),
            FakeKeycloakFailure.ResetNotFound => throw new KeycloakException("Keycloak password reset failed: 404", 404),
            _ => Task.FromResult(TemporaryPasswordGenerator.Generate())
        };
    }

    public Task LogoutUserSessionsAsync(string keycloakUserId, CancellationToken ct = default)
    {
        if (!accounts.TryGet(keycloakUserId, out var a))
            return inner.LogoutUserSessionsAsync(keycloakUserId, ct);
        accounts.LogoutCalls.AddOrUpdate(keycloakUserId, 1, (_, n) => n + 1);
        if (a.Failure == FakeKeycloakFailure.LogoutFails)
            throw new KeycloakException("Keycloak session logout failed: 500", 500);
        return Task.CompletedTask;
    }

    public Task SetEnabledAsync(string keycloakUserId, bool enabled, CancellationToken ct = default)
    {
        if (!accounts.TryGet(keycloakUserId, out var a))
            return inner.SetEnabledAsync(keycloakUserId, enabled, ct);
        accounts.SetEnabledCalls.AddOrUpdate(keycloakUserId, 1, (_, n) => n + 1);
        switch (a.Failure)
        {
            case FakeKeycloakFailure.StatusChangeFails:
                throw new KeycloakException("Keycloak account status update failed: 500", 500);
            case FakeKeycloakFailure.StatusChangeNotFound:
                throw new KeycloakException("Keycloak account status update failed: 404", 404);
        }
        accounts.Enabled[keycloakUserId] = enabled;
        return Task.CompletedTask;
    }

    public Task<string> GetAccessTokenAsync(string username, string password, string clientId, string clientSecret)
        => inner.GetAccessTokenAsync(username, password, clientId, clientSecret);
    public Task<string> GetUserInfoAsync(string accessToken) => inner.GetUserInfoAsync(accessToken);
    public Task<bool> ValidateTokenAsync(string token) => inner.ValidateTokenAsync(token);
    public Task<string> GetUserIdFromTokenAsync(string token) => inner.GetUserIdFromTokenAsync(token);
    public Task<string> GetUserNameFromTokenAsync(string token) => inner.GetUserNameFromTokenAsync(token);
    public Task<string> CreateUserAsync(string username, string password, string email, string firstName, string lastName)
        => inner.CreateUserAsync(username, password, email, firstName, lastName);
    public Task DeleteUserAsync(string userId) => inner.DeleteUserAsync(userId);
    public Task LogoutAsync(string refreshToken) => inner.LogoutAsync(refreshToken);
    public Task<TokenResponseDto> LoginAsync(string username, string password) => inner.LoginAsync(username, password);
    public Task SetRoleAsync(string keycloakUserId, UserRole userRole) => inner.SetRoleAsync(keycloakUserId, userRole);
    public Task<TokenResponseDto> ExchangeTokenAsync(string code) => inner.ExchangeTokenAsync(code);
    public Task<TokenResponseDto> RefreshTokenAsync(string refreshToken) => inner.RefreshTokenAsync(refreshToken);
    public Task SetSchoolIdAttributeAsync(string keycloakUserId, int? schoolId) => inner.SetSchoolIdAttributeAsync(keycloakUserId, schoolId);
}
