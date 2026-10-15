using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using ExamApp.Foundation.Security;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services;

/// <inheritdoc cref="IParentAccountService"/>
public sealed class ParentAccountService : IParentAccountService
{
    /// <summary>Bu rollerden biri olan Keycloak hesabı veli daveti için KULLANILAMAZ (hesaba dokunulmaz).</summary>
    private static readonly string[] NonParentRoles = ["Student", "Teacher", "Admin", "SuperAdmin"];

    private readonly AppDbContext _context;
    private readonly IKeycloakService _keycloak;
    private readonly ILogger<ParentAccountService> _logger;

    public ParentAccountService(AppDbContext context, IKeycloakService keycloak, ILogger<ParentAccountService> logger)
    {
        _context = context;
        _keycloak = keycloak;
        _logger = logger;
    }

    private static ParentAccountResult Fail(ParentAccountOutcomeKind outcome, string errorCode) => new(outcome, errorCode);

    private static ParentAccountResult Found(string outcome, int? userId = null, string? keycloakId = null)
        => new(ParentAccountOutcomeKind.Success, Lookup: new ParentAccountLookupResponse { Outcome = outcome, UserId = userId, KeycloakId = keycloakId });

    public async Task<ParentAccountResult> LookupAsync(string? email, CancellationToken ct = default)
    {
        var normalized = ParentContactRules.NormalizeEmail(email);
        if (normalized == null)
            return Fail(ParentAccountOutcomeKind.Invalid, ParentAccountErrorCodes.InvalidEmail);

        try
        {
            var users = await _keycloak.FindUsersByEmailAsync(normalized, ct);
            if (users.Count == 0)
            {
                // Keycloak'ta yok ama identity'de bu e-postayla (silinmiş dahil) satır var: sahibi belirsiz — kullanılmaz.
                var orphan = await _context.Users.IgnoreQueryFilters().AsNoTracking().AnyAsync(u => u.Email.ToLower() == normalized, ct);
                return Found(orphan ? ParentAccountOutcomes.NotParentAccount : ParentAccountOutcomes.None);
            }
            if (users.Count > 1)
            {
                _logger.LogWarning("[ParentAccounts] E-posta birden çok Keycloak hesabında; veli daveti için kullanılmadı.");
                return Found(ParentAccountOutcomes.NotParentAccount);
            }

            var user = users[0];
            var roles = await _keycloak.GetUserRealmRoleNamesAsync(user.Id, ct);
            var isParent = roles.Contains(ParentContactRules.ParentRole, StringComparer.OrdinalIgnoreCase)
                           && !roles.Any(r => NonParentRoles.Contains(r, StringComparer.OrdinalIgnoreCase));
            if (!isParent)
                return Found(ParentAccountOutcomes.NotParentAccount);

            var identity = await _context.Users.AsNoTracking()
                .Where(u => u.KeycloakId == user.Id)
                .Select(u => new { u.Id, u.Role })
                .FirstOrDefaultAsync(ct);
            // Identity satırı yoksa (Keycloak velisi uygulamaya hiç girmedi; salt okunur — yazmayız): UnverifiedParent gibi token'lı davet —
            // veli giriş yapınca identity satırı oluşur, sonra oturum + token ile kabul eder (review R4).
            if (identity == null)
                return Found(ParentAccountOutcomes.UnverifiedParent);
            if (!string.Equals(identity.Role, ParentContactRules.ParentRole, StringComparison.OrdinalIgnoreCase))
                return Found(ParentAccountOutcomes.NotParentAccount);

            // Security C1: realm verifyEmail kapalı ve veli e-postasını kendisi değiştirebilir — doğrulanmamış e-posta veli KİMLİĞİ değildir.
            // Uygulama içi onay yolu yalnız doğrulanmış e-postada; aksi exam API token'lı davet gönderir (kabul = oturum + token).
            return user.EmailVerified
                ? Found(ParentAccountOutcomes.ExistingParent, identity.Id, user.Id)
                : Found(ParentAccountOutcomes.UnverifiedParent);
        }
        catch (Exception ex) when (IsUpstream(ex, ct))
        {
            _logger.LogError(ex, "[ParentAccounts] Veli e-postası Keycloak'ta aranamadı.");
            return Fail(ParentAccountOutcomeKind.UpstreamFailure, ParentAccountErrorCodes.Unavailable);
        }
    }

