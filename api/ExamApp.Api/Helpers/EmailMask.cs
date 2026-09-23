using System.Globalization;

namespace ExamApp.Api.Helpers;

/// <summary>
/// Liste görünümleri için e-posta maskeleme (issue #246, KVKK veri minimizasyonu):
/// <c>ali.veli@okul.k12.tr</c> → <c>a***@okul.k12.tr</c>. Maskeleme sunucuda yapılır; tam adres listeden hiç çıkmaz.
/// </summary>
public static class EmailMask
{
    public const string Mask = "***";

    /// <summary>
    /// Kural: yerel kısmın ilk karakteri + <c>***</c> + <c>@</c> + domain. Güvenli taraf:
    /// <list type="bullet">
    /// <item>null / boş / boşluk → <c>""</c> (liste sözleşmesi: çözülemeyen e-posta boş string).</item>
    /// <item><c>@</c> yok, yerel kısım boş ya da domain boş (geçersiz biçim) → yalnızca <c>***</c>; hiçbir parça sızmaz.</item>
    /// <item>Yerel kısım tek karakter → <c>***@domain</c>; aksi halde yerel kısmın tamamı açığa çıkardı.</item>
    /// </list>
    /// Ayırıcı olarak SON <c>@</c> alınır (tırnaklı yerel kısım <c>@</c> içerebilir). İlk "karakter" bir metin öğesidir
    /// (surrogate çifti / birleşik karakter bölünmez).
    /// </summary>
    public static string Apply(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
            return string.Empty;

        var value = email.Trim();
        var at = value.LastIndexOf('@');
        if (at <= 0 || at == value.Length - 1)
            return Mask;

        var local = value[..at];
        var domain = value[(at + 1)..];

        var firstLength = StringInfo.GetNextTextElementLength(local);
        if (firstLength >= local.Length)
            return $"{Mask}@{domain}";

        return $"{local[..firstLength]}{Mask}@{domain}";
    }
}
