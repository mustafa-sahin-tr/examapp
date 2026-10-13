using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using ExamApp.Foundation.Security;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services;

/// <inheritdoc cref="IChildAccountService"/>
public sealed class ChildAccountService : IChildAccountService
{
    private readonly AppDbContext _context;
    private readonly IKeycloakService _keycloak;
    private readonly ILogger<ChildAccountService> _logger;

    public ChildAccountService(AppDbContext context, IKeycloakService keycloak, ILogger<ChildAccountService> logger)
    {
        _context = context;
        _keycloak = keycloak;
        _logger = logger;
    }

    private static ChildAccountResult Fail(ChildAccountOutcome outcome, string errorCode) => new(outcome, errorCode);

    public async Task<ChildAccountResult> CreateAsync(CreateChildAccountRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        // İkinci savunma: exam API aynı kuralları uygular; Keycloak'a geçersiz girdi gitmez.
        var username = ChildAccountRules.NormalizeUsername(request.Username);
        if (!ChildAccountRules.IsValidUsername(username))
            return Fail(ChildAccountOutcome.Invalid, ChildAccountErrorCodes.InvalidUsername);
        if (!ChildAccountRules.IsValidName(request.FirstName) || !ChildAccountRules.IsValidName(request.LastName))
            return Fail(ChildAccountOutcome.Invalid, ChildAccountErrorCodes.InvalidName);
        if (!ChildAccountRules.IsValidPassword(request.Password) || ChildAccountRules.PasswordContainsUsername(request.Password, username))
            return Fail(ChildAccountOutcome.Invalid, ChildAccountErrorCodes.PasswordRejected);

        var firstName = request.FirstName.Trim();
        var lastName = request.LastName.Trim();

        string keycloakId;
        try
        {
            keycloakId = await _keycloak.CreateParentManagedUserAsync(username, firstName, lastName, ct);
        }
        catch (KeycloakException ex) when (ex.Kind == KeycloakFailureKind.Conflict)
        {
            _logger.LogInformation("[ChildAccounts] Kullanıcı adı Keycloak'ta zaten var (409).");
            return Fail(ChildAccountOutcome.Conflict, ChildAccountErrorCodes.UsernameTaken);
        }
        catch (KeycloakException ex) when (ex.Kind == KeycloakFailureKind.Validation)
        {
            _logger.LogWarning(ex, "[ChildAccounts] Keycloak çocuk kullanıcısını doğrulama hatasıyla reddetti (400).");
            return Fail(ChildAccountOutcome.Invalid, ChildAccountErrorCodes.InvalidUsername);
        }
        catch (Exception ex) when (IsUpstream(ex, ct))
        {
            _logger.LogError(ex, "[ChildAccounts] Keycloak çocuk kullanıcısı oluşturulamadı.");
            return Fail(ChildAccountOutcome.UpstreamFailure, ChildAccountErrorCodes.Unavailable);
        }

        // Geri dönüşsüz nokta: Keycloak kullanıcısı var. Bundan sonra istemci iptali yarım iş bırakmasın (CancellationToken.None)
        // ve HER hata kullanıcıyı geri alsın (register #240 ile aynı karar).
        var stage = "password";
        try
        {
            await _keycloak.ResetPasswordAsync(keycloakId, request.Password, CancellationToken.None);

            stage = "role";
            var role = await _keycloak.GetRealmRoleAsync(ChildAccountRules.StudentRole, CancellationToken.None);
            await _keycloak.AddRealmRoleMappingAsync(keycloakId, role, CancellationToken.None);

            stage = "identity";
            var user = new User
            {
                KeycloakId = keycloakId,
                FullName = $"{firstName} {lastName}",
                // E-posta yok (veli opsiyonel e-postayı sonra ekler); kolon NOT NULL olduğu için boş dize.
                Email = string.Empty,
                Role = ChildAccountRules.StudentRole,
                RoleUpdatedAtUtc = DateTime.UtcNow
            };

            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear();
                await using var tx = await _context.Database.BeginTransactionAsync(CancellationToken.None);
                _context.Users.Add(user);
                await _context.SaveChangesAsync(CancellationToken.None);

                // Register (#185) ile aynı: BadgeService yeni kullanıcının dilini event'le öğrenir.
                _context.OutboxMessages.Add(new OutboxMessage
                {
                    Type = OutboxEventRegistry.NameFor<UserPreferredLocaleChangedEvent>(),
                    Content = JsonSerializer.Serialize(new UserPreferredLocaleChangedEvent
                    {
                        UserId = user.Id,
                        KeycloakId = keycloakId,
                        PreferredLocale = user.PreferredLocale,
                        ChangedAtUtc = DateTime.UtcNow
                    })
                });
                await _context.SaveChangesAsync(CancellationToken.None);
                await tx.CommitAsync(CancellationToken.None);
            });