    public async Task<ParentAccountResult> CreateAsync(CreateParentAccountRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var email = ParentContactRules.NormalizeEmail(request.Email);
        if (email == null)
            return Fail(ParentAccountOutcomeKind.Invalid, ParentAccountErrorCodes.InvalidEmail);
        var fullName = ParentContactRules.NormalizeName(request.FullName);
        if (fullName == null)
            return Fail(ParentAccountOutcomeKind.Invalid, ParentAccountErrorCodes.InvalidName);
        if (!ChildAccountRules.IsValidPassword(request.Password))
            return Fail(ParentAccountOutcomeKind.Invalid, ParentAccountErrorCodes.PasswordRejected);

        string keycloakId;
        try
        {
            // Davetten bu yana e-posta bir hesaba bağlandıysa (başka davetin aktivasyonu / kendi kaydı) dokunulmaz.
            if ((await _keycloak.FindUsersByEmailAsync(email, ct)).Count > 0
                || await _context.Users.IgnoreQueryFilters().AsNoTracking().AnyAsync(u => u.Email.ToLower() == email, ct))
            {
                return Fail(ParentAccountOutcomeKind.Conflict, ParentAccountErrorCodes.EmailAlreadyRegistered);
            }

            var (first, last) = ParentContactRules.SplitName(fullName);
            keycloakId = await _keycloak.CreateParentUserAsync(email, first, last, CancellationToken.None);
        }
        catch (KeycloakException ex) when (ex.Kind == KeycloakFailureKind.Conflict)
        {
            _logger.LogInformation("[ParentAccounts] Veli hesabı oluşturmada 409 (e-posta/kullanıcı adı kayıtlı).");
            return Fail(ParentAccountOutcomeKind.Conflict, ParentAccountErrorCodes.EmailAlreadyRegistered);
        }
        catch (KeycloakException ex) when (ex.Kind == KeycloakFailureKind.Validation)
        {
            _logger.LogWarning(ex, "[ParentAccounts] Keycloak veli kullanıcısını doğrulama hatasıyla reddetti (400).");
            return Fail(ParentAccountOutcomeKind.Invalid, ParentAccountErrorCodes.InvalidEmail);
        }
        catch (Exception ex) when (IsUpstream(ex, ct))
        {
            _logger.LogError(ex, "[ParentAccounts] Veli hesabı oluşturulamadı.");
            return Fail(ParentAccountOutcomeKind.UpstreamFailure, ParentAccountErrorCodes.Unavailable);
        }

        // Geri dönüşsüz nokta: Keycloak kullanıcısı var — her hatada silinir (bu çağrıda açıldı, başka hiçbir şey kullanmıyor).
        var stage = "password";
        try
        {
            await _keycloak.ResetPasswordAsync(keycloakId, request.Password, CancellationToken.None);
            stage = "role";
            await _keycloak.SetRoleAsync(keycloakId, ParentContactRules.ParentRole);
            stage = "identity";
            var userId = await InsertIdentityAsync(keycloakId, email, fullName);
            _logger.LogInformation("[ParentAccounts] Veli hesabı aktivasyonla açıldı: userId={UserId}", userId);
            return new ParentAccountResult(ParentAccountOutcomeKind.Success,
                Created: new CreateParentAccountResponse { UserId = userId, KeycloakId = keycloakId });
        }
        catch (KeycloakException ex) when (stage == "password" && ex.Kind == KeycloakFailureKind.Validation)
        {
            _logger.LogWarning("[ParentAccounts] Keycloak parola politikası veli parolasını reddetti; hesap geri alınıyor.");
            await TryDeleteAsync(keycloakId);
            return Fail(ParentAccountOutcomeKind.Invalid, ParentAccountErrorCodes.PasswordRejected);
        }
        catch (Exception ex)
        {
            _context.ChangeTracker.Clear();
            _logger.LogError(ex, "[ParentAccounts] Veli hesabı tamamlanamadı (aşama={Stage}); Keycloak kullanıcısı geri alınıyor.", stage);
            await TryDeleteAsync(keycloakId);
            return Fail(ParentAccountOutcomeKind.UpstreamFailure, ParentAccountErrorCodes.Unavailable);
        }
    }

