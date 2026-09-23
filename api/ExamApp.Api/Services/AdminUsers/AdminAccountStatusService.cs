using System;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ExamApp.Api.Services.AdminUsers;

/// <summary>
/// issue #155. <see cref="AdminPasswordResetService"/> (#156) ile aynı hedef çözümü, audit ve hata deseni.
/// Loglar yalnızca hedef tür/id, aktör sub'ı ve hata sınıfını içerir.
/// </summary>
public class AdminAccountStatusService : IAdminAccountStatusService
{
    private readonly IAdminAccountTargetResolver _targets;
    private readonly IKeycloakService _keycloak;
    private readonly IAdminUserActionAuditService _audit;
    private readonly UserProfileCacheService? _profileCache;
    private readonly ILogger<AdminAccountStatusService> _logger;

    public AdminAccountStatusService(
        IAdminAccountTargetResolver targets,
        IKeycloakService keycloak,
        IAdminUserActionAuditService audit,
        UserProfileCacheService? profileCache = null,
        ILogger<AdminAccountStatusService>? logger = null)
    {
        _targets = targets;
        _keycloak = keycloak;
        _audit = audit;
        _profileCache = profileCache;
        _logger = logger ?? NullLogger<AdminAccountStatusService>.Instance;
    }

    public async Task<AdminAccountStatusChangeResult> SetEnabledAsync(
        AdminUserTargetType targetType, int targetId, bool enabled, string actorKeycloakId, CancellationToken ct = default)
    {
        var target = await _targets.ResolveAsync(targetType, targetId, actorKeycloakId, ct);
        var action = enabled ? AdminUserAction.AccountEnabled : AdminUserAction.AccountDisabled;
        var record = new AdminUserActionRecord(actorKeycloakId, action, targetType, targetId);

        switch (target.Status)
        {
            case AdminAccountTargetStatus.TargetNotFound:
                await _audit.TryRecordAsync(record, AdminUserActionOutcome.NotFound);
                return new(AdminAccountStatusChangeStatus.TargetNotFound);
            case AdminAccountTargetStatus.AccountNotFound:
                await _audit.TryRecordAsync(record, AdminUserActionOutcome.NotFound);
                return new(AdminAccountStatusChangeStatus.AccountNotFound);
            case AdminAccountTargetStatus.ForbiddenSelf:
                _logger.LogWarning("[AdminAccountStatus] Admin kendi hesap durumunu değiştirmeye çalıştı: actor={Actor}", actorKeycloakId);
                await _audit.TryRecordAsync(record, AdminUserActionOutcome.Denied);
                return new(AdminAccountStatusChangeStatus.ForbiddenSelf);
            case AdminAccountTargetStatus.ForbiddenProtectedRole:
                _logger.LogWarning("[AdminAccountStatus] Korumalı hedef reddedildi: {TargetType}#{TargetId} actor={Actor}",
                    targetType, targetId, actorKeycloakId);
                await _audit.TryRecordAsync(record, AdminUserActionOutcome.Denied);
                return new(AdminAccountStatusChangeStatus.ForbiddenProtectedRole);
            case AdminAccountTargetStatus.UpstreamFailure:
                // Yan etki yok; aksiyon talep aşamasına gelmedi (log resolver'da).
                return new(AdminAccountStatusChangeStatus.UpstreamFailure);
        }

        var sub = target.KeycloakId!;

        // Fail-closed: iz yazılamazsa istisna yukarı çıkar (500), Keycloak'a hiç gidilmez.
        var auditId = await _audit.RecordAsync(record, AdminUserActionOutcome.Requested, ct);

        // Bu noktadan sonra istemci iptali akışı yarıda bırakmasın (hesap kapanıp oturumlar açık kalmasın).
        try
        {
            await _keycloak.SetEnabledAsync(sub, enabled, CancellationToken.None);
        }
        catch (KeycloakException ex) when (ex.StatusCode == 404)
        {
            _logger.LogWarning("[AdminAccountStatus] Keycloak'ta kullanıcı yok (404): {TargetType}#{TargetId}", targetType, targetId);
            await _audit.TryUpdateOutcomeAsync(auditId, AdminUserActionOutcome.NotFound);
            return new(AdminAccountStatusChangeStatus.AccountNotFound);
        }
        catch (Exception ex) when (ex is KeycloakException || AdminAccountTargetResolver.IsUpstreamFailure(ex, CancellationToken.None))
        {
            _logger.LogWarning(ex, "[AdminAccountStatus] Keycloak hesap durumunu değiştiremedi: {TargetType}#{TargetId} enabled={Enabled}",
                targetType, targetId, enabled);
            await _audit.TryUpdateOutcomeAsync(auditId, AdminUserActionOutcome.StatusChangeFailed);
            return new(AdminAccountStatusChangeStatus.UpstreamFailure);
        }

        if (!enabled)
        {
            Exception? revokeError = null;
            try
            {
                // Açık oturumlar / refresh token'lar hemen geçersiz olsun (yalnızca mevcut access token'ın TTL'i kalır).
                await _keycloak.LogoutUserSessionsAsync(sub, CancellationToken.None);
            }
            catch (Exception ex) when (ex is KeycloakException || AdminAccountTargetResolver.IsUpstreamFailure(ex, CancellationToken.None))
            {
                revokeError = ex;
            }

            // Hesap kapandı: logout başarısız olsa da önbellekteki profil (rol/okul) düşürülür; sonraki istek DB'den kurulur.
            await TryInvalidateProfileAsync(sub, targetType, targetId);

            if (revokeError is not null)
            {
                _logger.LogError(revokeError, "[AdminAccountStatus] Hesap devre dışı ama oturumlar kapatılamadı: {TargetType}#{TargetId}",
                    targetType, targetId);
                await _audit.TryUpdateOutcomeAsync(auditId, AdminUserActionOutcome.SessionRevokeFailed);
                return new(AdminAccountStatusChangeStatus.SessionRevokeFailed);
            }
        }

        await _audit.TryUpdateOutcomeAsync(auditId, AdminUserActionOutcome.Succeeded);
        _logger.LogInformation("[AdminAccountStatus] Hesap durumu değişti: {TargetType}#{TargetId} enabled={Enabled} actor={Actor}",
            targetType, targetId, enabled, actorKeycloakId);
        return new(AdminAccountStatusChangeStatus.Success, enabled);
    }

    private async Task TryInvalidateProfileAsync(string sub, AdminUserTargetType targetType, int targetId)
    {
        if (_profileCache is null)
            return;
        try
        {
            await _profileCache.RemoveAsync(sub);
        }
        catch (Exception ex)
        {
            // Redis erişilemezse akış durmaz: oturumlar yine kapatılır, profil en geç önbellek süresi (1 saat) sonunda düşer.
            _logger.LogWarning(ex, "[AdminAccountStatus] Profil önbelleği düşürülemedi: {TargetType}#{TargetId}", targetType, targetId);
        }
    }
}
