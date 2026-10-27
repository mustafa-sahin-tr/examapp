using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ExamApp.Api.Services.Labeling;

/// <summary>
/// Issue #580: cevap anahtarındaki etkinlik etiketinin ("ETKİNLİK 9", "9. Etkinlik", "9.") etkinlik bloğunun etiketiyle eşlenmesi.
/// <para>Kural:</para>
/// <list type="number">
/// <item>Normalize: Türkçe küçük harf (İ→i, I→ı), sonra Türkçe harfler ASCII'ye katlanır (ı→i, ş→s, ğ→g, ü→u, ö→o, ç→c, â→a …).</item>
/// <item>Etiket iki parçaya ayrılır: <b>tür kelimesi</b> = harf kelimeleri (sırasıyla, tek boşlukla; "etkinlik", "alistirma") ve
/// <b>numara</b> = içindeki tamsayı grupları sırasıyla ("Etkinlik 3.2" → [3, 2]; baştaki sıfırlar yok sayılır).</item>
/// <item>İki etiketin de numarası varsa: numaralar AYNI olmalı ve tür kelimeleri aynı olmalı ya da biri boş olmalı (çıplak numara
/// "9." her türle eşleşir; "Etkinlik 9" ≠ "Alıştırma 9").</item>
/// <item>İkisinin de numarası yoksa: normalize metinler (yalnız harf kelimeleri) aynı olmalı ("Etkinlik A" ≈ "etkinlik a").</item>
/// <item>Biri numaralı, diğeri numarasızsa eşleşmez. Boş etiket hiçbir şeyle eşleşmez.</item>
/// <item>Gürültü (review): numara belirteçleri ("no", "numara", "numaralı", "nolu", ":") ve numaralı etikette tek harfli son ekler
/// ("Etkinlik 9a", "9. a") tür kelimesine katılmaz — "Etkinlik No: 9" ≈ "Etkinlik 9a" ≈ "Etkinlik 9". int'e sığmayan numara → eşleşmez.</item>
/// </list>
/// </summary>
public static class ActivityLabelMatcher
{
    private static readonly CultureInfo Turkish = CultureInfo.GetCultureInfo("tr-TR");
    private static readonly Regex Numbers = new(@"\d+", RegexOptions.CultureInvariant);
    private static readonly Regex Words = new(@"[a-z]+", RegexOptions.CultureInvariant);

    // Numara belirteçleri ("Etkinlik No: 9", "9 numaralı etkinlik") tür kelimesine katılmaz.
    private static readonly HashSet<string> NoiseWords = new(StringComparer.Ordinal) { "no", "numara", "numarali", "nolu", "nr" };

    /// <summary>Etiketin tür kelimesi ve numarası (yukarıdaki kural); boş/harfsiz-numarasız etikette null.</summary>
    public sealed record Parsed(string Kind, IReadOnlyList<int> Number)
    {
        public bool HasNumber => Number.Count > 0;
    }

    public static Parsed? Parse(string? label)
    {
        if (string.IsNullOrWhiteSpace(label))
            return null;
        var folded = Fold(label);
        var numbers = new List<int>();
        foreach (Match m in Numbers.Matches(folded))
        {
            // Taşan numara: etiket eşlenemez (yanlışlıkla başka maddeye bağlanmasın).
            if (!int.TryParse(m.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var n))
                return null;
            numbers.Add(n);
        }
        var words = Words.Matches(folded).Select(m => m.Value)
            .Where(w => !NoiseWords.Contains(w))
            // Numaralı etikette tek harf ("9a", "9. a") madde/şık son ekidir, tür kelimesi değil.
            .Where(w => numbers.Count == 0 || w.Length > 1);
        var kind = string.Join(' ', words);
        if (kind.Length == 0 && numbers.Count == 0)
            return null;
        return new Parsed(kind, numbers);
    }

    public static bool Matches(string? a, string? b)
    {
        var x = Parse(a);
        var y = Parse(b);
        if (x is null || y is null)
            return false;
        if (x.HasNumber != y.HasNumber)
            return false;
        if (!x.HasNumber)
            return x.Kind == y.Kind;
        return x.Number.SequenceEqual(y.Number) && (x.Kind == y.Kind || x.Kind.Length == 0 || y.Kind.Length == 0);
    }

    /// <summary>Türkçe küçük harf + ASCII'ye katlama (aksanlar düşer).</summary>
    internal static string Fold(string value)
    {
        var lower = value.Normalize(NormalizationForm.FormC).ToLower(Turkish).Replace('ı', 'i');
        var decomposed = lower.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                sb.Append(c);
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }
}
