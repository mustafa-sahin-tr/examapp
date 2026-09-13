using System;
using System.Collections.Generic;
using System.Linq;

namespace ExamApp.Foundation.Localization;

/// <summary>
/// Platformun desteklediği dillerin tek kaynağı (issue #181). auth-api (kullanıcının
/// <c>PreferredLocale</c> alanını doğrular), exam API (request culture çözümlemesi) ve
/// BadgeService (bildirim metinleri) aynı listeyi kullanmalı — bu yüzden Foundation'da.
/// UI tarafındaki karşılığı: <c>ui/src/app/models/locale.ts</c> (elle senkron tutulur).
/// </summary>
public static class SupportedLocales
{
    /// <summary>Kullanıcı bir tercih belirtmediğinde/geçersiz değer geldiğinde kullanılan dil.</summary>
    public const string Default = "tr";

    /// <summary>Desteklenen dil kodları (bölgesiz, küçük harf).</summary>
    public static readonly IReadOnlyList<string> All = new[] { "tr", "en" };

    /// <summary>
    /// Dil kodu → .NET culture adı eşlemesi. <see cref="ToCultureName"/> üzerinden kullanılır;
    /// RequestLocalization'ın SupportedCultures listesi de buradan üretilir.
    /// </summary>
    private static readonly IReadOnlyDictionary<string, string> CultureNames =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["tr"] = "tr-TR",
            ["en"] = "en-US"
        };

    /// <summary>Desteklenen .NET culture adları (<c>tr-TR</c>, <c>en-US</c>).</summary>
    public static IReadOnlyList<string> AllCultureNames { get; } =
        All.Select(l => CultureNames[l]).ToArray();

    /// <summary>Varsayılan dilin .NET culture adı (<c>tr-TR</c>).</summary>
    public static string DefaultCultureName => CultureNames[Default];

    /// <summary>
    /// Serbest metni kanonik dil koduna indirger: küçük harfe çevirir, bölge ekini kırpar
    /// (<c>tr-TR</c> → <c>tr</c>, <c>en_US</c> → <c>en</c>). Desteklenmeyen/boş değerde
    /// <see cref="Default"/> döner — çağıran tarafın hata üretmesi gerekiyorsa
    /// <see cref="TryNormalize"/> kullanılmalı.
    /// </summary>
    public static string Normalize(string? locale)
        => TryNormalize(locale, out var normalized) ? normalized : Default;

    /// <summary>
    /// <see cref="Normalize"/> ile aynı indirgemeyi yapar ama desteklenmeyen değeri
    /// sessizce varsayılana çevirmez — doğrulama (400 dönmek) için bunu kullan.
    /// </summary>
    public static bool TryNormalize(string? locale, out string normalized)
    {
        normalized = Default;
        if (string.IsNullOrWhiteSpace(locale))
        {
            return false;
        }

        var candidate = locale.Trim();
        var separator = candidate.IndexOfAny(new[] { '-', '_' });
        if (separator > 0)
        {
            candidate = candidate[..separator];
        }

        candidate = candidate.ToLowerInvariant();
        if (!All.Contains(candidate))
        {
            return false;
        }

        normalized = candidate;
        return true;
    }

    /// <summary>Dil kodunu .NET culture adına çevirir; desteklenmiyorsa varsayılanınkini döner.</summary>
    public static string ToCultureName(string? locale) => CultureNames[Normalize(locale)];

    /// <summary>Değer desteklenen bir dile indirgenebiliyor mu?</summary>
    public static bool IsSupported(string? locale) => TryNormalize(locale, out _);
}
