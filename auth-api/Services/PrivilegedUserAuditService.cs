using ExamApp.Api.Data;
using ExamApp.Api.Helpers;
using ExamApp.Api.Models.Audit;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.Interfaces;
using ExamApp.Foundation.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services;

/// <summary>
/// Bkz. <see cref="IPrivilegedUserAuditService"/>. Akış (rol başına): doğrudan üyeler → role atanmış gruplar (alt gruplar
/// dahil) ve onların üyeleri → realm kompozit rolleri üzerinden dolaylı yetki kontrolü → tek sorguda identity <c>Users</c>
/// satırları (soft-delete DAHİL) → servis hesabı kanıtı (kimlik bilgisi + federated identity) → sınıflandırma → maskeleme.
///
/// <para>Tasarım false-negative'e karşı: register açığı (#240) ile açılmış bir hesap kullanıcı adını (= e-posta) serbestçe
/// seçebildiği için addan türeyen hiçbir işaret meşruiyet sayılmaz. KNOWN yalnızca Keycloak id ile; servis hesabı kanıtı
/// okunamazsa "doğrulanamadı" (fail-closed); yetkili rolde seed hesabı UNEXPLAINED. Dolaylı yetki kontrollerinden biri
/// okunamazsa exception yukarı çıkar (komut exit 3).</para>
/// </summary>
public sealed class PrivilegedUserAuditService : IPrivilegedUserAuditService
{
    public static readonly IReadOnlyList<string> DefaultRoles = ["Admin", "exam-service"];

    public const string ServiceAccountPrefix = "service-account-";

    /// <summary>Alt grup özyinelemesi için derinlik sınırı (döngü/aşırı büyüme koruması; aşılırsa hata).</summary>
    public const int MaxGroupDepth = 20;

    private readonly IKeycloakService _keycloak;
    private readonly AppDbContext _context;
    private readonly PrivilegedAuditSettings _settings;
    private readonly TimeProvider _time;

