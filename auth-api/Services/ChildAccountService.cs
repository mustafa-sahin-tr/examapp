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

        if (request.RequestId == Guid.Empty)
            return Fail(ChildAccountOutcome.Invalid, ChildAccountErrorCodes.InvalidRequest);

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
        var requestId = request.RequestId;

        // ---- Kilit noktası: requestId satırı (identity DB). Aynı requestId ikinci kez başlamaz; geri alınmışsa hiç başlamaz. ---
        var claimed = await ClaimAsync(requestId, ct);
        if (claimed != null)
            return claimed;

        // Bundan sonra istemci iptali yarım iş bırakmasın: Keycloak POST dahil her adım CancellationToken.None ile; her hata
        // satırı Cancelled yapar, Keycloak kullanıcısı oluştuysa siler.
        string keycloakId;
        try
        {
            keycloakId = await _keycloak.CreateParentManagedUserAsync(username, firstName, lastName, requestId, CancellationToken.None);
        }
        catch (KeycloakException ex) when (ex.Kind == KeycloakFailureKind.Conflict)
        {
            _logger.LogInformation("[ChildAccounts] Kullanıcı adı Keycloak'ta zaten var (409).");
            await MarkFailedAsync(requestId);
            return Fail(ChildAccountOutcome.Conflict, ChildAccountErrorCodes.UsernameTaken);
        }
        catch (KeycloakException ex) when (ex.Kind == KeycloakFailureKind.Validation)
        {
            _logger.LogWarning(ex, "[ChildAccounts] Keycloak çocuk kullanıcısını doğrulama hatasıyla reddetti (400).");
            await MarkFailedAsync(requestId);
            return Fail(ChildAccountOutcome.Invalid, ChildAccountErrorCodes.InvalidUsername);
        }
        catch (Exception ex) when (IsUpstream(ex, CancellationToken.None))
        {
            // Sonuç belirsiz (ör. zaman aşımı): Keycloak kullanıcıyı açmış olabilir — attribute ile bulunup silinir.
            _logger.LogError(ex, "[ChildAccounts] Keycloak çocuk kullanıcısı oluşturulamadı.");
            await MarkFailedAsync(requestId);
            await TryDeleteByRequestAttributeAsync(requestId);
            return Fail(ChildAccountOutcome.UpstreamFailure, ChildAccountErrorCodes.Unavailable);
        }

        return await CompleteAsync(keycloakId, requestId, request.Password, firstName, lastName);
    }

    /// <summary>
    /// Geri dönüşsüz nokta: Keycloak kullanıcısı var. Her aşama <c>State == InProgress</c> koşuluna bağlı: kompanzasyon
    /// (<c>DELETE by-request</c>) araya girip satırı Cancelled yaptıysa Keycloak kullanıcısı burada silinir, identity satırı yazılmaz.
    /// </summary>
    private async Task<ChildAccountResult> CompleteAsync(string keycloakId, Guid requestId, string pw, string firstName, string lastName)
    {
        var stage = "record";
        try
        {
            // Keycloak id'yi satıra yaz — kompanzasyon bundan sonra kullanıcıyı satırdan bulur.
            if (!await RecordKeycloakUserAsync(requestId, keycloakId))
                return await AbortCancelledAsync(keycloakId, stage);

            stage = "verify";
            // İşaretler gerçekten saklandı mı? Realm unmanagedAttributePolicy kapalıysa Keycloak attribute'ları SESSİZCE düşürür:
            // parent_managed'sız hesap ne sıfırlanabilir ne geri alınabilir — kullanılmadan silinir.
            var attributes = await _keycloak.GetUserAttributesAsync(keycloakId, CancellationToken.None);
            if (!HasMarker(attributes, ChildAccountRules.KeycloakParentManagedAttribute, ChildAccountRules.KeycloakParentManagedValue) ||
                !HasMarker(attributes, ChildAccountRules.KeycloakChildRequestIdAttribute, requestId.ToString("D")))
            {
                _logger.LogError(
                    "[ChildAccounts] Keycloak çocuk hesabının parent_managed/child_request_id attribute'larını saklamadı " +
                    "(realm unmanagedAttributePolicy=ADMIN_EDIT olmalı); hesap geri alınıyor.");
                await TryDeleteKeycloakUserAsync(keycloakId);
                await MarkFailedAsync(requestId);
                return Fail(ChildAccountOutcome.UpstreamFailure, ChildAccountErrorCodes.Unavailable);
            }

            stage = "password";
            await _keycloak.ResetPasswordAsync(keycloakId, pw, CancellationToken.None);

            stage = "role";
            // Varsayılan rol kompoziti (manage-account) kaldırılır; yalnız Student + offline_access + uma_authorization +
            // account/view-profile kalır (security review #438).
            await _keycloak.ApplyParentManagedRolesAsync(keycloakId, CancellationToken.None);

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
            var completed = await strategy.ExecuteAsync(async () =>
            {
                _context.ChangeTracker.Clear();
                user.Id = 0;
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

                // Koşullu geçiş aynı transaction'da: satır kilitlenir; eşzamanlı kompanzasyonun UPDATE'i commit'i bekler ve
                // tamamlanmış hesabı (Keycloak + bu identity satırı) siler. Satır zaten Cancelled ise hiçbir şey commit edilmez.
                var moved = await _context.ChildAccountRequests
                    .Where(r => r.RequestId == requestId && r.State == ChildAccountRequestState.InProgress)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(r => r.State, ChildAccountRequestState.Completed)
                        .SetProperty(r => r.UserId, user.Id)
                        .SetProperty(r => r.UpdatedAt, DateTime.UtcNow), CancellationToken.None);
                if (moved == 0)
                {
                    await tx.RollbackAsync(CancellationToken.None);
                    return false;
                }
                await tx.CommitAsync(CancellationToken.None);
                return true;
            });
            _context.ChangeTracker.Clear();

            if (!completed)
                return await AbortCancelledAsync(keycloakId, stage);

            _logger.LogInformation("[ChildAccounts] Çocuk hesabı oluşturuldu: userId={UserId}", user.Id);
            return Created(user.Id, keycloakId);
        }
        catch (KeycloakException ex) when (stage == "password" && ex.Kind == KeycloakFailureKind.Validation)
        {
            // Realm parola politikası reddetti (gövde parolayı içermez).
            _logger.LogWarning("[ChildAccounts] Keycloak parola politikası çocuk hesabının parolasını reddetti; hesap geri alınıyor.");
            await TryDeleteKeycloakUserAsync(keycloakId);
            await MarkFailedAsync(requestId);
            return Fail(ChildAccountOutcome.Invalid, ChildAccountErrorCodes.PasswordRejected);
        }
        catch (Exception ex)
        {
            _context.ChangeTracker.Clear();
            _logger.LogError(ex, "[ChildAccounts] Çocuk hesabı tamamlanamadı (aşama={Stage}); Keycloak kullanıcısı geri alınıyor.", stage);
            await TryDeleteKeycloakUserAsync(keycloakId);
            await MarkFailedAsync(requestId);
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
            return RefuseUnmanaged(userId);

        try
        {
            var managed = await IsParentManagedAsync(user.KeycloakId, ct);
            if (managed == false)
                return RefuseUnmanaged(userId);
            if (managed == true)
                await _keycloak.TryDeleteUserAsync(user.KeycloakId, CancellationToken.None);
            // null: Keycloak kullanıcısı zaten yok — yalnız yetim identity satırı temizlenir.
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

    public async Task<ChildAccountResult> DeleteByRequestIdAsync(Guid requestId, CancellationToken ct = default)
    {
        if (requestId == Guid.Empty)
            return Fail(ChildAccountOutcome.Invalid, ChildAccountErrorCodes.InvalidRequest);

        // 1) Önce kilit: satır Cancelled (yoksa Cancelled olarak eklenir) — süren create bundan sonra tamamlanamaz, sonradan gelen
        //    create hiç başlamaz. Tamamlanmış create'in satır kilidi varsa bu UPDATE onun commit'ini bekler.
        string? recordedKeycloakId;
        try
        {
            recordedKeycloakId = await CancelRequestAsync(requestId);
        }
        catch (Exception ex) when (ex is DbUpdateException or InvalidOperationException)
        {
            _logger.LogError(ex, "[ChildAccounts] Kompanzasyon (requestId): istek satırı iptal edilemedi.");
            return Fail(ChildAccountOutcome.UpstreamFailure, ChildAccountErrorCodes.Unavailable);
        }

        // 2) Hesap(lar): satırdaki Keycloak id + attribute araması (id satıra yazılmadan önce oluşmuş kullanıcı ya da tablo öncesi).
        var managedIds = new List<string>();
        var goneIds = new List<string>();
        var deleted = 0;
        try
        {
            var ids = new HashSet<string>(StringComparer.Ordinal);
            if (!string.IsNullOrEmpty(recordedKeycloakId))
                ids.Add(recordedKeycloakId);
            foreach (var id in await _keycloak.FindUserIdsByAttributeAsync(
                         ChildAccountRules.KeycloakChildRequestIdAttribute, requestId.ToString("D"), ct))
                ids.Add(id);

            // Önce hepsini sınıflandır: işaretsiz bir hesap varsa HİÇBİRİNE dokunulmaz (yarım silme / yetim identity satırı yok).
            foreach (var id in ids)
            {
                switch (await IsParentManagedAsync(id, ct))
                {
                    case false:
                        _logger.LogWarning("[ChildAccounts] requestId ile bulunan hesap parent_managed değil; silme reddedildi.");
                        return Fail(ChildAccountOutcome.Conflict, ChildAccountErrorCodes.NotParentManaged);
                    case true:
                        managedIds.Add(id);
                        break;
                    default:
                        goneIds.Add(id); // Keycloak'ta zaten yok — identity satırı yine temizlenir
                        break;
                }
            }

            foreach (var id in managedIds)
            {
                if (await _keycloak.TryDeleteUserAsync(id, CancellationToken.None))
                    deleted++;
            }
        }
        catch (Exception ex) when (IsUpstream(ex, ct))
        {
            _logger.LogError(ex, "[ChildAccounts] Kompanzasyon (requestId): Keycloak kullanıcısı silinemedi.");
            return Fail(ChildAccountOutcome.UpstreamFailure, ChildAccountErrorCodes.Unavailable);
        }

        // 3) Tamamlanmış ilk denemenin identity satırı da (exam API yanıtı alamamış olabilir) gerçekten silinir — Keycloak'ta
        //    silinen ya da zaten olmayan her id için.
        var cleanup = managedIds.Concat(goneIds).ToList();
        var removedRows = cleanup.Count == 0
            ? 0
            : await _context.Users.IgnoreQueryFilters()
                .Where(u => cleanup.Contains(u.KeycloakId) && u.Role == ChildAccountRules.StudentRole)
                .ExecuteDeleteAsync(CancellationToken.None);

        if (deleted == 0 && removedRows == 0)
            return Fail(ChildAccountOutcome.NotFound, ChildAccountErrorCodes.NotFound);

        _logger.LogInformation("[ChildAccounts] Çocuk hesabı requestId ile geri alındı ({Count} Keycloak, {Rows} identity).", deleted, removedRows);
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

    // ---- requestId kilidi -------------------------------------------------------------------------------------------

    /// <summary>
    /// InProgress satırı ekler; null = kilit alındı, create devam eder. PK çakışması: Completed → mevcut hesap, InProgress → 409
    /// <c>RequestInProgress</c>, Cancelled → 409 <c>RequestCancelled</c>.
    /// </summary>
    private async Task<ChildAccountResult?> ClaimAsync(Guid requestId, CancellationToken ct)
    {
        _context.ChildAccountRequests.Add(new ChildAccountRequest
        {
            RequestId = requestId,
            State = ChildAccountRequestState.InProgress,
            CreatedAt = DateTime.UtcNow
        });
        try
        {
            await _context.SaveChangesAsync(ct);
            return null;
        }
        catch (DbUpdateException ex)
        {
            _context.ChangeTracker.Clear();
            var row = await _context.ChildAccountRequests.AsNoTracking()
                .FirstOrDefaultAsync(r => r.RequestId == requestId, CancellationToken.None);
            switch (row)
            {
                case { State: ChildAccountRequestState.Completed, UserId: int uid, KeycloakUserId: { Length: > 0 } kc }:
                    _logger.LogInformation("[ChildAccounts] Aynı requestId ile tekrar gelen istek: mevcut hesap döndü (userId={UserId}).", uid);
                    return Created(uid, kc);
                case { State: ChildAccountRequestState.InProgress }:
                    _logger.LogWarning("[ChildAccounts] Aynı requestId ile süren bir create var; ikinci istek reddedildi.");
                    return Fail(ChildAccountOutcome.Conflict, ChildAccountErrorCodes.RequestInProgress);
                case { State: ChildAccountRequestState.Cancelled }:
                    _logger.LogWarning("[ChildAccounts] requestId geri alınmış/başarısız; create başlatılmadı.");
                    return Fail(ChildAccountOutcome.Conflict, ChildAccountErrorCodes.RequestCancelled);
                default:
                    _logger.LogError(ex, "[ChildAccounts] requestId satırı yazılamadı.");
                    return Fail(ChildAccountOutcome.UpstreamFailure, ChildAccountErrorCodes.Unavailable);
            }
        }
        finally
        {
            _context.ChangeTracker.Clear();
        }
    }

    /// <summary>Keycloak id'yi yalnız satır hâlâ InProgress iken yazar; false = kompanzasyon araya girdi (Cancelled).</summary>
    private async Task<bool> RecordKeycloakUserAsync(Guid requestId, string keycloakId)
        => await _context.ChildAccountRequests
            .Where(r => r.RequestId == requestId && r.State == ChildAccountRequestState.InProgress)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.KeycloakUserId, keycloakId)
                .SetProperty(r => r.UpdatedAt, DateTime.UtcNow), CancellationToken.None) == 1;

    /// <summary>Başarısız create: satır (hâlâ InProgress ise) Cancelled — aynı requestId bir daha başlamaz. Best-effort.</summary>
    private async Task MarkFailedAsync(Guid requestId)
    {
        try
        {
            await _context.ChildAccountRequests
                .Where(r => r.RequestId == requestId && r.State == ChildAccountRequestState.InProgress)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.State, ChildAccountRequestState.Cancelled)
                    .SetProperty(r => r.UpdatedAt, DateTime.UtcNow), CancellationToken.None);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[ChildAccounts] requestId satırı Cancelled yapılamadı.");
        }
    }

    /// <summary>Satırı (durumu ne olursa olsun) Cancelled yapar; yoksa Cancelled olarak ekler. Kayıtlı Keycloak id'sini döner.</summary>
    private async Task<string?> CancelRequestAsync(Guid requestId)
    {
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var updated = await _context.ChildAccountRequests
                .Where(r => r.RequestId == requestId)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(r => r.State, ChildAccountRequestState.Cancelled)
                    .SetProperty(r => r.UpdatedAt, DateTime.UtcNow), CancellationToken.None);
            if (updated > 0)
            {
                return await _context.ChildAccountRequests.AsNoTracking()
                    .Where(r => r.RequestId == requestId)
                    .Select(r => r.KeycloakUserId)
                    .FirstOrDefaultAsync(CancellationToken.None);
            }

            _context.ChildAccountRequests.Add(new ChildAccountRequest
            {
                RequestId = requestId,
                State = ChildAccountRequestState.Cancelled,
                CreatedAt = DateTime.UtcNow
            });
            try
            {
                await _context.SaveChangesAsync(CancellationToken.None);
                return null; // create hiç başlamamıştı; artık başlayamaz
            }
            catch (DbUpdateException) when (attempt == 0)
            {
                // Eşzamanlı create satırı az önce ekledi — bir tur daha: UPDATE ile Cancelled.
            }
            finally
            {
                _context.ChangeTracker.Clear();
            }
        }
        throw new InvalidOperationException("Child account request row could not be cancelled.");
    }

    /// <summary>Kompanzasyon araya girdi: Keycloak kullanıcısı burada silinir, identity satırı yazılmamıştır.</summary>
    private async Task<ChildAccountResult> AbortCancelledAsync(string keycloakId, string stage)
    {
        _logger.LogWarning("[ChildAccounts] requestId create sürerken geri alındı (aşama={Stage}); Keycloak kullanıcısı siliniyor.", stage);
        await TryDeleteKeycloakUserAsync(keycloakId);
        return Fail(ChildAccountOutcome.Conflict, ChildAccountErrorCodes.RequestCancelled);
    }

    /// <summary>Sonucu belirsiz Keycloak POST'u sonrası: bu requestId attribute'lu (ve işaretli) kullanıcı varsa silinir.</summary>
    private async Task TryDeleteByRequestAttributeAsync(Guid requestId)
    {
        try
        {
            foreach (var id in await _keycloak.FindUserIdsByAttributeAsync(
                         ChildAccountRules.KeycloakChildRequestIdAttribute, requestId.ToString("D"), CancellationToken.None))
            {
                if (await IsParentManagedAsync(id, CancellationToken.None) == true)
                    await _keycloak.TryDeleteUserAsync(id, CancellationToken.None);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[ChildAccounts] Belirsiz create sonrası requestId ile temizlik yapılamadı (exam API kompanzasyonu dener).");
        }
    }

    // ---- yardımcılar ------------------------------------------------------------------------------------------------

    private static ChildAccountResult Created(int userId, string keycloakId)
        => new(ChildAccountOutcome.Success, Created: new CreateChildAccountResponse { UserId = userId, KeycloakId = keycloakId });

    private ChildAccountResult RefuseUnmanaged(int userId)
    {
        // Veli tarafından açılmamış bir hesap bu uçla ASLA silinmez.
        _logger.LogWarning("[ChildAccounts] parent_managed olmayan hesap için silme reddedildi: userId={UserId}", userId);
        return Fail(ChildAccountOutcome.Conflict, ChildAccountErrorCodes.NotParentManaged);
    }

    private static bool HasMarker(IReadOnlyDictionary<string, string> attributes, string name, string value)
        => attributes.TryGetValue(name, out var actual) && string.Equals(actual, value, StringComparison.Ordinal);

    /// <summary>true = işaretli, false = işaretsiz, null = Keycloak'ta kullanıcı yok (404).</summary>
    private async Task<bool?> IsParentManagedAsync(string keycloakId, CancellationToken ct)
    {
        try
        {
            var attributes = await _keycloak.GetUserAttributesAsync(keycloakId, ct);
            return HasMarker(attributes, ChildAccountRules.KeycloakParentManagedAttribute, ChildAccountRules.KeycloakParentManagedValue);
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
