using System;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Services.AdminUsers;

/// <inheritdoc cref="IAdminAccountTargetResolver"/>
public class AdminAccountTargetResolver : IAdminAccountTargetResolver
{
    /// <summary>Hedefi korumalı yapan realm rolleri (büyük/küçük harf duyarsız).</summary>
    public static readonly string[] ProtectedRealmRoles = ["Admin", "exam-service"];

    // Keycloak kullanıcı id'si UUID'dir; URL path'ine girdiği için beklenmeyen karakterler reddedilir.
    private static readonly Regex KeycloakIdPattern = new("^[A-Za-z0-9-]{1,64}$", RegexOptions.CultureInvariant);

    private readonly AppDbContext _context;
    private readonly IAuthApiClient _authApiClient;
    private readonly IKeycloakService _keycloak;
    private readonly ILogger<AdminAccountTargetResolver> _logger;

    public AdminAccountTargetResolver(
        AppDbContext context, IAuthApiClient authApiClient, IKeycloakService keycloak,
        ILogger<AdminAccountTargetResolver>? logger = null)
    {
        _context = context;
        _authApiClient = authApiClient;
        _keycloak = keycloak;
        _logger = logger ?? NullLogger<AdminAccountTargetResolver>.Instance;
    }

    public async Task<AdminAccountTargetResolution> ResolveAsync(
        AdminUserTargetType targetType, int targetId, string actorKeycloakId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(actorKeycloakId))
            throw new InvalidOperationException("Admin account action requires the actor's Keycloak subject.");

        var userId = await FindUserIdAsync(targetType, targetId, ct);
        if (userId is null)
            return new(AdminAccountTargetStatus.TargetNotFound);

        string? sub;
        try
        {
            // Fail-soft DEĞİL: servis token'ı alınamazsa boş liste yerine istisna → 502 ("hesap yok" 404'ü ile karışmasın).
            var users = await _authApiClient.GetUsersByIdsOrThrowAsync(new[] { userId.Value }, ct);
            sub = users.FirstOrDefault(u => u.Id == userId.Value)?.KeycloakId;
        }
        catch (Exception ex) when (IsUpstreamFailure(ex, ct))
        {
            _logger.LogWarning(ex, "[AdminAccountTarget] auth-api lookup başarısız: {TargetType}#{TargetId}", targetType, targetId);
            return new(AdminAccountTargetStatus.UpstreamFailure);
        }

        if (string.IsNullOrWhiteSpace(sub) || !KeycloakIdPattern.IsMatch(sub))
        {
            _logger.LogWarning("[AdminAccountTarget] Keycloak hesabı çözülemedi: {TargetType}#{TargetId}", targetType, targetId);
            return new(AdminAccountTargetStatus.AccountNotFound);
        }

        if (string.Equals(sub, actorKeycloakId.Trim(), StringComparison.OrdinalIgnoreCase))
            return new(AdminAccountTargetStatus.ForbiddenSelf);

        try
        {
            // İstemciye/claim'e değil, Keycloak'taki ETKİN rollere bakılır.
            var roles = await _keycloak.GetUserRolesAsync(sub, ct);
            if (roles.RealmRoles.Any(r => ProtectedRealmRoles.Contains(r, StringComparer.OrdinalIgnoreCase))
                || roles.RealmManagementRoles.Count > 0)
            {
                return new(AdminAccountTargetStatus.ForbiddenProtectedRole);
            }
        }
        catch (KeycloakException ex) when (ex.StatusCode == 404)
        {
            _logger.LogWarning("[AdminAccountTarget] Keycloak'ta kullanıcı yok (404): {TargetType}#{TargetId}", targetType, targetId);
            return new(AdminAccountTargetStatus.AccountNotFound);
        }
        catch (Exception ex) when (ex is KeycloakException || IsUpstreamFailure(ex, ct))
        {
            _logger.LogWarning(ex, "[AdminAccountTarget] Keycloak rol sorgusu başarısız: {TargetType}#{TargetId}", targetType, targetId);
            return new(AdminAccountTargetStatus.UpstreamFailure);
        }

        return new(AdminAccountTargetStatus.Resolved, sub);
    }

    private Task<int?> FindUserIdAsync(AdminUserTargetType targetType, int targetId, CancellationToken ct) => targetType switch
    {
        AdminUserTargetType.Teacher => _context.Teachers.AsNoTracking()
            .Where(t => t.Id == targetId && !t.IsDeleted).Select(t => (int?)t.UserId).FirstOrDefaultAsync(ct),
        AdminUserTargetType.Student => _context.Students.AsNoTracking()
            .Where(s => s.Id == targetId && !s.IsDeleted).Select(s => (int?)s.UserId).FirstOrDefaultAsync(ct),
        _ => throw new ArgumentOutOfRangeException(nameof(targetType), targetType, null)
    };

    /// <summary>auth-api/Keycloak erişim hataları (kullanıcı iptali hariç).</summary>
    internal static bool IsUpstreamFailure(Exception ex, CancellationToken ct) =>
        ex is HttpRequestException or JsonException or Polly.ExecutionRejectedException
        || (ex is TaskCanceledException && !ct.IsCancellationRequested);
}
