namespace ExamApp.Api.Models.Audit;

/// <summary>Yetkili hesabın meşruiyet sınıfı (issue #267). Sıra = öncelik: ilk eşleşen sınıf atanır.</summary>
public enum PrivilegedAccountClass
{
    /// <summary>
    /// Keycloak client service account: <c>service-account-</c> öneki VE (Keycloak <c>serviceAccountClientId</c> döndürdüyse)
    /// ya da (e-posta boş, kimlik bilgisi yok, federated identity yok — ikisi de OKUNABİLMİŞ olmalı; fail-closed).
    /// </summary>
    ServiceAccount,

    /// <summary><c>PrivilegedAudit:KnownAccounts</c> listesinde — yalnızca Keycloak id (sub) ile, tam eşleşme.</summary>
    Known,

    /// <summary>Hiçbir meşru kaynakla eşleşmedi — incelenmeli. Yetkili roldeki seed hesabı da buradadır (seed aracı bu rolleri atamaz).</summary>
    Unexplained
}

public static class PrivilegedAccountClassNames
{
    public static string ToLabel(this PrivilegedAccountClass c) => c switch
    {
        PrivilegedAccountClass.ServiceAccount => "SERVICE_ACCOUNT",
        PrivilegedAccountClass.Known => "KNOWN",
        _ => "UNEXPLAINED"
    };
}

/// <summary>
/// Denetim satırı. Kişisel veri maskelidir (<see cref="UsernameMasked"/>, <see cref="EmailMasked"/>); Keycloak id (sub)
/// bilinçli olarak açık — exam DB'deki <c>LoginEvents</c>/<c>AdminDataAccessLogs</c> sorgularının anahtarı.
/// Identity alanları: auth-api identity DB <c>Users</c> tablosu (soft-delete dahil, <c>KeycloakId</c> ile eşleşir);
/// satır yoksa <see cref="IdentityFound"/> false ve identity alanları null.
/// <see cref="Source"/>: <c>direct</c> (rol doğrudan atanmış) ya da <c>group:/yol</c> (grup üzerinden dolaylı).
/// </summary>
public sealed record PrivilegedAccountRow(
    string Role,
    string KeycloakId,
    string UsernameMasked,
    string EmailMasked,
    bool Enabled,
    DateTimeOffset? KeycloakCreatedAt,
    bool IdentityFound,
    DateTime? IdentityCreatedAt,
    bool? IdentityIsDeleted,
    string? IdentityRole,
    bool? IdentityIsSeedData,
    PrivilegedAccountClass Class,
    string? Note,
    string Source = PrivilegedAccountRow.DirectSource)
{
    public const string DirectSource = "direct";
}

/// <param name="RoleMissing">Rol realm'de tanımlı değil (üye olamaz); denetim hatası sayılmaz.</param>
public sealed record PrivilegedRoleSummary(
    string Role, int Total, int ServiceAccount, int Known, int Unexplained, bool RoleMissing = false);

/// <param name="Warnings">
/// Dolaylı yetki bulguları (role atanmış grup, yetkili rolü içeren kompozit rol). Satır bazında açıklanamasa bile
/// yapılandırma düzeyinde incelenmesi gerekir → bulgu sayılır (exit 2).
/// </param>
public sealed record PrivilegedAuditReport(
    DateTimeOffset GeneratedAt,
    IReadOnlyList<PrivilegedAccountRow> Rows,
    IReadOnlyList<PrivilegedRoleSummary> Summary,
    IReadOnlyList<string>? Warnings = null)
{
    public IReadOnlyList<string> WarningList => Warnings ?? [];

    public bool HasUnexplained => Rows.Any(r => r.Class == PrivilegedAccountClass.Unexplained);

    /// <summary>UNEXPLAINED hesap ya da dolaylı yetki uyarısı var → exit 2.</summary>
    public bool HasFindings => HasUnexplained || WarningList.Count > 0;
}
