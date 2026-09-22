using System.Diagnostics;
using System.Globalization;
using System.Text.Json;
using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Contracts;
using ExamApp.Foundation.Persistence;
using ExamApp.Foundation.Security;
using Microsoft.EntityFrameworkCore;

namespace ExamApp.Api.Services;

/// <summary>
/// Bkz. <see cref="IDevUserSeedService"/>. Akış: ortam guard'ı → doğrulama (yalnızca seed alanı e-postaları,
/// hiçbir yazma öncesi) → identity ön yükleme (soft-delete dahil) → Keycloak (tekil admin API ya da partial
/// import) → identity <c>User</c> upsert (tek transaction) → isteğe bağlı outbox event.
///
/// <para>Güvenlik: e-posta <see cref="SeedDataConventions.IsSeedEmail"/> ile sınırlı — gerçek bir kullanıcının
/// hesabı bu uçtan geçemez. Keycloak'ta zaten var olan bir kullanıcıya rol onarımı YALNIZCA identity'de
/// <c>IsSeedData=true</c> satırı varsa yapılır; yoksa öğe <c>SkippedForeign</c> ile atlanır (identity'ye de
/// yazılmaz). Soft-delete edilmiş seed kullanıcısı geri açılır, ikinci satır oluşmaz.</para>
/// </summary>
public sealed class DevUserSeedService : IDevUserSeedService
{
    private static readonly string[] AllowedRoles = { "Student", "Teacher", "Parent" };

    private const string SchoolIdAttribute = "school_id";

    private readonly AppDbContext _context;
    private readonly IKeycloakService _keycloak;
    private readonly IHostEnvironment _environment;
    private readonly ILogger<DevUserSeedService> _logger;

    public DevUserSeedService(
        AppDbContext context,
        IKeycloakService keycloak,
        IHostEnvironment environment,
        ILogger<DevUserSeedService> logger)
    {
        _context = context;
        _keycloak = keycloak;
        _environment = environment;
        _logger = logger;
    }

    public static bool IsAllowedEnvironment(IHostEnvironment environment)
        => environment.IsDevelopment() || environment.IsStaging();

    public async Task<DevSeedUsersResponse> SeedAsync(DevSeedUsersRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsAllowedEnvironment(_environment))
        {
            throw new DevSeedEnvironmentException(
                $"Toplu kullanıcı oluşturma yalnızca Development/Staging ortamında çalışır; mevcut ortam: '{_environment.EnvironmentName}'.");
        }

        Validate(request);

        var role = AllowedRoles.First(r => r.Equals(request.Role, StringComparison.OrdinalIgnoreCase));
        var mode = request.Mode.Equals(DevSeedUsersRequest.ModePartialImport, StringComparison.OrdinalIgnoreCase)
            ? DevSeedUsersRequest.ModePartialImport
            : DevSeedUsersRequest.ModeAdminApi;

        var response = new DevSeedUsersResponse { Mode = mode };
        var results = request.Users
            .Select(u => new DevSeedUserResult { Email = u.Email.Trim() })
            .ToList();
        response.Results = results;

        // ---- 0) Identity ön yükleme (soft-delete dahil): rol onarımı ve geri açma kararı için ----
        var emails = results.Select(r => r.Email).ToList();
        var existingUsers = await _context.Users
            .IgnoreQueryFilters()
            .Where(u => emails.Contains(u.Email))
            .ToListAsync(ct);
        var existingByEmail = existingUsers
            .GroupBy(u => u.Email, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(u => u.IsSeedData).ThenBy(u => u.IsDeleted).First(), StringComparer.OrdinalIgnoreCase);

        bool IsSeedIdentity(string email) => existingByEmail.TryGetValue(email, out var u) && u.IsSeedData;