    public async Task<ParentAccountResult> ConfirmEmailAsync(ConfirmParentEmailRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var email = ParentContactRules.NormalizeEmail(request.Email);
        if (email == null)
            return Fail(ParentAccountOutcomeKind.Invalid, ParentAccountErrorCodes.InvalidEmail);

        var identity = await _context.Users.AsNoTracking()
            .Where(u => u.Id == request.UserId)
            .Select(u => new { u.KeycloakId, u.Role })
            .FirstOrDefaultAsync(ct);
        if (identity == null || !string.Equals(identity.Role, ParentContactRules.ParentRole, StringComparison.OrdinalIgnoreCase))
            return Fail(ParentAccountOutcomeKind.Conflict, ParentAccountErrorCodes.NotParentAccount);

        try
        {
            var roles = await _keycloak.GetUserRealmRoleNamesAsync(identity.KeycloakId, ct);
            if (!roles.Contains(ParentContactRules.ParentRole, StringComparer.OrdinalIgnoreCase)
                || roles.Any(r => NonParentRoles.Contains(r, StringComparer.OrdinalIgnoreCase)))
                return Fail(ParentAccountOutcomeKind.Conflict, ParentAccountErrorCodes.NotParentAccount);

            // E-posta JWT claim'inden değil, Keycloak'taki hesaptan okunur.
            var (accountEmail, verified) = await _keycloak.GetUserEmailAsync(identity.KeycloakId, ct);
            if (!string.Equals(ParentContactRules.NormalizeEmail(accountEmail), email, StringComparison.Ordinal))
            {
                _logger.LogWarning("[ParentAccounts] Davet kabulü: hesabın e-postası davet e-postası değil (userId={UserId}).", request.UserId);
                return Fail(ParentAccountOutcomeKind.Conflict, ParentAccountErrorCodes.EmailMismatch);
            }

            if (!verified)
                await _keycloak.MarkEmailVerifiedAsync(identity.KeycloakId, email, CancellationToken.None);
        }
        catch (KeycloakException ex) when (ex.Kind == KeycloakFailureKind.Conflict)
        {
            _logger.LogWarning("[ParentAccounts] Davet kabulü: hesabın e-postası doğrulama sırasında değişti (userId={UserId}).", request.UserId);
            return Fail(ParentAccountOutcomeKind.Conflict, ParentAccountErrorCodes.EmailMismatch);
        }
        catch (KeycloakException ex) when (ex.StatusCode == StatusCodes.Status404NotFound)
        {
            return Fail(ParentAccountOutcomeKind.Conflict, ParentAccountErrorCodes.NotParentAccount);
        }
        catch (Exception ex) when (IsUpstream(ex, ct))
        {
            _logger.LogError(ex, "[ParentAccounts] Davet kabulü: hesap doğrulanamadı (userId={UserId}).", request.UserId);
            return Fail(ParentAccountOutcomeKind.UpstreamFailure, ParentAccountErrorCodes.Unavailable);
        }

        _logger.LogInformation("[ParentAccounts] Davet kabulüyle veli e-postası doğrulandı: userId={UserId}", request.UserId);
        return ParentAccountResult.Ok;
    }

    /// <summary>Identity satırı + dil event'i (register / çocuk hesabı ile aynı: BadgeService kullanıcının dilini event'le öğrenir).</summary>
    private async Task<int> InsertIdentityAsync(string keycloakId, string email, string fullName)
    {
        var user = new User
        {
            KeycloakId = keycloakId,
            FullName = fullName,
            Email = email,
            Role = ParentContactRules.ParentRole,
            RoleUpdatedAtUtc = DateTime.UtcNow
        };

        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            _context.ChangeTracker.Clear();
            user.Id = 0;
            await using var tx = await _context.Database.BeginTransactionAsync(CancellationToken.None);
            _context.Users.Add(user);
            await _context.SaveChangesAsync(CancellationToken.None);
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
        _context.ChangeTracker.Clear();
        return user.Id;
    }

    private async Task TryDeleteAsync(string keycloakId)
    {
        try
        {
            await _keycloak.TryDeleteUserAsync(keycloakId, CancellationToken.None);
        }
        catch (Exception cleanupEx)
        {
            _logger.LogError(cleanupEx, "[ParentAccounts] Yarım kalan veli hesabı Keycloak'tan silinemedi ({KeycloakId})", keycloakId);
        }
    }

    private static bool IsUpstream(Exception ex, CancellationToken ct)
        => ex is KeycloakException or HttpRequestException or Polly.ExecutionRejectedException
           || (ex is OperationCanceledException && !ct.IsCancellationRequested);
}
