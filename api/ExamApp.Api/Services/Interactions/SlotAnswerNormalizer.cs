using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;

namespace ExamApp.Api.Services.Interactions;

/// <summary>
/// issue #575: <c>slotFill</c> kutu cevaplarının normalleştirilmesi ve karşılaştırılması (saf, durumsuz). Kuralların tek
/// kaynağı paylaşılan fixture'dır (<c>shared/fixtures/slot-normalization.v1.json</c>); C# ve TS aynı vektörleri geçer.
/// <list type="bullet">
/// <item><b>Sayı</b> (TR, yalnız ASCII rakam 0-9): tam sayı <c>12</c>, negatif <c>-3</c>, ondalık virgül <c>0,75</c>, binlik nokta <c>1.250</c> /
/// <c>1.250,5</c>, tek noktalı ondalık <c>0.75</c> (noktadan sonra TAM 3 hane değilse; 3 hane = binlik), kesir <c>a/b</c>,
/// tam sayılı kesir <c>2 3/5</c> / <c>2 tam 3/5</c>, yüzde <c>%75</c> / <c>75%</c> / <c>% 75</c>. Kesin rasyonel aritmetik
/// (<see cref="BigInteger"/>), kayan nokta yok. Birim = sayıdan sonraki harf dizisi.</item>
/// <item><b>Metin</b>: NFC, kırp, iç boşlukları teke indir, Türkçe küçük harf (İ→i, I→ı), kesme işaretlerini birleştir,
/// sondaki <c>.,;:!?</c> atılır. Aksan katlaması YOK (ılık ≠ ilik).</item>
/// <item><b>Palet</b>: NFC, tüm boşluklar silinir, büyük/küçük harf duyarlı birebir karşılaştırma.</item>
/// </list>
/// </summary>
public static class SlotAnswerNormalizer
{
    /// <summary>Sayı cevabının yazılış biçimi (<c>equivalence:false</c> iken öğrencininki çözümünkiyle aynı olmalı).</summary>
    public enum NumberForm { Integer, Decimal, Fraction, Mixed, Percent }

    /// <summary>Ayrıştırılmış sayı: indirgenmiş rasyonel değer (payda &gt; 0), biçim, kesirde en sade mi, normalize birim (yoksa null).</summary>
    public readonly record struct ParsedNumber(BigInteger Numerator, BigInteger Denominator, NumberForm Form, bool IsSimplest, string? Unit);

    /// <summary>Ayrıştırmaya girecek en uzun girdi (karakter); daha uzunu geçersiz (dev BigInteger'a karşı).</summary>
    public const int MaxNumberInputLength = 64;

    /// <summary>review #575: sayı deseni; paylaşılan fixture'ın <c>$comment</c>'inde birebir yazılıdır (testle kilitli).</summary>
    public const string NumberPatternSource =
        @"^(?:(?<neg>-) ?)?(?:(?<whole>[0-9]+) (?:tam )?(?<mn>[0-9]+) ?/ ?(?<md>[0-9]+)|(?<fn>[0-9]+) ?/ ?(?<fd>[0-9]+)|(?<dec>[0-9]+(?:[.,][0-9]+)*))(?: ?(?<unit>[\p{L}°\p{Sc}][\p{L}\p{M}°²³ ]*(?:/ ?[\p{L}][\p{L}\p{M}²³ ]*)?))?$";

    private static readonly Regex NumberPattern = new(NumberPatternSource, RegexOptions.CultureInvariant);

