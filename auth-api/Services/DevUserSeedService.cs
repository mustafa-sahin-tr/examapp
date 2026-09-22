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
/// hesabı bu uçtan geçemez. Keycloak'ta zaten var olan bir kullanıcı için üç durum:
/// identity'de <c>IsSeedData=true</c> satırı var → <c>Existing</c> (rol/<c>school_id</c> onarımı);
/// identity'de HİÇ satır yok → <c>Adopted</c> (yetim: önceki koşu Keycloak'tan sonra kesilmiş; e-posta bu isteğin
/// deterministik seed e-postası olduğu için sahiplenilir — roller/<c>school_id</c> onarılır, identity satırı açılır);
/// identity'de <c>IsSeedData=false</c> satırı var → <c>SkippedForeign</c> (elle açılmış; hiçbir sisteme yazılmaz).
/// Parola yalnızca <see cref="DevSeedUsersRequest.ResetPassword"/> ile sıfırlanır. Soft-delete edilmiş seed
/// kullanıcısı geri açılır, ikinci satır oluşmaz.</para>
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
        // Harf duyarsız: identity'de "Seed.T.X@…" gibi elle açılmış bir satır da yabancı kararını tetiklemeli.
        // İstek e-postaları Validate'te zaten küçük harf (IsSeedEmail); DB tarafı LOWER() ile karşılaştırılır.
        var emails = results.Select(r => r.Email.ToLowerInvariant()).ToList();
        var existingUsers = await _context.Users
            .IgnoreQueryFilters()
            .Where(u => emails.Contains(u.Email.ToLower()))
            .ToListAsync(ct);
        var existingByEmail = existingUsers
            .GroupBy(u => u.Email, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(u => u.IsSeedData).ThenBy(u => u.IsDeleted).First(), StringComparer.OrdinalIgnoreCase);

        IdentityState StateOf(string email) => existingByEmail.TryGetValue(email, out var u)
            ? (u.IsSeedData ? IdentityState.Seed : IdentityState.Foreign)
            : IdentityState.None;

        // ---- 1) Keycloak ----
        var sw = Stopwatch.StartNew();
        if (mode == DevSeedUsersRequest.ModePartialImport)
            await SeedKeycloakPartialImportAsync(request, role, results, StateOf, ct);
        else
            await SeedKeycloakAdminApiAsync(request, role, results, StateOf, ct);
        response.KeycloakElapsedMs = sw.ElapsedMilliseconds;

        // ---- 2) Identity DB ----
        sw.Restart();
        await UpsertIdentityUsersAsync(request, role, results, existingByEmail, ct);
        response.IdentityDbElapsedMs = sw.ElapsedMilliseconds;

        _logger.LogInformation(
            "dev seed-users: mode={Mode} istek={Count} kcCreated={KcCreated} kcExisting={KcExisting} kcAdopted={KcAdopted} kcFailed={KcFailed} kcForeign={KcForeign} " +
            "pwReset={PwReset} idCreated={IdCreated} idExisting={IdExisting} kcMs={KcMs} dbMs={DbMs}",
            mode, results.Count,
            results.Count(r => r.KeycloakStatus == DevSeedUsersResponse.StatusCreated),
            results.Count(r => r.KeycloakStatus == DevSeedUsersResponse.StatusExisting),
            results.Count(r => r.KeycloakStatus == DevSeedUsersResponse.StatusAdopted),
            results.Count(r => r.KeycloakStatus == DevSeedUsersResponse.StatusFailed),
            results.Count(r => r.KeycloakStatus == DevSeedUsersResponse.StatusSkippedForeign),
            results.Count(r => r.PasswordReset),
            results.Count(r => r.IdentityStatus == DevSeedUsersResponse.StatusCreated),
            results.Count(r => r.IdentityStatus == DevSeedUsersResponse.StatusExisting),
            response.KeycloakElapsedMs, response.IdentityDbElapsedMs);

        return response;
    }

    // ------------------------------------------------------------------------------------------
    // Temizleme (issue #218)
    // ------------------------------------------------------------------------------------------

    /// <summary>
    /// Kapsam: Keycloak'ta kullanıcı adı seed desenine uyan (<see cref="SeedDataConventions.IsSeedEmail"/>) VE
    /// identity'de <c>IsSeedData=true</c> satırı olan hesaplar. Identity'de aynı e-postayla seed olmayan bir satır
    /// varsa hesap yabancı sayılır ve iki sistemde de dokunulmaz. Identity'de hiç satır yoksa (yetim) varsayılan
    /// yine dokunulmaz; <see cref="DevSeedCleanupRequest.IncludeOrphans"/> ile yalnızca Keycloak'tan silinir.
    /// Sıra: Keycloak → identity; identity satırı yalnızca Keycloak silme başarılı ya da kullanıcı zaten yoksa
    /// silinir. Böylece kısmi hata sonrası ikinci koşu kalanı bulur (identity satırı = yeniden deneme listesi).
    /// </summary>
    public async Task<DevSeedCleanupResponse> CleanupAsync(DevSeedCleanupRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!IsAllowedEnvironment(_environment))
        {
            throw new DevSeedEnvironmentException(
                $"Seed temizliği yalnızca Development/Staging ortamında çalışır; mevcut ortam: '{_environment.EnvironmentName}'.");
        }

        var excluded = new HashSet<int>(request.ExcludeUserIds ?? new List<int>());
        var response = new DevSeedCleanupResponse { DryRun = request.DryRun };
        var entries = new Dictionary<string, DevSeedCleanupUser>(StringComparer.OrdinalIgnoreCase);

        DevSeedCleanupUser Entry(string email)
        {
            if (!entries.TryGetValue(email, out var e))
                entries[email] = e = new DevSeedCleanupUser { Email = email };
            return e;
        }

        // ---- 0) Identity: seed alanındaki TÜM satırlar (soft-delete dahil) — yabancı kararı için IsSeedData=false olanlar da ----
        var domainSuffix = "@" + SeedDataConventions.EmailDomain;
        // Harf duyarsız (LOWER): "@Seed.Examapp.Local" ile elle açılmış satır da yüklenmeli ki yabancı kararı verilsin.
        var identityRows = await _context.Users
            .IgnoreQueryFilters()
            .Where(u => u.Email.ToLower().EndsWith(domainSuffix))
            .Select(u => new { u.Id, u.Email, u.KeycloakId, u.IsSeedData, u.IsDeleted })
            .ToListAsync(ct);

        // Aynı e-postada birden fazla satır olabilir (soft-delete kalıntısı). Tek bir seed olmayan satır bile hesabı
        // yabancı yapar; aksi halde tüm satırlar bir entry'de toplanır ve birlikte silinir ya da (biri excluded ise) korunur.
        var foreignEmails = identityRows.Where(r => !r.IsSeedData).Select(r => r.Email).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var email in foreignEmails)
        {
            var e = Entry(email);
            e.KeycloakStatus = DevSeedCleanupResponse.StatusSkippedForeign;
            e.IdentityStatus = DevSeedCleanupResponse.StatusSkippedForeign;
            e.Error = "identity'de aynı e-postalı seed olmayan kayıt var — dokunulmadı.";
            response.KeycloakSkippedForeign++;
        }

        var seedGroups = identityRows
            .Where(r => r.IsSeedData && SeedDataConventions.IsSeedEmail(r.Email) && !foreignEmails.Contains(r.Email))
            .GroupBy(r => r.Email, StringComparer.OrdinalIgnoreCase);
        foreach (var g in seedGroups)
        {
            var rows = g.OrderBy(r => r.IsDeleted).ThenBy(r => r.Id).ToList(); // aktif satır önce
            var e = Entry(g.Key);
            e.UserId = rows[0].Id;
            e.KeycloakId = rows.Select(r => r.KeycloakId).FirstOrDefault(k => !string.IsNullOrEmpty(k));
            e.IdentityIds = rows.Select(r => r.Id).ToList();
            e.IdentityStatus = rows.Any(r => excluded.Contains(r.Id))
                ? DevSeedCleanupResponse.StatusExcluded
                : DevSeedCleanupResponse.StatusPlanned;
        }

        // ---- 1) Keycloak: seed alanı araması (e-posta + kullanıcı adı; username≠email vakası için) ----
        var sw = Stopwatch.StartNew();
        var keycloakUsers = new List<KeycloakUserSummary>();
        var searchFailed = false;
        try
        {
            keycloakUsers.AddRange(await _keycloak.SearchUsersAsync(domainSuffix, KeycloakUserSearchField.Email, ct));
            keycloakUsers.AddRange(await _keycloak.SearchUsersAsync(domainSuffix, KeycloakUserSearchField.Username, ct));
        }
        catch (KeycloakException ex)
        {
            searchFailed = true;
            _logger.LogWarning(ex, "dev seed-cleanup: Keycloak araması başarısız");
            foreach (var e in entries.Values.Where(e => e.IdentityStatus == DevSeedCleanupResponse.StatusPlanned))
            {
                e.KeycloakStatus = DevSeedCleanupResponse.StatusFailed;
                e.IdentityStatus = DevSeedCleanupResponse.StatusFailed;
                e.Error = $"Keycloak araması başarısız: {ex.Message}";
                response.KeycloakFailed++;
                response.IdentityFailed++;
            }
        }

        var keycloakByUsername = keycloakUsers
            .Where(u => SeedDataConventions.IsSeedEmail(u.Username))
            .GroupBy(u => u.Username, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);

        foreach (var kc in keycloakByUsername.Values)
        {
            var e = Entry(kc.Username);
            if (e.KeycloakStatus == DevSeedCleanupResponse.StatusSkippedForeign) continue;
            e.KeycloakId = kc.Id;

            if (e.UserId is null)
            {
                // Keycloak'ta var, identity'de HİÇ satır yok (yetim: kesilmiş seed koşusu). Varsayılan: dokunulmaz.
                // IncludeOrphans ile Keycloak'tan silinir — kapsam yine seed deseni (username) ile sınırlı; identity'de
                // seed olmayan satırı olanlar foreignEmails üzerinden yukarıda zaten SkippedForeign oldu.
                var marked = string.Equals(kc.Attribute(SeedDataConventions.KeycloakOriginAttribute), SeedDataConventions.KeycloakOriginValue, StringComparison.Ordinal);
                if (request.IncludeOrphans && marked)
                {
                    e.Orphan = true;
                    e.KeycloakStatus = DevSeedCleanupResponse.StatusPlanned;
                    e.IdentityStatus = DevSeedCleanupResponse.StatusMissing;
                    response.KeycloakOrphans++;
                    continue;
                }

                e.KeycloakStatus = DevSeedCleanupResponse.StatusSkippedForeign;
                e.IdentityStatus = DevSeedCleanupResponse.StatusSkippedForeign;
                e.Error = marked
                    ? "Keycloak'ta var ama identity'de satırı yok — yetim hesap, dokunulmadı (--include-orphans ile silinir)."
                    : "Keycloak'ta var, identity'de yok ve seed_origin işareti taşımıyor — seed aracı açmamış olabilir, --include-orphans ile de silinmez.";
                response.KeycloakSkippedForeign++;
                continue;
            }

            if (e.IdentityStatus == DevSeedCleanupResponse.StatusExcluded)
                e.KeycloakStatus = DevSeedCleanupResponse.StatusExcluded;
            else if (e.IdentityStatus == DevSeedCleanupResponse.StatusPlanned)
                e.KeycloakStatus = DevSeedCleanupResponse.StatusPlanned;
        }

        // Aramada çıkmayan identity kayıtları: KeycloakId biliniyorsa id ile silme denenir (self-heal; 404 → Missing),
        // bilinmiyorsa Missing. Excluded olanlar Keycloak'ta da korunur.
        foreach (var e in entries.Values.Where(e => string.IsNullOrEmpty(e.KeycloakStatus)))
        {
            if (e.IdentityStatus == DevSeedCleanupResponse.StatusExcluded)
                e.KeycloakStatus = DevSeedCleanupResponse.StatusExcluded;
            else if (e.IdentityStatus == DevSeedCleanupResponse.StatusPlanned && !searchFailed)
            {
                if (!string.IsNullOrEmpty(e.KeycloakId))
                    e.KeycloakStatus = DevSeedCleanupResponse.StatusPlanned; // aramada yok ama id var → id ile denenecek
                else
                {
                    e.KeycloakStatus = DevSeedCleanupResponse.StatusMissing;
                    response.KeycloakMissing++;
                }
            }
        }

        response.KeycloakExcluded = entries.Values.Count(e => e.KeycloakStatus == DevSeedCleanupResponse.StatusExcluded);
        response.IdentityExcluded = entries.Values.Count(e => e.IdentityStatus == DevSeedCleanupResponse.StatusExcluded);

        if (!request.DryRun)
        {
            // ---- 2) Keycloak silme (kullanıcı başına; bir hata diğerlerini durdurmaz) ----
            foreach (var e in entries.Values.Where(e => e.KeycloakStatus == DevSeedCleanupResponse.StatusPlanned))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var deleted = await _keycloak.TryDeleteUserAsync(e.KeycloakId!, ct);
                    e.KeycloakStatus = deleted ? DevSeedCleanupResponse.StatusDeleted : DevSeedCleanupResponse.StatusMissing;
                    if (deleted) response.KeycloakDeleted++; else response.KeycloakMissing++;
                }
                catch (KeycloakException ex)
                {
                    e.KeycloakStatus = DevSeedCleanupResponse.StatusFailed;
                    e.Error = ex.Message;
                    response.KeycloakFailed++;
                    if (!e.Orphan)
                    {
                        e.IdentityStatus = DevSeedCleanupResponse.StatusFailed;
                        response.IdentityFailed++;
                    }
                    _logger.LogWarning(ex, "dev seed-cleanup: Keycloak silme hatası ({Email})", e.Email);
                }
            }
        }
        response.KeycloakElapsedMs = sw.ElapsedMilliseconds;

        // ---- 3) Identity hard delete (yalnızca Keycloak tarafı Deleted/Missing olanlar; entry'nin TÜM satırları) ----
        sw.Restart();
        var identityIds = entries.Values
            .Where(e => e.IdentityStatus == DevSeedCleanupResponse.StatusPlanned
                        && e.KeycloakStatus is DevSeedCleanupResponse.StatusDeleted or DevSeedCleanupResponse.StatusMissing or DevSeedCleanupResponse.StatusPlanned)
            .SelectMany(e => e.IdentityIds)
            .ToList();

        if (!request.DryRun && identityIds.Count > 0)
        {
            // ExecuteDelete: SaveChanges/soft-delete interceptor'ından geçmez → gerçek DELETE. Seed satırı
            // benzersiz e-posta/KeycloakId için yeniden koşuda yer açar. Son sigorta: IsSeedData filtresi SQL'de de var.
            var strategy = _context.Database.CreateExecutionStrategy();
            var deleted = 0;
            await strategy.ExecuteAsync(async () =>
            {
                deleted = 0;
                foreach (var chunk in identityIds.Chunk(1000))
                {
                    deleted += await _context.Users
                        .IgnoreQueryFilters()
                        .Where(u => u.IsSeedData && u.Email.ToLower().EndsWith(domainSuffix) && chunk.Contains(u.Id))
                        .ExecuteDeleteAsync(ct);
                }
            });
            response.IdentityDeleted = deleted;
            var deletedIds = identityIds.ToHashSet();
            foreach (var e in entries.Values.Where(e => e.IdentityIds.Any(deletedIds.Contains)))
                e.IdentityStatus = DevSeedCleanupResponse.StatusDeleted;
        }
        response.IdentityDbElapsedMs = sw.ElapsedMilliseconds;

        response.Users = entries.Values.OrderBy(e => e.Email, StringComparer.Ordinal).ToList();

        _logger.LogInformation(
            "dev seed-cleanup: dryRun={DryRun} kcDeleted={KcDeleted} kcMissing={KcMissing} kcExcluded={KcExcluded} kcForeign={KcForeign} kcOrphans={KcOrphans} kcFailed={KcFailed} " +
            "idDeleted={IdDeleted} idExcluded={IdExcluded} idFailed={IdFailed} kcMs={KcMs} dbMs={DbMs}",
            response.DryRun, response.KeycloakDeleted, response.KeycloakMissing, response.KeycloakExcluded, response.KeycloakSkippedForeign, response.KeycloakOrphans, response.KeycloakFailed,
            response.IdentityDeleted, response.IdentityExcluded, response.IdentityFailed, response.KeycloakElapsedMs, response.IdentityDbElapsedMs);

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

    /// <summary>Seed hesabının Keycloak'ta taşıması gereken attribute'lar: her zaman sahiplik kilidi, okul varsa school_id.</summary>
    private static Dictionary<string, string> DesiredAttributes(DevSeedUserItem item)
    {
        var attributes = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [SeedDataConventions.KeycloakOriginAttribute] = SeedDataConventions.KeycloakOriginValue
        };
        if (item.SchoolId is { } schoolId)
            attributes[SchoolIdAttribute] = schoolId.ToString(CultureInfo.InvariantCulture);
        return attributes;
    }

    private static KeycloakSeedUser ToSeedUser(DevSeedUserItem item)
    {
        var email = item.Email.Trim();
        return new KeycloakSeedUser(email, email, item.FirstName.Trim(), item.LastName.Trim(), DesiredAttributes(item));
    }

    private static bool HasSeedOrigin(IReadOnlyDictionary<string, string> attributes)
        => attributes.TryGetValue(SeedDataConventions.KeycloakOriginAttribute, out var v)
           && string.Equals(v, SeedDataConventions.KeycloakOriginValue, StringComparison.Ordinal);

    /// <summary>Identity'deki durum: satır yok / seed satırı / seed olmayan satır (yabancı).</summary>
    private enum IdentityState { None, Seed, Foreign }

    private const string ForeignError = "identity'de aynı e-postalı seed olmayan kayıt var — yabancı hesap, dokunulmadı.";
    private const string UnmarkedOrphanError =
        "Keycloak'ta var, identity'de yok ve seed_origin işareti taşımıyor (seed aracı açmamış olabilir) — dokunulmadı; " +
        "yalnızca incident temizliği için --adopt-unmarked.";

    private static void MarkForeign(DevSeedUserResult result, string error)
    {
        result.KeycloakId = null;
        result.KeycloakStatus = DevSeedUsersResponse.StatusSkippedForeign;
        result.Error = error;
    }

    /// <summary>
    /// Keycloak'ta zaten var olan kullanıcı için onarım: eksik roller, attribute'lar (<c>seed_origin</c> kilidi + varsa
    /// <c>school_id</c>), isteğe bağlı parola sıfırlama. Identity durumu <see cref="IdentityState.Seed"/> → <c>Existing</c>;
    /// <see cref="IdentityState.None"/> → yalnızca Keycloak hesabı <c>seed_origin</c> işaretini taşıyorsa (ya da tek seferlik
    /// <see cref="DevSeedUsersRequest.AdoptUnmarked"/> ile) <c>Adopted</c>, aksi halde <c>SkippedForeign</c> — seed desenli
    /// e-postayla Keycloak'ta başka yoldan açılmış bir hesap sahiplenilmez. <see cref="IdentityState.Foreign"/> → hiçbir
    /// Keycloak çağrısı yapılmaz, <c>SkippedForeign</c>. false = dokunulmadı.
    /// </summary>
    private async Task<bool> TryRepairExistingAsync(
        string keycloakId, DevSeedUserItem item, DevSeedUserResult result, string role, string defaultRole,
        IdentityState state, RoleCache roles, DevSeedUsersRequest request, CancellationToken ct)
    {
        if (state == IdentityState.Foreign)
        {
            MarkForeign(result, ForeignError);
            return false;
        }

        if (state == IdentityState.None && !request.AdoptUnmarked)
        {
            // Sahiplik kilidi: yetim yalnızca seed aracının açtığı (işaretli) hesapsa sahiplenilir.
            var attributes = await _keycloak.GetUserAttributesAsync(keycloakId, ct);
            if (!HasSeedOrigin(attributes))
            {
                MarkForeign(result, UnmarkedOrphanError);
                return false;
            }
        }

        var current = await _keycloak.GetUserRealmRoleNamesAsync(keycloakId, ct);
        if (!current.Contains(role, StringComparer.OrdinalIgnoreCase))
            await _keycloak.AddRealmRoleMappingAsync(keycloakId, await roles.GetAsync(role, ct), ct);
        if (!current.Contains(defaultRole, StringComparer.OrdinalIgnoreCase))
            await _keycloak.AddRealmRoleMappingAsync(keycloakId, await roles.GetAsync(defaultRole, ct), ct);

        // Existing hesaplarda da işaret eksikse tamamlanır (işaret eklenmeden önce açılmış yerel hesaplar böylece işaretlenir).
        await _keycloak.EnsureUserAttributesAsync(keycloakId, DesiredAttributes(item), ct);

        if (request.ResetPassword)
        {
            await _keycloak.ResetPasswordAsync(keycloakId, request.Password, ct);
            result.PasswordReset = true;
            _logger.LogWarning("dev seed-users: parola sıfırlandı — {Email} (keycloak {KeycloakId})", result.Email, keycloakId);
        }

        result.KeycloakId = keycloakId;
        if (state == IdentityState.Seed)
        {
            result.KeycloakStatus = DevSeedUsersResponse.StatusExisting;
        }
        else
        {
            result.KeycloakStatus = DevSeedUsersResponse.StatusAdopted;
            _logger.LogWarning("dev seed-users: yetim Keycloak hesabı sahiplenildi — {Email} (keycloak {KeycloakId}, adoptUnmarked={AdoptUnmarked})",
                result.Email, keycloakId, request.AdoptUnmarked);
        }
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
        DevSeedUsersRequest request, string role, List<DevSeedUserResult> results, Func<string, IdentityState> stateOf, CancellationToken ct)
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
                    // Kısmi durum: önceki koşu kullanıcıyı açıp rolü atayamadan / identity'yi yazamadan kesilmiş olabilir.
                    await TryRepairExistingAsync(created.Id, item, result, role, defaultRole, stateOf(result.Email), roles, request, ct);
                }
                else
                {
                    await _keycloak.AddRealmRoleMappingAsync(created.Id, roleRep, ct);
                    result.KeycloakStatus = DevSeedUsersResponse.StatusCreated;
                }
            }
            catch (KeycloakException ex)
            {
                // Onarım adımında patlamış olabilir (id zaten atanmıştı): identity'ye yazılmasın diye id düşürülür.
                result.KeycloakId = null;
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
        DevSeedUsersRequest request, string role, List<DevSeedUserResult> results, Func<string, IdentityState> stateOf, CancellationToken ct)
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
                var state = stateOf(result.Email);
                if (!isAdded && state == IdentityState.Foreign)
                {
                    // Yabancı: id araması dahil hiçbir Keycloak çağrısı yapılmaz.
                    MarkForeign(result, ForeignError);
                    continue;
                }

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

                await TryRepairExistingAsync(keycloakId, request.Users[i], result, role, defaultRole, state, roles, request, ct);
            }
            catch (KeycloakException ex)
            {
                result.KeycloakId = null;
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
