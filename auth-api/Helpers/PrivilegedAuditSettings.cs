namespace ExamApp.Api.Helpers;

/// <summary>
/// <c>audit-privileged-users</c> komutu ayarları (issue #267). "PrivilegedAudit" bölümünden okunur.
/// </summary>
public class PrivilegedAuditSettings
{
    public const string SectionName = "PrivilegedAudit";

    /// <summary>
    /// Elle açılmış, meşruiyeti bilinen yetkili hesapların Keycloak id'leri (sub) — tam eşleşme. Kullanıcı adı KABUL
    /// EDİLMEZ: register açığıyla (#240) kullanıcı adı serbestçe seçilebildiği için ad tek başına kanıt değildir.
    /// appsettings.json'da BOŞ dizi — gerçek değer ortam başına env ile verilir
    /// (<c>PrivilegedAudit__KnownAccounts__0=&lt;sub&gt;</c>) ki kod deposunda hesap listesi tutulmasın.
    /// </summary>
    public List<string> KnownAccounts { get; set; } = new();
}