    private static readonly Regex GroupedInteger = new(@"^[1-9][0-9]{0,2}(?:\.[0-9]{3})+$", RegexOptions.CultureInvariant);
    private static readonly Regex CommaDecimal = new(@"^(?<int>[1-9][0-9]{0,2}(?:\.[0-9]{3})+|[0-9]+),(?<frac>[0-9]+)$", RegexOptions.CultureInvariant);
    private static readonly Regex DotDecimal = new(@"^(?<int>[0-9]+)\.(?<frac>[0-9]+)$", RegexOptions.CultureInvariant);
    private static readonly Regex Digits = new(@"^[0-9]+$", RegexOptions.CultureInvariant);
    /// <summary>
    /// review #575: boşluk sınıfı iki tarafta (C#/TS) birebir aynı olsun diye açıkça yazılır (.NET <c>\s</c> U+0085'i, JS
    /// <c>\s</c> U+FEFF'i içerir; ikisi de burada var).
    /// </summary>
    public const string WhitespaceClass = @"[\t\n\v\f\r \u0085\u00A0\u1680\u2000-\u200A\u2028\u2029\u202F\u205F\u3000\uFEFF]";

    private static readonly Regex Whitespace = new(WhitespaceClass + "+", RegexOptions.CultureInvariant);
    private static readonly Regex WhitespaceChar = new("^" + WhitespaceClass + "$", RegexOptions.CultureInvariant);
    private static readonly Regex NegativePercent = new("^- ?%", RegexOptions.CultureInvariant);

    /// <summary><see cref="WhitespaceClass"/> içindeki karakter mi?</summary>
    public static bool IsWhitespace(char c) => WhitespaceChar.IsMatch(c.ToString());

    /// <summary>null, boş ya da yalnız <see cref="WhitespaceClass"/> karakterleri = cevapsız kutu.</summary>
    public static bool IsBlank(string? value) => string.IsNullOrEmpty(value) || value.All(IsWhitespace);

    // ------------------------------------------------------------------ ortak

    /// <summary>
    /// Türkçe küçük harf (kültürden bağımsız, TS ile aynı sonuç): İ→i, I→ı, diğer her kod noktası (astral dahil) tek başına
    /// değişmez basit küçük harfe (bağlama duyarlı kural yok: Σ her zaman σ); sonra i + U+0307 → i.
    /// </summary>
    public static string TurkishLower(string value)
    {
        var sb = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
        {
            if (rune.Value == 'İ')
                sb.Append('i');
            else if (rune.Value == 'I')
                sb.Append('ı');
            else
                sb.Append(Rune.ToLowerInvariant(rune).ToString());
        }
        // "İ".toLowerCase() (Türkçe olmayan yerel) "i̇" üretir; birleşik nokta düşürülür.
        return sb.ToString().Replace("i̇", "i", StringComparison.Ordinal);
    }

    private static string CollapseWhitespace(string value) => Whitespace.Replace(value, " ").Trim(' ');

    // ------------------------------------------------------------------ sayı

    /// <summary>
    /// Sayı cevabını ayrıştırır. Geçersiz (sıfır payda, tanınmayan biçim, yüzdeye birim, sayı + tanınmayan kuyruk) → false.
    /// </summary>
    public static bool TryParseNumber(string? input, out ParsedNumber result)
    {
        result = default;
        if (IsBlank(input) || input!.Length > MaxNumberInputLength)
            return false;

        var s = CollapseWhitespace(TurkishLower(input.Normalize(NormalizationForm.FormC)).Replace('−', '-'));
        var percent = false;
        var nm = NegativePercent.Match(s);
        if (nm.Success)
        {
            // "-%75" / "- %75" = "%-75"
            percent = true;
            s = "-" + s[nm.Length..].TrimStart(' ');
        }
        else if (s.StartsWith('%'))
        {
            percent = true;
            s = s[1..].TrimStart(' ');
        }
        else if (s.EndsWith('%'))
        {
            percent = true;
            s = s[..^1].TrimEnd(' ');
        }
        if (s.Length == 0 || s.Contains('%'))
            return false;

        var m = NumberPattern.Match(s);
        if (!m.Success)
            return false;

        var negative = m.Groups["neg"].Success;
        var unit = m.Groups["unit"].Success ? NormalizeUnit(m.Groups["unit"].Value) : null;
        if (unit is { Length: 0 })
            unit = null;

        BigInteger num, den;
        NumberForm form;
        var simplest = true;

        if (m.Groups["whole"].Success)
        {
            var whole = BigInteger.Parse(m.Groups["whole"].Value);
            var mn = BigInteger.Parse(m.Groups["mn"].Value);
            var md = BigInteger.Parse(m.Groups["md"].Value);
            // Tam sayılı kesrin kesir kısmı basit kesir olmalı (0 < pay < payda).
            if (md.IsZero || mn.IsZero || mn >= md)
                return false;
            simplest = BigInteger.GreatestCommonDivisor(mn, md).IsOne;
            num = whole * md + mn;
            den = md;
            form = NumberForm.Mixed;
        }
        else if (m.Groups["fn"].Success)
        {
            num = BigInteger.Parse(m.Groups["fn"].Value);
            den = BigInteger.Parse(m.Groups["fd"].Value);
            if (den.IsZero)
                return false;
            simplest = BigInteger.GreatestCommonDivisor(num, den).IsOne;
            form = NumberForm.Fraction;
        }
        else
        {
            if (!TryParseDecimal(m.Groups["dec"].Value, out num, out den, out var isInteger))
                return false;
            form = isInteger ? NumberForm.Integer : NumberForm.Decimal;
        }

        if (percent)
        {
            if (form is NumberForm.Fraction or NumberForm.Mixed || unit != null)
                return false;
            den *= 100;
            form = NumberForm.Percent;
        }

        if (negative)
            num = -num;
        var gcd = BigInteger.GreatestCommonDivisor(num, den);
        if (!gcd.IsZero && !gcd.IsOne)
        {
            num /= gcd;
            den /= gcd;
        }

        result = new ParsedNumber(num, den, form, simplest, unit);
        return true;
    }

