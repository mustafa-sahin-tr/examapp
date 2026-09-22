using System;
using System.Text.RegularExpressions;

namespace ExamApp.Foundation.Security;

/// <summary>
/// Test verisi (seed) hesaplarının ortak sözleşmesi (issue #217/#218). Hem exam API (<c>seed-teachers</c>
/// üretici) hem auth-api (<c>/api/auth/dev/seed-users</c> tüketici) aynı sabiti kullanır: auth-api yalnızca
/// bu alan adındaki e-postaları kabul eder — gerçek bir kullanıcının e-postası dev ucundan asla geçemez.
/// </summary>
public static partial class SeedDataConventions
{
    /// <summary>Seed hesaplarının tek alan adı. Gerçek kullanıcılar bu alanı kullanamaz.</summary>
    public const string EmailDomain = "seed.examapp.local";

    /// <summary>Yerel kısım <c>seed.</c> ile başlar; ör. <c>seed.t.&lt;kurumKodu&gt;.&lt;brans&gt;.&lt;n&gt;</c>.</summary>
    public const string EmailLocalPrefix = "seed.";

    /// <summary>
    /// Sahiplik kilidi: seed aracının açtığı her Keycloak kullanıcısına yazılan attribute. Identity satırı olmayan
    /// (yetim) bir Keycloak hesabı yalnızca bu attribute'u taşıyorsa sahiplenilir/silinir — seed desenli e-postayla
    /// Keycloak self-registration'dan açılmış bir hesaba dokunulmaz.
    /// </summary>
    public const string KeycloakOriginAttribute = "seed_origin";
    public const string KeycloakOriginValue = "examapp-seed";

    [GeneratedRegex(@"^seed\.[a-z0-9.\-]+@seed\.examapp\.local$", RegexOptions.CultureInvariant)]
    private static partial Regex SeedEmailRegex();

    /// <summary>Küçük harf, ASCII, yalnızca seed alanı. Büyük harf/boşluk kabul edilmez (Keycloak username = e-posta).</summary>
    public static bool IsSeedEmail(string? email)
        => !string.IsNullOrEmpty(email) && SeedEmailRegex().IsMatch(email);
}
