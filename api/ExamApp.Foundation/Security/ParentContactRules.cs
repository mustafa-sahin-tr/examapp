using System;
using System.Linq;
using System.Net.Mail;
using System.Text;
using System.Text.RegularExpressions;

namespace ExamApp.Foundation.Security;

/// <summary>
/// Issue #440: öğrencinin kayıtta / "veli bilgisi ekle" formunda verdiği veli iletişim bilgilerinin doğrulama + normalizasyon kuralları.
/// exam API (birincil) ve auth-api (ikinci savunma) aynı kuralı kullanır.
/// <list type="bullet">
/// <item>E-posta: kırpılır, küçük harfe çevrilir; tek <c>@</c>, noktalı alan adı, boşluksuz, en fazla <see cref="EmailMaxLength"/>.</item>
/// <item>Telefon: yalnız TR cep telefonu. Boşluk, tire, nokta ve parantez atılır; <c>05xxxxxxxxx</c>, <c>5xxxxxxxxx</c>,
/// <c>905xxxxxxxxx</c> ve <c>+905xxxxxxxxx</c> kabul edilir; E.164 (<c>+905xxxxxxxxx</c>) olarak saklanır.</item>
/// <item>Ad: kırpılır, iç boşluklar teke indirilir; en az <see cref="NameMinLength"/>, en fazla <see cref="NameMaxLength"/> karakter, en az
/// bir harf; yalnız harf, boşluk, nokta, tire ve kesme işareti. Bağlantıya benzeyen içerik (<c>www.</c>, <c>x.com</c> gibi harf-nokta-harf)
/// reddedilir — ad davet e-postasına girer, spam/oltalama metni taşımasın.</item>
/// </list>
/// </summary>
public static partial class ParentContactRules
{
    public const int EmailMaxLength = 254;
    public const int NameMinLength = 2;
    public const int NameMaxLength = 100;

    /// <summary>E.164 TR cep telefonu uzunluğu (<c>+90</c> + 10 hane).</summary>
    public const int PhoneE164Length = 13;

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s.]{2,}$", RegexOptions.CultureInvariant)]
    private static partial Regex EmailShape();

    [GeneratedRegex(@"^5\d{9}$", RegexOptions.CultureInvariant)]
    private static partial Regex TrMobileNational();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"^[\p{L}\p{M} .'’\-]+$", RegexOptions.CultureInvariant)]
    private static partial Regex NameCharacters();

    /// <summary>Alan adı / bağlantı benzeri: "www." ya da harf + nokta + en az iki harf (örn. "site.com", "x.co").</summary>
    [GeneratedRegex(@"www\.|\p{L}\.\p{L}{2,}", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex LinkLike();

    /// <summary>Kırpılmış + küçük harf e-posta; geçersizse null.</summary>
    public static string? NormalizeEmail(string? email)
    {
        var value = (email ?? string.Empty).Trim().ToLowerInvariant();
        if (value.Length == 0 || value.Length > EmailMaxLength || !EmailShape().IsMatch(value))
            return null;
        try
        {
            var parsed = new MailAddress(value);
            return string.Equals(parsed.Address, value, StringComparison.Ordinal) ? value : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>TR cep telefonu → <c>+905xxxxxxxxx</c>; geçersizse null.</summary>
    public static string? NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return null;

        var sb = new StringBuilder(phone.Length);
        foreach (var c in phone.Trim())
        {
            if (char.IsWhiteSpace(c) || c is '-' or '.' or '(' or ')')
                continue;
            sb.Append(c);
        }
        var raw = sb.ToString();

        string national;
        if (raw.StartsWith("+90", StringComparison.Ordinal))
            national = raw[3..];
        else if (raw.StartsWith("90", StringComparison.Ordinal) && raw.Length == 12)
            national = raw[2..];
        else if (raw.StartsWith('0') && raw.Length == 11)
            national = raw[1..];
        else
            national = raw;

        return TrMobileNational().IsMatch(national) ? "+90" + national : null;
    }

    /// <summary>Kırpılmış, iç boşlukları teke indirilmiş ad; geçersizse null.</summary>
    public static string? NormalizeName(string? name)
    {
        var value = Whitespace().Replace((name ?? string.Empty).Trim(), " ");
        if (value.Length < NameMinLength || value.Length > NameMaxLength || !value.Any(char.IsLetter))
            return null;
        if (!NameCharacters().IsMatch(value) || LinkLike().IsMatch(value))
            return null;
        return value;
    }

    /// <summary>Ad-soyad → (ad, soyad): son kelime soyad, kalanı ad; tek kelimeyse soyad boş.</summary>
    public static (string FirstName, string LastName) SplitName(string fullName)
    {
        var value = (fullName ?? string.Empty).Trim();
        var idx = value.LastIndexOf(' ');
        return idx <= 0 ? (value, string.Empty) : (value[..idx], value[(idx + 1)..]);
    }

    /// <summary>Veli hesabının realm rolü.</summary>
    public const string ParentRole = "Parent";
}