        // ---- 1) Keycloak ----
        var sw = Stopwatch.StartNew();
        if (mode == DevSeedUsersRequest.ModePartialImport)
            await SeedKeycloakPartialImportAsync(request, role, results, IsSeedIdentity, ct);
        else
            await SeedKeycloakAdminApiAsync(request, role, results, IsSeedIdentity, ct);
        response.KeycloakElapsedMs = sw.ElapsedMilliseconds;

        // ---- 2) Identity DB ----
        sw.Restart();
        await UpsertIdentityUsersAsync(request, role, results, existingByEmail, ct);
        response.IdentityDbElapsedMs = sw.ElapsedMilliseconds;

        _logger.LogInformation(
            "dev seed-users: mode={Mode} istek={Count} kcCreated={KcCreated} kcExisting={KcExisting} kcFailed={KcFailed} kcForeign={KcForeign} " +
            "idCreated={IdCreated} idExisting={IdExisting} kcMs={KcMs} dbMs={DbMs}",
            mode, results.Count,
            results.Count(r => r.KeycloakStatus == DevSeedUsersResponse.StatusCreated),
            results.Count(r => r.KeycloakStatus == DevSeedUsersResponse.StatusExisting),
            results.Count(r => r.KeycloakStatus == DevSeedUsersResponse.StatusFailed),
            results.Count(r => r.KeycloakStatus == DevSeedUsersResponse.StatusSkippedForeign),
            results.Count(r => r.IdentityStatus == DevSeedUsersResponse.StatusCreated),
            results.Count(r => r.IdentityStatus == DevSeedUsersResponse.StatusExisting),
            response.KeycloakElapsedMs, response.IdentityDbElapsedMs);