    /// <summary>Tam sayı / ondalık / binlik gruplu yazım → rasyonel. Kurallar sınıf özetinde.</summary>
    private static bool TryParseDecimal(string s, out BigInteger num, out BigInteger den, out bool isInteger)
    {
        num = BigInteger.Zero;
        den = BigInteger.One;
        isInteger = false;

        if (Digits.IsMatch(s))
        {
            num = BigInteger.Parse(s);
            isInteger = true;
            return true;
        }
        if (s.Contains(','))
        {
            var cm = CommaDecimal.Match(s);
            if (!cm.Success)
                return false;
            return Compose(cm.Groups["int"].Value.Replace(".", string.Empty, StringComparison.Ordinal), cm.Groups["frac"].Value, out num, out den);
        }
        if (GroupedInteger.IsMatch(s))
        {
            num = BigInteger.Parse(s.Replace(".", string.Empty, StringComparison.Ordinal));
            isInteger = true;
            return true;
        }
        var dm = DotDecimal.Match(s);
        if (!dm.Success || dm.Groups["frac"].Value.Length == 3)
            return false;
        return Compose(dm.Groups["int"].Value, dm.Groups["frac"].Value, out num, out den);
    }

    private static bool Compose(string integerPart, string fractionPart, out BigInteger num, out BigInteger den)
    {
        den = BigInteger.Pow(10, fractionPart.Length);
        num = BigInteger.Parse(integerPart) * den + BigInteger.Parse(fractionPart);
        return true;
    }

    /// <summary>
    /// review #575: kabul edilen birim yazılabilir mi? <c>"1 " + birim</c> ayrıştırılınca birim olarak aynen geri gelmeli
    /// (ör. <c>²</c>, <c>%</c>, <c>5cm</c>, astral harf geçersiz).
    /// </summary>
    public static bool IsParseableUnit(string unit)
        => TryParseNumber("1 " + unit, out var parsed) && parsed.Form == NumberForm.Integer
           && parsed.Unit != null && parsed.Unit == NormalizeUnit(unit);

    /// <summary>Birim karşılaştırma anahtarı: NFC, Türkçe küçük harf, tüm boşluklar silinir (<c>5cm</c> == <c>5 CM</c>).</summary>
    public static string NormalizeUnit(string unit)
        => Whitespace.Replace(TurkishLower(unit.Normalize(NormalizationForm.FormC)), string.Empty);

    /// <summary>Sayı çözümü (<c>InteractionSolution</c>'daki tek kutu girdisi).</summary>
    public sealed record NumberSolution(string Value, bool Equivalence, bool RequireSimplest, IReadOnlyList<string> Units, string UnitMode);

