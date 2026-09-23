using System;
using System.Collections.Generic;
using System.Threading;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Services.Interfaces;

public interface IKeycloakService
{
    Task<string> GetAccessTokenAsync(string username, string password, string clientId, string clientSecret);
    Task<string> GetUserInfoAsync(string accessToken);
    Task<bool> ValidateTokenAsync(string token);
    Task<string> GetUserIdFromTokenAsync(string token);
    Task<string> GetUserNameFromTokenAsync(string token);
    Task<string> CreateUserAsync(string username, string password, string email, string firstName, string lastName);
    Task DeleteUserAsync(string userId);
    Task LogoutAsync(string refreshToken);
    Task<TokenResponseDto> LoginAsync(string username, string password);
    Task SetRoleAsync(string keycloakUserId, UserRole userRole) ;
    Task <TokenResponseDto> ExchangeTokenAsync(string code);
    Task<TokenResponseDto> RefreshTokenAsync(string refreshToken);

    /// <summary>
    /// issue #189: Keycloak kullanıcısının "school_id" attribute'unu günceller (JWT mapper bunu
    /// claim'e taşır — yalnızca ipucu amaçlı, yetki kararları hâlâ sunucu tarafında DB'den
    /// doğrulanır). schoolId null ise attribute silinir. Diğer attribute'ları korumak için önce
    /// mevcut kullanıcı GET edilir, attributes merge edilip PUT edilir. Keycloak hatası kayıt
    /// akışını kırmamalı — çağıran taraf try/catch ile sarmalı.
    /// </summary>
    Task SetSchoolIdAttributeAsync(string keycloakUserId, int? schoolId);

    /// <summary>
    /// issue #156: kullanıcının ETKİN realm rolleri (<c>role-mappings/realm/composite</c>) ve realm-management client
    /// rolleri (<c>role-mappings/clients/{uuid}/composite</c>; UUID admin servis hesabının mapping'lerinden çözülür,
    /// çözülemezse doğrudan atamalara düşülür). Kullanıcı yoksa <see cref="Helpers.KeycloakException"/> StatusCode=404.
    /// </summary>
    Task<KeycloakUserRolesDto> GetUserRolesAsync(string keycloakUserId, CancellationToken ct = default);

    /// <summary>
    /// issue #156: CSPRNG ile geçici şifre üretir ve <c>PUT users/{id}/reset-password</c>
    /// (<c>{type:"password", value, temporary:true}</c>) ile set eder; üretilen şifreyi döner.
    /// Şifre hiçbir log/exception mesajına yazılmaz. Hata → <see cref="Helpers.KeycloakException"/> (yalnızca durum kodu).
    /// </summary>
    Task<string> ResetPasswordAsync(string keycloakUserId, CancellationToken ct = default);

    /// <summary>
    /// issue #156: kullanıcının tüm Keycloak oturumlarını sonlandırır (<c>POST users/{id}/logout</c>).
    /// Hata → <see cref="Helpers.KeycloakException"/>.
    /// </summary>
    Task LogoutUserSessionsAsync(string keycloakUserId, CancellationToken ct = default);

    /// <summary>
    /// issue #155: hesabı etkinleştirir / devre dışı bırakır: <c>GET users/{id}</c> tam temsil → yalnızca <c>enabled</c>
    /// değiştirilir (salt okunur <c>userProfileMetadata</c>/<c>access</c> düşürülür) → <c>PUT users/{id}</c>. Diğer alanlar
    /// ve attribute'lar korunur; zaten aynı durumdaysa da başarılıdır (idempotent).
    /// Devre dışı kullanıcıya Keycloak yeni token (login/refresh) vermez. Hata → <see cref="Helpers.KeycloakException"/>
    /// (yalnızca durum kodu; kullanıcı yoksa 404).
    /// </summary>
    Task SetEnabledAsync(string keycloakUserId, bool enabled, CancellationToken ct = default);
}
