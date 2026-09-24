namespace ExamApp.Api.Helpers;

/// <summary>
/// Admin öğrenci listesinde öğrenci numarası kısmi gösterimi (issue #262, KVKK veri minimizasyonu — öğrencilerin çoğu
/// reşit değil). Ürün kararı: yalnızca son 4 karakter görünür: <c>20241234</c> → <c>****1234</c>.
/// </summary>
public static class StudentNumberMask
{
    public const string Mask = "****";

    /// <summary>Görünür bırakılan son karakter sayısı.</summary>
    public const int VisibleSuffixLength = 4;

    /// <summary>
    /// Kural (sunucuda uygulanır; tam numara listeden hiç çıkmaz):
    /// <list type="bullet">
    /// <item>null / boş / boşluk → <c>""</c> (numarası olmayan öğrenci; liste sözleşmesi boş string).</item>
    /// <item>Kırpılmış uzunluk ≤ 4 → yalnızca <c>****</c> (tamamı gizli; aksi halde numaranın tamamı açığa çıkardı).</item>
    /// <item>Aksi halde <c>****</c> + son 4 karakter. Önek sabit uzunlukta: numaranın uzunluğu da sızmaz.</item>
    /// </list>
    /// </summary>
    public static string Apply(string? studentNumber)
    {
        if (string.IsNullOrWhiteSpace(studentNumber))
            return string.Empty;

        var value = studentNumber.Trim();
        if (value.Length <= VisibleSuffixLength)
            return Mask;

        var suffixStart = value.Length - VisibleSuffixLength;
        // Son 4 "karakter" bir surrogate çiftini bölmesin (numara pratikte ASCII; savunmacı).
        if (char.IsLowSurrogate(value[suffixStart]))
            suffixStart++;

        return Mask + value[suffixStart..];
    }
}