    public const string UnitModeNone = "none";
    public const string UnitModeOptional = "optional";
    public const string UnitModeRequired = "required";

    /// <summary>
    /// Sayı cevabı doğru mu? Çözüm değeri birimsiz geçerli bir sayı olmalı (değilse hiçbir cevap doğru değil).
    /// <c>equivalence</c> açık: rasyonel değer eşitliği yeter; kapalı: biçim de aynı. <c>requireSimplest</c>: öğrenci kesir/tam
    /// sayılı kesir yazdıysa en sade olmalı. Birim: <c>none</c> → birim yazılmamalı; <c>optional</c> → yoksa olur, varsa
    /// listede; <c>required</c> → olmalı ve listede.
    /// </summary>
    public static bool NumberMatches(NumberSolution solution, string? answer)
    {
        if (!TryParseNumber(solution.Value, out var expected) || expected.Unit != null)
            return false;
        if (!TryParseNumber(answer, out var actual))
            return false;

        if (actual.Numerator != expected.Numerator || actual.Denominator != expected.Denominator)
            return false;
        if (!solution.Equivalence && actual.Form != expected.Form)
            return false;
        if (solution.RequireSimplest && actual.Form is NumberForm.Fraction or NumberForm.Mixed && !actual.IsSimplest)
            return false;

        var units = solution.Units.Select(NormalizeUnit).Where(u => u.Length > 0).ToHashSet(StringComparer.Ordinal);
        return solution.UnitMode switch
        {
            UnitModeOptional => actual.Unit == null || units.Contains(actual.Unit),
            UnitModeRequired => actual.Unit != null && units.Contains(actual.Unit),
            _ => actual.Unit == null,
        };
    }

    // ------------------------------------------------------------------ metin

    private static readonly char[] TrailingPunctuation = { '.', ',', ';', ':', '!', '?' };

    /// <summary>Metin karşılaştırma anahtarı (sınıf özeti). Aksanlar korunur.</summary>
    public static string NormalizeText(string? value)
    {
        if (IsBlank(value))
            return string.Empty;
        var s = TurkishLower(value.Normalize(NormalizationForm.FormC));
        s = s.Replace('’', '\'').Replace('‘', '\'').Replace('ʼ', '\'').Replace('`', '\'').Replace('´', '\'');
        s = CollapseWhitespace(s);
        while (s.Length > 0 && (Array.IndexOf(TrailingPunctuation, s[^1]) >= 0 || IsWhitespace(s[^1])))
            s = s[..^1];
        return s;
    }

    /// <summary>Metin cevabı: normalize cevap == normalize kabul, ya da kabul + " " + normalize isteğe bağlı ek.</summary>
    public static bool TextMatches(IReadOnlyList<string> accept, IReadOnlyList<string> optionalSuffixes, string? answer)
    {
        var a = NormalizeText(answer);
        if (a.Length == 0)
            return false;
        var suffixes = optionalSuffixes.Select(NormalizeText).Where(x => x.Length > 0).ToList();
        foreach (var candidate in accept.Select(NormalizeText).Where(x => x.Length > 0))
        {
            if (a == candidate)
                return true;
            foreach (var suffix in suffixes)
            {
                if (a == candidate + " " + suffix)
                    return true;
            }
        }
        return false;
    }

    // ------------------------------------------------------------------ palet

    /// <summary>Palet karşılaştırma anahtarı: NFC, tüm boşluklar silinir; büyük/küçük harf korunur.</summary>
    public static string NormalizePalette(string? value)
        => string.IsNullOrEmpty(value) ? string.Empty : Whitespace.Replace(value.Normalize(NormalizationForm.FormC), string.Empty);

    public static bool PaletteMatches(IReadOnlyList<string> accept, string? answer)
    {
        var a = NormalizePalette(answer);
        return a.Length > 0 && accept.Any(x => string.Equals(NormalizePalette(x), a, StringComparison.Ordinal));
    }
}