    public PrivilegedUserAuditService(
        IKeycloakService keycloak,
        AppDbContext context,
        IOptions<PrivilegedAuditSettings> settings,
        TimeProvider? time = null)
    {
        _keycloak = keycloak;
        _context = context;
        _settings = settings.Value;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Identity <c>Users</c> satırının denetimde kullanılan alt kümesi.</summary>
    public sealed record IdentityInfo(DateTime CreatedAt, bool IsDeleted, string Role, bool IsSeedData, int RowCount = 1);

    /// <summary>
    /// Servis hesabı kanıtı; null alan = okunamadı (fail-closed → doğrulanamadı).
    /// </summary>
    public sealed record ServiceAccountEvidence(bool? HasCredentials, bool? HasFederatedIdentity);

    private sealed record RoleMembership(KeycloakRoleMember Member, string Source);

    public async Task<PrivilegedAuditReport> AuditAsync(IReadOnlyList<string> roles, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(roles);

        var warnings = new List<string>();
        var perRole = new List<(string Role, List<RoleMembership> Members, bool Missing)>();
        foreach (var role in roles)
        {
            IReadOnlyList<KeycloakRoleMember> direct;
            try
            {
                direct = await _keycloak.GetUsersInRoleAsync(role, ct);
            }
            catch (KeycloakRoleNotFoundException)
            {
                // Realm'de tanımsız rol kimseye verilemez → üye yok; özet satırında işaretlenir.
                perRole.Add((role, [], true));
                continue;
            }

            var members = direct.Select(m => new RoleMembership(m, PrivilegedAccountRow.DirectSource)).ToList();
            await AddGroupMembersAsync(role, members, warnings, ct);
            perRole.Add((role, members, false));
        }

        await AddCompositeWarningsAsync(roles, warnings, ct);

        var allMembers = perRole.SelectMany(r => r.Members).Select(x => x.Member).DistinctBy(m => m.Id).ToList();
        var identity = await LoadIdentityAsync(allMembers.Select(m => m.Id).ToList(), ct);
        var evidence = await CollectServiceAccountEvidenceAsync(allMembers, ct);
        // Yalnızca id (sub), tam eşleşme: kullanıcı adı register ile serbestçe seçilebildiği için KNOWN kanıtı değildir.
        var knownIds = new HashSet<string>(
            _settings.KnownAccounts.Where(k => !string.IsNullOrWhiteSpace(k)).Select(k => k.Trim()),
            StringComparer.Ordinal);

        var rows = new List<PrivilegedAccountRow>();
        var summary = new List<PrivilegedRoleSummary>();
        foreach (var (role, members, missing) in perRole)
        {
            var roleRows = members
                .Select(x =>
                {
                    var m = x.Member;
                    identity.TryGetValue(m.Id, out var id);
                    evidence.TryGetValue(m.Id, out var ev);
                    var (cls, note) = Classify(m, id, knownIds, ev);
                    return new PrivilegedAccountRow(
                        role, m.Id, MaskUsername(m.Username, cls == PrivilegedAccountClass.ServiceAccount), MaskEmail(m.Email),
                        m.Enabled, m.CreatedAt, id is not null, id?.CreatedAt, id?.IsDeleted, id?.Role, id?.IsSeedData, cls, note,
                        x.Source);
                })
                .OrderBy(r => r.Class == PrivilegedAccountClass.Unexplained ? 0 : 1)
                .ThenBy(r => r.KeycloakCreatedAt ?? DateTimeOffset.MaxValue)
                .ToList();

            rows.AddRange(roleRows);
            summary.Add(new PrivilegedRoleSummary(
                role,
                roleRows.Count,
                roleRows.Count(r => r.Class == PrivilegedAccountClass.ServiceAccount),
                roleRows.Count(r => r.Class == PrivilegedAccountClass.Known),
                roleRows.Count(r => r.Class == PrivilegedAccountClass.Unexplained),
                missing));
        }

        return new PrivilegedAuditReport(_time.GetUtcNow(), rows, summary, warnings);
    }

    /// <summary>
    /// Role atanmış gruplar ve alt grupları (roller miras alınır): her grup bir uyarı, üyeleri denetime <c>group:/yol</c>
    /// kaynağıyla eklenir. Doğrudan üye zaten listelenmişse tekrar eklenmez.
    /// </summary>
    private async Task AddGroupMembersAsync(string role, List<RoleMembership> members, List<string> warnings, CancellationToken ct)
    {
        var groups = await _keycloak.GetGroupsInRoleAsync(role, ct);
        if (groups.Count == 0) return;

        var seenMembers = members.Select(x => x.Member.Id).ToHashSet(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<(KeycloakGroupRef Group, int Depth)>(groups.Select(g => (g, 0)));
        while (queue.Count > 0)
        {
            var (group, depth) = queue.Dequeue();
            if (!visited.Add(group.Id)) continue;
            if (depth > MaxGroupDepth)
                throw new InvalidOperationException($"'{role}' rolünün grup ağacı {MaxGroupDepth} seviyeyi aşıyor; denetim tamamlanamadı.");

            var groupMembers = await _keycloak.GetGroupMembersAsync(group.Id, ct);
            warnings.Add(depth == 0
                ? $"'{role}' rolü '{group.Path}' grubuna atanmış — {groupMembers.Count} doğrudan üye dolaylı yetkili (grup ataması kaldırılmalı ya da gerekçelendirilmeli)."
                : $"'{role}' rolü üst grup üzerinden '{group.Path}' alt grubuna miras — {groupMembers.Count} doğrudan üye dolaylı yetkili.");

            foreach (var m in groupMembers)
            {
                if (seenMembers.Add(m.Id))
                    members.Add(new RoleMembership(m, "group:" + group.Path));
            }

            foreach (var child in await _keycloak.GetSubGroupsAsync(group.Id, ct))
                queue.Enqueue((child, depth + 1));
        }
    }

    /// <summary>
    /// Denetlenen rolleri (geçişli olarak) içeren kompozit realm rolleri: bu rollerin sahipleri dolaylı yetkilidir.
    /// <c>default-roles-*</c> ise realm'deki HERKES yetkilidir. Her bulgu bir uyarı.
    /// </summary>
    private async Task AddCompositeWarningsAsync(IReadOnlyList<string> targets, List<string> warnings, CancellationToken ct)
    {
        var composites = await _keycloak.GetRealmRoleCompositesAsync(ct);
        var targetSet = targets.ToHashSet(StringComparer.Ordinal);

        foreach (var (roleName, _) in composites.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (targetSet.Contains(roleName)) continue;
            var reached = ReachableTargets(roleName, composites, targetSet);
            if (reached.Count == 0) continue;

            var everyone = roleName.StartsWith("default-roles-", StringComparison.Ordinal)
                ? " Bu realm'in varsayılan rolü: realm'deki TÜM kullanıcılar dolaylı yetkili!"
                : string.Empty;
            warnings.Add($"'{roleName}' kompozit rolü {string.Join(", ", reached.Select(r => $"'{r}'"))} içeriyor — '{roleName}' sahipleri dolaylı yetkili.{everyone}");
        }
    }

    private static List<string> ReachableTargets(
        string start, IReadOnlyDictionary<string, IReadOnlyList<string>> composites, HashSet<string> targets)
    {
        var found = new SortedSet<string>(StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal) { start };
        var stack = new Stack<string>([start]);
        while (stack.Count > 0)
        {
            if (!composites.TryGetValue(stack.Pop(), out var children)) continue;
            foreach (var child in children)
            {
                if (targets.Contains(child)) found.Add(child);
                if (visited.Add(child)) stack.Push(child);
            }
        }
        return [.. found];
    }

    /// <summary>
    /// <c>service-account-</c> önekli, clientId'siz üyeler için kimlik bilgisi + federated identity (genelde 0-2 kullanıcı).
    /// Okunamayan (404 dahil) → null → doğrulanamadı.
    /// </summary>
    private async Task<Dictionary<string, ServiceAccountEvidence>> CollectServiceAccountEvidenceAsync(
        List<KeycloakRoleMember> members, CancellationToken ct)
    {
        var result = new Dictionary<string, ServiceAccountEvidence>(StringComparer.Ordinal);
        foreach (var m in members.Where(m => HasServicePrefix(m.Username) && string.IsNullOrWhiteSpace(m.ServiceAccountClientId)))
        {
            bool? hasCredentials = null;
            bool? hasFederated = null;
            try { hasCredentials = (await _keycloak.GetUserCredentialTypesAsync(m.Id, ct)).Count > 0; }
            catch (KeycloakException) { }
            try { hasFederated = (await _keycloak.GetUserFederatedIdentityProvidersAsync(m.Id, ct)).Count > 0; }
            catch (KeycloakException) { }
            result[m.Id] = new ServiceAccountEvidence(hasCredentials, hasFederated);
        }
        return result;
    }

    private async Task<Dictionary<string, IdentityInfo>> LoadIdentityAsync(List<string> keycloakIds, CancellationToken ct)
    {
        var result = new Dictionary<string, IdentityInfo>(StringComparer.Ordinal);
        if (keycloakIds.Count == 0) return result;

        // Soft-delete dahil: silinmiş identity satırı da bir iz (kayıt zamanı, kayıtta seçilen rol) taşır.
        var rows = await _context.Users
            .IgnoreQueryFilters()
            .AsNoTracking()
            .Where(u => keycloakIds.Contains(u.KeycloakId))
            .Select(u => new { u.KeycloakId, u.CreateTime, u.IsDeleted, u.Role, u.IsSeedData })
            .ToListAsync(ct);

        foreach (var group in rows.GroupBy(r => r.KeycloakId, StringComparer.Ordinal))
        {
            // Birden fazla satır: aktif olanı, sonra en yenisini göster; sayı nota düşer.
            var pick = group.OrderBy(r => r.IsDeleted).ThenByDescending(r => r.CreateTime).First();
            result[group.Key] = new IdentityInfo(pick.CreateTime, pick.IsDeleted, pick.Role, pick.IsSeedData, group.Count());
        }
        return result;
    }

    /// <summary>
    /// Sınıf + açıklama notu. Öncelik: SERVICE_ACCOUNT → KNOWN (yalnız id) → UNEXPLAINED. Not, sınıfı değiştirmeyen ama
    /// incelemede dikkat edilmesi gereken işaretleri taşır (taklit önek, yetkili rolde seed hesabı, identity rolü).
    /// <para>Servis hesabı: <c>service-account-</c> öneki VE (Keycloak <c>serviceAccountClientId</c> döndürdüyse o; yoksa
    /// e-posta boş VE kimlik bilgisi yok VE federated identity yok — ikisi de okunabilmiş olmalı).</para>
    /// </summary>
    public static (PrivilegedAccountClass Class, string? Note) Classify(
        KeycloakRoleMember member, IdentityInfo? identity, IReadOnlySet<string> knownIds, ServiceAccountEvidence? evidence = null)
    {
        ArgumentNullException.ThrowIfNull(member);
        ArgumentNullException.ThrowIfNull(knownIds);

        var notes = new List<string>();
        if (identity is { RowCount: > 1 })
            notes.Add($"identity'de {identity.RowCount} satır");

        if (HasServicePrefix(member.Username))
        {
            if (!string.IsNullOrWhiteSpace(member.ServiceAccountClientId))
            {
                notes.Insert(0, $"client={member.ServiceAccountClientId}");
                return (PrivilegedAccountClass.ServiceAccount, Join(notes));
            }

            var hasEmail = !string.IsNullOrWhiteSpace(member.Email);
            if (!hasEmail && evidence is { HasCredentials: false, HasFederatedIdentity: false })
            {
                notes.Insert(0, $"client={member.Username[ServiceAccountPrefix.Length..]} (addan; e-posta, kimlik bilgisi ve federated identity yok)");
                return (PrivilegedAccountClass.ServiceAccount, Join(notes));
            }

            notes.Add(
                hasEmail ? "service-account- öneki var ama e-posta taşıyor — taklit olabilir"
                : evidence?.HasCredentials == true ? "service-account- öneki var ama kimlik bilgisi (parola/OTP) taşıyor — taklit olabilir"
                : evidence?.HasFederatedIdentity == true ? "service-account- öneki var ama federated identity bağlı — taklit olabilir"
                : "service-account- öneki var ama servis hesabı doğrulanamadı (kimlik bilgisi/federated identity okunamadı)");
        }

        if (SeedDataConventions.IsSeedEmail(member.Username) || identity is { IsSeedData: true })
        {
            // Seed aracı yalnızca Student/Teacher/Parent atar; yetkili rolde seed hesabı açıklanamaz.
            notes.Add(identity is { IsSeedData: true }
                ? "SEED hesabı, rol sonradan eklenmiş"
                : "seed desenli ad ama identity'de IsSeedData=true satırı yok");
        }

        if (knownIds.Contains(member.Id))
            return (PrivilegedAccountClass.Known, Join(notes));

        notes.Add(identity is null
            ? "identity kaydı yok"
            : $"identity rolü={(string.IsNullOrEmpty(identity.Role) ? "(boş)" : identity.Role)}");

        return (PrivilegedAccountClass.Unexplained, Join(notes));
    }

    private static bool HasServicePrefix(string username)
        => username.StartsWith(ServiceAccountPrefix, StringComparison.OrdinalIgnoreCase);

    private static string? Join(List<string> notes) => notes.Count == 0 ? null : string.Join("; ", notes);

    /// <summary>
    /// <c>alice@example.com</c> → <c>a***@e***.com</c>. Alan adının yalnızca ilk etiketinin ilk harfi ve son etiketi
    /// (TLD) kalır. Boş → <c>-</c>; <c>@</c> yoksa ilk harf + <c>***</c>.
    /// </summary>
    public static string MaskEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email)) return "-";
        var value = email.Trim();
        var at = value.LastIndexOf('@');
        if (at < 0) return MaskToken(value);

        var local = value[..at];
        var labels = value[(at + 1)..].Split('.', StringSplitOptions.RemoveEmptyEntries);
        var maskedDomain = labels.Length switch
        {
            0 => "***",
            1 => MaskToken(labels[0]),
            _ => MaskToken(labels[0]) + "." + labels[^1]
        };
        return MaskToken(local) + "@" + maskedDomain;
    }

    /// <summary>
    /// Kullanıcı adı çoğunlukla e-postadır (register: username = email) → e-posta gibi maskelenir. Gerçek servis hesabı
    /// adları (<c>service-account-&lt;client&gt;</c>, SERVICE_ACCOUNT olarak doğrulanmış) kişisel veri değildir, açık kalır.
    /// </summary>
    public static string MaskUsername(string? username, bool verifiedServiceAccount = false)
    {
        if (string.IsNullOrWhiteSpace(username)) return "-";
        if (username.Contains('@')) return MaskEmail(username);
        if (verifiedServiceAccount && HasServicePrefix(username))
            return username;
        return MaskToken(username);
    }

    private static string MaskToken(string token)
        => string.IsNullOrEmpty(token) ? "***" : token[..1] + "***";
}
