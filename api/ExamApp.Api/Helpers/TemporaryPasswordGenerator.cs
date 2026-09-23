using System;
using System.Security.Cryptography;

namespace ExamApp.Api.Helpers;

/// <summary>
/// Admin şifre sıfırlaması için geçici şifre üretir (issue #156). Kaynak CSPRNG (<see cref="RandomNumberGenerator"/>);
/// <see cref="Random"/> KULLANILMAZ.
///
/// Alfabe ekranda okunup elle yazılacağı için belirsiz karakterleri içermez (0/O/o, 1/l/I, ayrıca tırnak/boşluk/ters
/// bölü gibi kopyalamada sorun çıkaran semboller yok). Her sınıftan (büyük, küçük, rakam, sembol) en az bir karakter
/// garanti edilir; kalan konumlar birleşik alfabeden seçilir ve sonuç Fisher–Yates ile karıştırılır (zorunlu
/// karakterlerin yeri tahmin edilemesin). 16 karakter ≈ 16·log2(56) ≈ 92 bit entropi.
///
/// Realm password policy: dev realm-export'ta <c>passwordPolicy</c> tanımlı değil. Üretilen şifre yaygın Keycloak
/// politikalarının hepsini karşılar: length(≤16), upperCase(1), lowerCase(1), digits(1), specialChars(1), notUsername,
/// notEmail. Politika daha fazlasını isterse (ör. specialChars(2)) Keycloak 400 döner → uç 502 verir.
/// </summary>
public static class TemporaryPasswordGenerator
{
    public const int DefaultLength = 16;

    public const string Upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";   // I ve O yok
    public const string Lower = "abcdefghijkmnpqrstuvwxyz";   // l ve o yok
    public const string Digits = "23456789";                  // 0 ve 1 yok
    public const string Symbols = "!@#$%*-_+=?";

    private static readonly string[] Classes = [Upper, Lower, Digits, Symbols];
    private static readonly string All = Upper + Lower + Digits + Symbols;

    public static string Generate(int length = DefaultLength)
    {
        if (length < Classes.Length)
            throw new ArgumentOutOfRangeException(nameof(length), $"Length must be at least {Classes.Length}.");

        var chars = new char[length];
        for (var i = 0; i < Classes.Length; i++)
            chars[i] = Pick(Classes[i]);
        for (var i = Classes.Length; i < length; i++)
            chars[i] = Pick(All);

        for (var i = length - 1; i > 0; i--)
        {
            var j = RandomNumberGenerator.GetInt32(i + 1);
            (chars[i], chars[j]) = (chars[j], chars[i]);
        }

        return new string(chars);
    }

    private static char Pick(string alphabet) => alphabet[RandomNumberGenerator.GetInt32(alphabet.Length)];
}
