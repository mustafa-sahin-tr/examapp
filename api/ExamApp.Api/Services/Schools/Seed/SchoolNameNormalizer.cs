using System;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ExamApp.Api.Services.Schools.Seed;

/// <summary>
/// MEB dizinindeki okul adlarının yazım normalizasyonu (issue #216). Kaynakta ~%1 kayıt tamamı
/// büyük harf ("KARATAŞ İLKOKULU") ya da son eki küçük harf ("Kapıkaya ilkokulu") gelir.
///
/// Kurallar (bilinçli olarak dar tutuldu — "TOKİ", "DSİ", "T.E.K." gibi kısaltmalar bozulmasın):
/// <list type="bullet">
/// <item>Fazla boşluklar tek boşluğa indirilir, baş/son boşluk atılır.</item>
/// <item>Adda hiç küçük harf yoksa (tamamı büyük) ya da hiç büyük harf yoksa (tamamı küçük):
/// Türkçe kültürüyle Title Case — her kelimenin (boşluk, '-', '.', '/', '(' ile ayrılan parçanın)
/// ilk harfi büyük, kalanı küçük. "İ/I" dönüşümü tr-TR'ye göre. Kısaltmalar (bilinenler ve parantez
/// içindeki kısa kelimeler) büyük kalır, "ve" küçük yazılır: <c>FMV ÖZEL ... MESLEKİ VE TEKNİK (MBA)</c>.</item>
/// <item>Karışık yazımlı adlarda yalnızca tür son ekleri düzeltilir: ilkokulu → İlkokulu,
/// ortaokulu → Ortaokulu, lisesi → Lisesi, imam → İmam, hatip → Hatip. Diğer kelimelere dokunulmaz.</item>
/// </list>
/// </summary>
public static class SchoolNameNormalizer
{
    private static readonly CultureInfo Tr = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
    private static readonly Regex KnownAcronym = new(
        @"(?<!\p{L})(Ted|Tev|Fmv|Mef|Mev|Mba|İtü|Odtü|Ayk|Osb|Bikev|Yetev)(?!\p{L})", RegexOptions.Compiled);
    private static readonly Regex ParenthesizedAcronym = new(@"\((\p{L}{2,4})\)", RegexOptions.Compiled);
    private static readonly Regex InnerVe = new(@"(?<= )Ve(?= )", RegexOptions.Compiled);

    private static readonly (string Lower, string Proper)[] SuffixWords =
    [
        ("ilkokulu", "İlkokulu"),
        ("ortaokulu", "Ortaokulu"),
        ("lisesi", "Lisesi"),
        ("imam", "İmam"),
        ("hatip", "Hatip")
    ];

    public static string Normalize(string raw)
    {
        ArgumentNullException.ThrowIfNull(raw);
        var name = Whitespace.Replace(raw, " ").Trim();
        if (name.Length == 0)
            return name;

        var hasLower = false;
        var hasUpper = false;
        foreach (var ch in name)
        {
            if (char.IsLower(ch)) hasLower = true;
            else if (char.IsUpper(ch)) hasUpper = true;
        }

        if (hasLower && hasUpper)
            return FixSuffixWords(name);

        return RestoreSpecialWords(ToTitleCase(name));
    }

    private static string RestoreSpecialWords(string name)
    {
        name = KnownAcronym.Replace(name, m => m.Value.ToUpper(Tr));
        name = ParenthesizedAcronym.Replace(name, m => m.Value.ToUpper(Tr));
        return InnerVe.Replace(name, "ve");
    }

    /// <summary>Kelime sınırı: boşluk, '-', '.', '/', '(' — bu ayraçlardan sonraki ilk harf büyür.</summary>
    private static string ToTitleCase(string name)
    {
        var lower = name.ToLower(Tr);
        var sb = new StringBuilder(lower.Length);
        var startOfWord = true;
        foreach (var ch in lower)
        {
            if (startOfWord && char.IsLetter(ch))
            {
                sb.Append(char.ToUpper(ch, Tr));
                startOfWord = false;
            }
            else
            {
                sb.Append(ch);
                startOfWord = ch is ' ' or '-' or '.' or '/' or '(';
            }
        }
        return sb.ToString();
    }

    private static string FixSuffixWords(string name)
    {
        var words = name.Split(' ');
        for (var i = 0; i < words.Length; i++)
        {
            var lowered = words[i].ToLower(Tr);
            foreach (var (lower, proper) in SuffixWords)
            {
                if (lowered == lower && words[i] != proper)
                {
                    words[i] = proper;
                    break;
                }
            }
        }
        return string.Join(' ', words);
    }
}