            _logger.LogInformation("[ChildAccounts] Çocuk hesabı oluşturuldu: userId={UserId}", user.Id);
            return new ChildAccountResult(ChildAccountOutcome.Success, Created: new CreateChildAccountResponse
            {
                UserId = user.Id,
                KeycloakId = keycloakId
            });
        }
        catch (KeycloakException ex) when (stage == "password" && ex.Kind == KeycloakFailureKind.Validation)
        {
            // Realm parola politikası reddetti (gövde parolayı içermez).
            _logger.LogWarning("[ChildAccounts] Keycloak parola politikası çocuk hesabının parolasını reddetti; hesap geri alınıyor.");
            await TryDeleteKeycloakUserAsync(keycloakId);
            return Fail(ChildAccountOutcome.Invalid, ChildAccountErrorCodes.PasswordRejected);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ChildAccounts] Çocuk hesabı tamamlanamadı (aşama={Stage}); Keycloak kullanıcısı geri alınıyor.", stage);
            await TryDeleteKeycloakUserAsync(keycloakId);
            return Fail(ChildAccountOutcome.UpstreamFailure, ChildAccountErrorCodes.Unavailable);
        }
    }

    public async Task<ChildAccountResult> DeleteAsync(int userId, CancellationToken ct = default)
    {
        var user = await _context.Users.IgnoreQueryFilters().AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.KeycloakId, u.Role })
            .FirstOrDefaultAsync(ct);
        if (user == null)
            return Fail(ChildAccountOutcome.NotFound, ChildAccountErrorCodes.NotFound);
        if (!string.Equals(user.Role, ChildAccountRules.StudentRole, StringComparison.OrdinalIgnoreCase))
            return Fail(ChildAccountOutcome.NotFound, ChildAccountErrorCodes.NotFound);

        try
        {
            var managed = await IsParentManagedAsync(user.KeycloakId, ct);
            if (managed == false)
            {
                // Veli tarafından açılmamış bir hesap bu uçla ASLA silinmez.
                _logger.LogWarning("[ChildAccounts] parent_managed olmayan hesap için silme reddedildi: userId={UserId}", userId);
                return Fail(ChildAccountOutcome.NotFound, ChildAccountErrorCodes.NotFound);
            }
            if (managed == true)
                await _keycloak.TryDeleteUserAsync(user.KeycloakId, CancellationToken.None);
        }
        catch (Exception ex) when (IsUpstream(ex, ct))
        {
            _logger.LogError(ex, "[ChildAccounts] Kompanzasyon: Keycloak kullanıcısı silinemedi: userId={UserId}", userId);
            return Fail(ChildAccountOutcome.UpstreamFailure, ChildAccountErrorCodes.Unavailable);
        }

        // Hiç kullanılmamış (kompanzasyon) hesap: satır gerçekten silinir — yetim identity satırı kalmasın.
        await _context.Users.IgnoreQueryFilters().Where(u => u.Id == userId).ExecuteDeleteAsync(CancellationToken.None);
        _logger.LogInformation("[ChildAccounts] Çocuk hesabı geri alındı: userId={UserId}", userId);
        return ChildAccountResult.Ok;
    }

    public async Task<ChildAccountResult> ResetPasswordAsync(int userId, string? password, CancellationToken ct = default)
    {
        if (!ChildAccountRules.IsValidPassword(password))
            return Fail(ChildAccountOutcome.Invalid, ChildAccountErrorCodes.PasswordRejected);

        var keycloakId = await _context.Users.AsNoTracking()
            .Where(u => u.Id == userId && u.Role == ChildAccountRules.StudentRole)
            .Select(u => u.KeycloakId)
            .FirstOrDefaultAsync(ct);
        if (string.IsNullOrWhiteSpace(keycloakId))
            return Fail(ChildAccountOutcome.NotFound, ChildAccountErrorCodes.NotFound);

        try
        {
            if (await IsParentManagedAsync(keycloakId, ct) != true)
            {
                _logger.LogWarning("[ChildAccounts] parent_managed olmayan / bulunamayan hesapta şifre sıfırlama reddedildi: userId={UserId}", userId);
                return Fail(ChildAccountOutcome.NotFound, ChildAccountErrorCodes.NotFound);
            }

            await _keycloak.ResetPasswordAsync(keycloakId, password!, CancellationToken.None);
        }
        catch (KeycloakException ex) when (ex.Kind == KeycloakFailureKind.Validation)
        {
            _logger.LogWarning("[ChildAccounts] Keycloak parola politikası yeni parolayı reddetti: userId={UserId}", userId);
            return Fail(ChildAccountOutcome.Invalid, ChildAccountErrorCodes.PasswordRejected);
        }
        catch (KeycloakException ex) when (ex.StatusCode == StatusCodes.Status404NotFound)
        {
            return Fail(ChildAccountOutcome.NotFound, ChildAccountErrorCodes.NotFound);
        }
        catch (Exception ex) when (IsUpstream(ex, ct))
        {
            _logger.LogError(ex, "[ChildAccounts] Şifre sıfırlanamadı: userId={UserId}", userId);
            return Fail(ChildAccountOutcome.UpstreamFailure, ChildAccountErrorCodes.Unavailable);
        }

        // Eski parolayla açık oturumlar kapanır (best-effort: parola zaten değişti).
        try
        {
            await _keycloak.LogoutAsync(keycloakId);
        }
        catch (Exception ex) when (IsUpstream(ex, CancellationToken.None))
        {
            _logger.LogWarning(ex, "[ChildAccounts] Şifre değişti ama oturumlar kapatılamadı: userId={UserId}", userId);
        }

        _logger.LogInformation("[ChildAccounts] Çocuk hesabının şifresi veli tarafından sıfırlandı: userId={UserId}", userId);
        return ChildAccountResult.Ok;
    }

    /// <summary>true = işaretli, false = işaretsiz, null = Keycloak'ta kullanıcı yok (404).</summary>
    private async Task<bool?> IsParentManagedAsync(string keycloakId, CancellationToken ct)
    {
        try
        {
            var attributes = await _keycloak.GetUserAttributesAsync(keycloakId, ct);
            return attributes.TryGetValue(ChildAccountRules.KeycloakParentManagedAttribute, out var value)
                   && string.Equals(value, ChildAccountRules.KeycloakParentManagedValue, StringComparison.Ordinal);
        }
        catch (KeycloakException ex) when (ex.StatusCode == StatusCodes.Status404NotFound)
        {
            return null;
        }
    }

    private async Task TryDeleteKeycloakUserAsync(string keycloakId)
    {
        try
        {
            await _keycloak.TryDeleteUserAsync(keycloakId, CancellationToken.None);
        }
        catch (Exception cleanupEx)
        {
            _logger.LogError(cleanupEx, "[ChildAccounts] Yarım kalan çocuk hesabı Keycloak'tan silinemedi ({KeycloakId})", keycloakId);
        }
    }

    private static bool IsUpstream(Exception ex, CancellationToken ct)
        => ex is KeycloakException or HttpRequestException or Polly.ExecutionRejectedException
           || (ex is OperationCanceledException && !ct.IsCancellationRequested);
}