        return response;
    }

    /// <summary>Hiçbir dış sisteme dokunmadan önce tüm istek doğrulanır; tek geçersiz öğe tüm isteği reddeder (400).</summary>
    private static void Validate(DevSeedUsersRequest request)
    {
        if (request.Users is null || request.Users.Count == 0)
            throw new ArgumentException("En az bir kullanıcı verilmeli.", nameof(request));
        if (request.Users.Count > DevSeedUsersRequest.MaxUsersPerRequest)
            throw new ArgumentException($"İstek başına en fazla {DevSeedUsersRequest.MaxUsersPerRequest} kullanıcı.", nameof(request));
        if (string.IsNullOrWhiteSpace(request.Password) || request.Password.Length < 6)
            throw new ArgumentException("Parola en az 6 karakter olmalı.", nameof(request));
        if (!AllowedRoles.Contains(request.Role, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Rol Student/Teacher/Parent olmalı.", nameof(request));
        if (!request.Mode.Equals(DevSeedUsersRequest.ModeAdminApi, StringComparison.OrdinalIgnoreCase) &&
            !request.Mode.Equals(DevSeedUsersRequest.ModePartialImport, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException($"Mode '{DevSeedUsersRequest.ModeAdminApi}' ya da '{DevSeedUsersRequest.ModePartialImport}' olmalı.", nameof(request));

        var duplicate = request.Users
            .Select(u => u.Email?.Trim() ?? string.Empty)
            .GroupBy(e => e, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
            throw new ArgumentException($"Aynı e-posta birden fazla kez verilmiş: '{duplicate.Key}'.", nameof(request));

        foreach (var u in request.Users)
        {
            var email = u.Email?.Trim();
            if (!SeedDataConventions.IsSeedEmail(email))
                throw new ArgumentException(
                    $"Yalnızca seed alanı e-postaları kabul edilir (seed.*@{SeedDataConventions.EmailDomain}, küçük harf): '{u.Email}'.", nameof(request));
            if (email!.Length > 100)
                throw new ArgumentException($"E-posta 100 karakteri aşıyor: '{email}'.", nameof(request));
            if (string.IsNullOrWhiteSpace(u.FirstName) || string.IsNullOrWhiteSpace(u.LastName))
                throw new ArgumentException($"Ad/soyad boş olamaz: '{email}'.", nameof(request));
            if (u.FirstName.Trim().Length > 50 || u.LastName.Trim().Length > 50)
                throw new ArgumentException($"Ad/soyad 50 karakteri aşıyor: '{email}'.", nameof(request));
            if (u.SchoolId is <= 0)
                throw new ArgumentException($"SchoolId pozitif olmalı: '{email}'.", nameof(request));
        }
    }

    private static KeycloakSeedUser ToSeedUser(DevSeedUserItem item)
    {
        var email = item.Email.Trim();
        var attributes = item.SchoolId is { } schoolId
            ? new Dictionary<string, string> { [SchoolIdAttribute] = schoolId.ToString(CultureInfo.InvariantCulture) }
            : null;
        return new KeycloakSeedUser(email, email, item.FirstName.Trim(), item.LastName.Trim(), attributes);
    }

    /// <summary>
    /// Mevcut Keycloak kullanıcısında eksik rolleri tamamlar — yalnızca identity'de seed kaydı varsa.
    /// Aksi halde false: yabancı hesap, dokunulmaz.
    /// </summary>
    private async Task<bool> TryRepairRolesAsync(
        string keycloakId, string email, string role, string defaultRole,
        Func<string, bool> isSeedIdentity, RoleCache roles, CancellationToken ct)
    {
        if (!isSeedIdentity(email))
            return false;

        var current = await _keycloak.GetUserRealmRoleNamesAsync(keycloakId, ct);
        if (!current.Contains(role, StringComparer.OrdinalIgnoreCase))
            await _keycloak.AddRealmRoleMappingAsync(keycloakId, await roles.GetAsync(role, ct), ct);
        if (!current.Contains(defaultRole, StringComparer.OrdinalIgnoreCase))
            await _keycloak.AddRealmRoleMappingAsync(keycloakId, await roles.GetAsync(defaultRole, ct), ct);
        return true;
    }

    private sealed class RoleCache
    {
        private readonly IKeycloakService _keycloak;
        private readonly Dictionary<string, KeycloakRoleDto> _cache = new(StringComparer.OrdinalIgnoreCase);
        public RoleCache(IKeycloakService keycloak) => _keycloak = keycloak;

        public async Task<KeycloakRoleDto> GetAsync(string roleName, CancellationToken ct)
        {
            if (!_cache.TryGetValue(roleName, out var rep))
                _cache[roleName] = rep = await _keycloak.GetRealmRoleAsync(roleName, ct);
            return rep;
        }
    }

    /// <summary>Kullanıcı başına: POST users (+409 → username ile bul) + POST role-mapping.</summary>
    private async Task SeedKeycloakAdminApiAsync(
        DevSeedUsersRequest request, string role, List<DevSeedUserResult> results, Func<string, bool> isSeedIdentity, CancellationToken ct)
    {
        var roles = new RoleCache(_keycloak);
        var roleRep = await roles.GetAsync(role, ct);
        // Yeni kullanıcı POST /users ile varsayılan rolü otomatik alır; mevcut kullanıcıda (örn. eski bir
        // partial import ile açılmış) eksik olabilir — onarım için adı bir kez çözülür.
        var defaultRole = await _keycloak.GetRealmDefaultRoleNameAsync(ct);

        for (var i = 0; i < request.Users.Count; i++)
        {
            var item = request.Users[i];
            var result = results[i];
            try
            {
                var created = await _keycloak.CreateSeedUserAsync(ToSeedUser(item), request.Password, ct);
                result.KeycloakId = created.Id;

                if (created.AlreadyExisted)
                {
                    // Kısmi durum: önceki koşu kullanıcıyı açıp rolü atayamadan kesilmiş olabilir.
                    var repaired = await TryRepairRolesAsync(created.Id, result.Email, role, defaultRole, isSeedIdentity, roles, ct);
                    result.KeycloakStatus = repaired ? DevSeedUsersResponse.StatusExisting : DevSeedUsersResponse.StatusSkippedForeign;
                    if (!repaired)
                    {
                        result.KeycloakId = null;
                        result.Error = "Keycloak'ta var ama identity'de seed kaydı yok — yabancı hesap, dokunulmadı.";
                    }
                }
                else
                {
                    await _keycloak.AddRealmRoleMappingAsync(created.Id, roleRep, ct);
                    result.KeycloakStatus = DevSeedUsersResponse.StatusCreated;
                }
            }
            catch (KeycloakException ex)
            {
                result.KeycloakStatus = DevSeedUsersResponse.StatusFailed;
                result.Error = ex.Message;
                _logger.LogWarning(ex, "dev seed-users: Keycloak hatası ({Email})", result.Email);
            }
        }
    }

    /// <summary>
    /// Tek istek: partialImport (SKIP), roller + önceden hash'lenmiş parola. Import'ta <c>realmRoles</c> realm
    /// varsayılan rolünü (default-roles-*: account rolleri → JWT <c>aud=account</c>) otomatik eklemez; açıkça
    /// eklenir, yoksa exam API/BadgeService token'ı reddeder. SKIPPED olanlar için id username ile aranır ve
    /// (yalnızca seed kaydı varsa) eksik roller tamamlanır.
    /// </summary>
    private async Task SeedKeycloakPartialImportAsync(
        DevSeedUsersRequest request, string role, List<DevSeedUserResult> results, Func<string, bool> isSeedIdentity, CancellationToken ct)
    {
        var credential = KeycloakPasswordHasher.HashPbkdf2Sha512(request.Password);
        var users = request.Users.Select(ToSeedUser).ToList();

        KeycloakPartialImportResult import;
        string defaultRole;
        try
        {
            defaultRole = await _keycloak.GetRealmDefaultRoleNameAsync(ct);
            import = await _keycloak.PartialImportUsersAsync(users, new[] { role, defaultRole }, credential, ct);
        }
        catch (KeycloakException ex)
        {
            foreach (var r in results)
            {
                r.KeycloakStatus = DevSeedUsersResponse.StatusFailed;
                r.Error = ex.Message;
            }
            _logger.LogWarning(ex, "dev seed-users: partial import başarısız");
            return;
        }

        var roles = new RoleCache(_keycloak);

        for (var i = 0; i < users.Count; i++)
        {
            var result = results[i];
            try
            {
                if (!import.Results.TryGetValue(users[i].Username, out var entry))
                {
                    result.KeycloakStatus = DevSeedUsersResponse.StatusFailed;
                    result.Error = "Partial import sonucu bu kullanıcıyı içermiyor.";
                    continue;
                }

                var isAdded = entry.Action.Equals("ADDED", StringComparison.OrdinalIgnoreCase);
                var keycloakId = entry.Id ?? await _keycloak.FindUserIdByUsernameAsync(users[i].Username, ct);
                if (keycloakId is null)
                {
                    result.KeycloakStatus = DevSeedUsersResponse.StatusFailed;
                    result.Error = $"Partial import '{entry.Action}' döndü ama kullanıcı id'si bulunamadı.";
                    continue;
                }

                if (isAdded)
                {
                    result.KeycloakId = keycloakId;
                    result.KeycloakStatus = DevSeedUsersResponse.StatusCreated;
                    continue;
                }

                var repaired = await TryRepairRolesAsync(keycloakId, result.Email, role, defaultRole, isSeedIdentity, roles, ct);
                if (repaired)
                {
                    result.KeycloakId = keycloakId;
                    result.KeycloakStatus = DevSeedUsersResponse.StatusExisting;
                }
                else
                {
                    result.KeycloakStatus = DevSeedUsersResponse.StatusSkippedForeign;
                    result.Error = "Keycloak'ta var ama identity'de seed kaydı yok — yabancı hesap, dokunulmadı.";
                }
            }
            catch (KeycloakException ex)
            {
                result.KeycloakStatus = DevSeedUsersResponse.StatusFailed;
                result.Error = ex.Message;
            }
        }
    }

    private async Task UpsertIdentityUsersAsync(
        DevSeedUsersRequest request, string role, List<DevSeedUserResult> results,
        Dictionary<string, User> existingByEmail, CancellationToken ct)
    {
        var toAdd = new List<(User User, DevSeedUserResult Result)>();
        var revived = new List<(User User, DevSeedUserResult Result)>();

        foreach (var r in results)
        {
            if (r.KeycloakId is null)
            {
                // Failed ya da SkippedForeign — identity'ye yazılmaz.
                r.IdentityStatus = r.KeycloakStatus == DevSeedUsersResponse.StatusSkippedForeign
                    ? DevSeedUsersResponse.StatusSkippedForeign
                    : DevSeedUsersResponse.StatusFailed;
                continue;
            }

            if (existingByEmail.TryGetValue(r.Email, out var user))
            {
                if (!user.IsSeedData)
                {
                    // Seed alanında ama seed aracı açmamış bir identity satırı: dokunulmaz.
                    r.IdentityStatus = DevSeedUsersResponse.StatusSkippedForeign;
                    r.Error = "identity'de aynı e-postalı seed olmayan kayıt var — dokunulmadı.";
                    continue;
                }

                // Kısmi durum onarımı: Keycloak yeniden kurulmuşsa KeycloakId değişmiş olabilir.
                if (!string.Equals(user.KeycloakId, r.KeycloakId, StringComparison.Ordinal))
                    user.KeycloakId = r.KeycloakId;

                // Soft-delete edilmiş seed kullanıcısı: ikinci satır açmak yerine geri aç.
                if (user.IsDeleted)
                {
                    user.IsDeleted = false;
                    user.DeleteTime = null;
                    user.DeleteUserId = null;
                    revived.Add((user, r));
                }

                r.UserId = user.Id;
                r.IdentityStatus = DevSeedUsersResponse.StatusExisting;
                continue;
            }

            var item = request.Users.First(u => u.Email.Trim().Equals(r.Email, StringComparison.OrdinalIgnoreCase));
            var newUser = new User
            {
                FullName = $"{item.FirstName.Trim()} {item.LastName.Trim()}",
                Email = r.Email,
                Role = role,
                KeycloakId = r.KeycloakId,
                IsSeedData = true
            };
            toAdd.Add((newUser, r));
        }

        // Register akışıyla aynı desen: User satırı ve outbox satırı aynı transaction'da, iki SaveChanges
        // (User.Id identity ile üretildiği için event ikinci adımda yazılır).
        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async () =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            foreach (var (user, _) in toAdd)
            {
                if (_context.Entry(user).State == EntityState.Detached)
                    _context.Users.Add(user);
            }
            await _context.SaveChangesAsync(ct);

            if (request.EmitLocaleEvents && (toAdd.Count > 0 || revived.Count > 0))
            {
                var now = DateTime.UtcNow;
                foreach (var (user, _) in toAdd.Concat(revived))
                {
                    _context.OutboxMessages.Add(new OutboxMessage
                    {
                        Type = OutboxEventRegistry.NameFor<UserPreferredLocaleChangedEvent>(),
                        Content = JsonSerializer.Serialize(new UserPreferredLocaleChangedEvent
                        {
                            UserId = user.Id,
                            KeycloakId = user.KeycloakId,
                            PreferredLocale = user.PreferredLocale,
                            ChangedAtUtc = now
                        }),
                        CreatedAt = now
                    });
                }
                await _context.SaveChangesAsync(ct);
            }

            await tx.CommitAsync(ct);
        });

        foreach (var (user, result) in toAdd)
        {
            result.UserId = user.Id;
            result.IdentityStatus = DevSeedUsersResponse.StatusCreated;
        }
    }
}
