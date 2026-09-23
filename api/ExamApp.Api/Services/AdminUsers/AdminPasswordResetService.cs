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
/// issue #156. Loglar yalnızca hedef tür/id, aktör sub'ı ve hata sınıfını içerir; geçici şifre asla.
/// Keycloak hatalarında <see cref="KeycloakException"/> mesajı yalnızca durum kodu taşır (bkz. KeycloakService).
/// </summary>
public class AdminPasswordResetService : IAdminPasswordResetService
{
    private readonly IAdminAccountTargetResolver _targets;
    private readonly IKeycloakService _keycloak;
    private readonly IAdminUserActionAuditService _audit;
    private readonly ILogger<AdminPasswordResetService> _logger;

    public AdminPasswordResetService(
        IAdminAccountTargetResolver targets,
        IKeycloakService keycloak,
        IAdminUserActionAuditService audit,
        ILogger<AdminPasswordResetService>? logger = null)
    {
        _targets = targets;
        _keycloak = keycloak;
        _audit = audit;
        _logger = logger ?? NullLogger<AdminPasswordResetService>.Instance;
    }

    public async Task<AdminPasswordResetResult> ResetAsync(
        AdminUserTargetType targetType, int targetId, string actorKeycloakId, CancellationToken ct = default)
    {
        var target = await _targets.ResolveAsync(targetType, targetId, actorKeycloakId, ct);
        var record = new AdminUserActionRecord(actorKeycloakId, AdminUserAction.PasswordReset, targetType, targetId);

        switch (target.Status)
        {
            case AdminAccountTargetStatus.TargetNotFound:
                await _audit.TryRecordAsync(record, AdminUserActionOutcome.NotFound);
                return new(AdminPasswordResetStatus.TargetNotFound);
            case AdminAccountTargetStatus.AccountNotFound:
                await _audit.TryRecordAsync(record, AdminUserActionOutcome.NotFound);
                return new(AdminPasswordResetStatus.AccountNotFound);
            case AdminAccountTargetStatus.ForbiddenSelf:
                _logger.LogWarning("[AdminPasswordReset] Admin kendi şifresini sıfırlamaya çalıştı: actor={Actor}", actorKeycloakId);
                await _audit.TryRecordAsync(record, AdminUserActionOutcome.Denied);
                return new(AdminPasswordResetStatus.ForbiddenSelf);
            case AdminAccountTargetStatus.ForbiddenProtectedRole:
                _logger.LogWarning("[AdminPasswordReset] Korumalı hedef reddedildi: {TargetType}#{TargetId} actor={Actor}",
                    targetType, targetId, actorKeycloakId);
                await _audit.TryRecordAsync(record, AdminUserActionOutcome.Denied);
                return new(AdminPasswordResetStatus.ForbiddenProtectedRole);
            case AdminAccountTargetStatus.UpstreamFailure:
                // Yan etki yok; aksiyon talep aşamasına gelmedi (log resolver'da).
                return new(AdminPasswordResetStatus.UpstreamFailure);
        }

        var sub = target.KeycloakId!;

        // Fail-closed: iz yazılamazsa istisna yukarı çıkar (500), Keycloak'a hiç gidilmez.
        var auditId = await _audit.RecordAsync(record, AdminUserActionOutcome.Requested, ct);

        // Bu noktadan sonra istemci iptali akışı yarıda bırakmasın (şifre değişip oturumlar açık kalmasın).
        string temporaryPassword;
        try
        {
            temporaryPassword = await _keycloak.ResetPasswordAsync(sub, CancellationToken.None);
        }
        catch (KeycloakException ex) when (ex.StatusCode == 404)
        {
            _logger.LogWarning("[AdminPasswordReset] Keycloak'ta kullanıcı yok (404): {TargetType}#{TargetId}", targetType, targetId);
            await _audit.TryUpdateOutcomeAsync(auditId, AdminUserActionOutcome.NotFound);
            return new(AdminPasswordResetStatus.AccountNotFound);
        }
        catch (Exception ex) when (ex is KeycloakException || AdminAccountTargetResolver.IsUpstreamFailure(ex, CancellationToken.None))
        {
            // ex.Message yalnızca durum kodu içerir (KeycloakService); şifre içermez.
            _logger.LogWarning(ex, "[AdminPasswordReset] Keycloak şifre set edemedi: {TargetType}#{TargetId}", targetType, targetId);
            await _audit.TryUpdateOutcomeAsync(auditId, AdminUserActionOutcome.ResetFailed);
            return new(AdminPasswordResetStatus.UpstreamFailure);
        }

        try
        {
            await _keycloak.LogoutUserSessionsAsync(sub, CancellationToken.None);
        }
        catch (Exception ex) when (ex is KeycloakException || AdminAccountTargetResolver.IsUpstreamFailure(ex, CancellationToken.None))
        {
            // Şifre değişti ama oturumlar açık: şifre GÖSTERİLMEZ ("şifre gösterildi ⇒ oturumlar kapandı" garantisi).
            _logger.LogError(ex, "[AdminPasswordReset] Şifre değişti ama oturumlar kapatılamadı: {TargetType}#{TargetId}", targetType, targetId);
            await _audit.TryUpdateOutcomeAsync(auditId, AdminUserActionOutcome.SessionRevokeFailed);
            return new(AdminPasswordResetStatus.SessionRevokeFailed);
        }

        await _audit.TryUpdateOutcomeAsync(auditId, AdminUserActionOutcome.Succeeded);
        _logger.LogInformation("[AdminPasswordReset] Şifre sıfırlandı: {TargetType}#{TargetId} actor={Actor}",
            targetType, targetId, actorKeycloakId);
        return new(AdminPasswordResetStatus.Success, temporaryPassword);
    }
}
