using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace ExamApp.Foundation.Security;

/// <summary>
/// Issue #438 (epic #435): velinin açtığı çocuk hesabının ortak kuralları. exam API (istek doğrulaması) ve auth-api (Keycloak'a
/// gitmeden önce ikinci savunma) aynı tanımı kullanır.
/// <list type="bullet">
/// <item>Kullanıcı adı: 4–30 karakter, yalnız <c>[a-z0-9._]</c>. Girdi kırpılıp küçük harfe çevrilir (Keycloak da kullanıcı adını
/// küçük harfle saklar). '@' içermediği için hiçbir e-posta kullanıcı adıyla (e-posta = kullanıcı adı) çakışamaz.</item>
/// <item>Parola: realm'de <c>passwordPolicy</c> yok; yerel taban 8–128 karakter, en az bir harf ve bir rakam, baş/son boşluksuz.
/// Realm'e politika eklenirse Keycloak'ın reddi ayrıca <see cref="Contracts.ChildAccountErrorCodes.PasswordRejected"/>'a eşlenir.</item>
/// </list>
/// Parola hiçbir yerde loglanmaz/saklanmaz/yanıtta dönmez.
/// </summary>
public static partial class ChildAccountRules
{
    public const int UsernameMinLength = 4;
    public const int UsernameMaxLength = 30;
    public const int PasswordMinLength = 8;
    public const int PasswordMaxLength = 128;
    public const int NameMaxLength = 100;

    /// <summary>
    /// Keycloak kullanıcı attribute'u: hesap veli tarafından açıldı ve yönetiliyor (şifre sıfırlama yalnız bu işaretli hesaplarda).
    /// Realm'in <c>unmanagedAttributePolicy=ADMIN_EDIT</c> ayarıyla kullanıcı kendisi değiştiremez.
    /// </summary>
    public const string KeycloakParentManagedAttribute = "parent_managed";
    public const string KeycloakParentManagedValue = "true";

    /// <summary>Çocuk hesabının realm rolü.</summary>
    public const string StudentRole = "Student";

    [GeneratedRegex(@"^[a-z0-9._]{4,30}$", RegexOptions.CultureInvariant)]
    private static partial Regex UsernameRegex();

    /// <summary>Kırpar ve küçük harfe çevirir (invariant); null → boş.</summary>
    public static string NormalizeUsername(string? username) => (username ?? string.Empty).Trim().ToLowerInvariant();

    /// <summary><see cref="NormalizeUsername"/> sonrası biçim geçerli mi.</summary>
    public static bool IsValidUsername(string? normalizedUsername)
        => normalizedUsername is not null && UsernameRegex().IsMatch(normalizedUsername);

    /// <summary>Yerel parola tabanı (Keycloak politikası ayrıca uygulanır).</summary>
    public static bool IsValidPassword(string? password)
    {
        if (string.IsNullOrEmpty(password) || password.Length < PasswordMinLength || password.Length > PasswordMaxLength)
            return false;
        if (char.IsWhiteSpace(password[0]) || char.IsWhiteSpace(password[^1]))
            return false;
        return password.Any(char.IsLetter) && password.Any(char.IsDigit);
    }

    /// <summary>Parola kullanıcı adını içeremez (büyük/küçük harf duyarsız) — tahmin edilebilir çocuk hesabı olmasın.</summary>
    public static bool PasswordContainsUsername(string? password, string? normalizedUsername)
        => !string.IsNullOrEmpty(password) && !string.IsNullOrEmpty(normalizedUsername)
           && password.Contains(normalizedUsername, StringComparison.OrdinalIgnoreCase);

    /// <summary>Ad/soyad: kırpılmış, boş değil, en fazla <see cref="NameMaxLength"/>.</summary>
    public static bool IsValidName(string? name)
    {
        var trimmed = name?.Trim();
        return !string.IsNullOrEmpty(trimmed) && trimmed.Length <= NameMaxLength;
    }
}
