using System;
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
}
