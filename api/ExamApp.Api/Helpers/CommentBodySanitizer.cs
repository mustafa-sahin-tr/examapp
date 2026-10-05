using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ExamApp.Api.Helpers;

/// <summary>
/// issue #105 (security O2): herkese açık yorum gövdesinin temizliği; issue #106 doğrudan mesaj gövdesi ve şikayet notu da
/// aynı kuralı kullanır. Sıra:
/// <list type="number">
/// <item>\r\n, tek \r ve U+2028/U+2029 (satır/paragraf ayırıcı, #106 security D4) → \n (reddedilmez, normalize edilir).</item>
/// <item>Unicode NFC normalizasyonu (eşlenmemiş surrogate → geçersiz).</item>
/// <item>\n ve \t dışındaki C0/C1 kontrol karakterleri (NUL ve DEL dahil) → REDDEDİLİR.</item>
/// <item>TÜM Unicode biçim (Cf) karakterleri — bidi override/isolate, sıfır genişlikli karakterler (ZWJ YALNIZ iki emoji arasında
/// korunur — birleşik emojiler bozulmasın), BOM, soft hyphen, ek düzlemdeki tag karakterleri — sessizce AYIKLANIR (görünmez; metni
/// ters çevirip sahte içerik/kimlik gösterimi yapılamasın). #106 security D4: önceki sabit aralık listesi Cf'nin tamamına
/// genişletildi; kontrol rune bazlıdır.</item>
/// <item>Trim; boş kalırsa "gerekli", ardından uzunluk sınırı (temizlik SONRASI).</item>
/// </list>
/// </summary>
public static class CommentBodySanitizer
{
    public enum Outcome
    {
        Ok = 0,
        Required = 1,
        InvalidCharacters = 2,
        TooLong = 3
    }

    public static Outcome TrySanitize(string? body, int maxLength, out string sanitized)
    {
        sanitized = string.Empty;
        if (body == null)
            return Outcome.Required;

        var text = body.Replace("\r\n", "\n").Replace('\r', '\n')
            .Replace((char)0x2028, '\n').Replace((char)0x2029, '\n');

        // Eşlenmemiş surrogate: Normalize bazı durumlarda fırlatır, bazılarında olduğu gibi bırakır — açıkça kontrol et.
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1]))
                    return Outcome.InvalidCharacters;
                i++;
            }
            else if (char.IsLowSurrogate(c))
            {
                return Outcome.InvalidCharacters;
            }
        }

        try
        {
            text = text.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            return Outcome.InvalidCharacters;
        }

        var runes = new List<Rune>(text.Length);
        foreach (var rune in text.EnumerateRunes())
        {
            // Rune.IsControl: U+0000–U+001F ve U+007F–U+009F (C0, DEL, C1).
            if (Rune.IsControl(rune) && rune.Value != '\n' && rune.Value != '\t')
                return Outcome.InvalidCharacters;
            runes.Add(rune);
        }

        var builder = new StringBuilder(text.Length);
        Rune? lastKept = null;
        for (var i = 0; i < runes.Count; i++)
        {
            var rune = runes[i];
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Format)
            {
                // Tek istisna ZWJ (U+200D): YALNIZ iki emoji arasında korunur (aile/meslek/bayrak birleşimleri bozulmasın,
                // #106 review). Metin arasındaki ya da tek başına ZWJ diğer Cf'ler gibi ayıklanır.
                if (rune.Value == ZeroWidthJoiner && lastKept is { } prev && EndsEmoji(prev)
                    && i + 1 < runes.Count && IsEmojiBase(runes[i + 1]))
                {
                    builder.Append(rune.ToString());
                    lastKept = rune;
                }
                continue;
            }

            builder.Append(rune.ToString());
            lastKept = rune;
        }

        text = builder.ToString().Trim();
        if (text.Length == 0)
            return Outcome.Required;
        if (text.Length > maxLength)
            return Outcome.TooLong;

        sanitized = text;
        return Outcome.Ok;
    }

    private const int ZeroWidthJoiner = 0x200D;

    /// <summary>
    /// .NET'te Extended_Pictographic özelliği yok; yaklaşık karşılık: emoji tabanları Unicode "OtherSymbol" (So) kategorisinde
    /// (👨 👩 💻 🔥 ♀ ♂ ❤ 🏳 …). Harf/rakam/noktalama So değildir — metin arasındaki ZWJ korunmaz.
    /// </summary>
    private static bool IsEmojiBase(Rune rune) => Rune.GetUnicodeCategory(rune) == UnicodeCategory.OtherSymbol;

    /// <summary>ZWJ'den önceki öğe bir emoji ile bitiyor mu: emoji tabanı, VS16 (U+FE0F) ya da ten rengi değiştirici (U+1F3FB–1F3FF).</summary>
    private static bool EndsEmoji(Rune rune) =>
        IsEmojiBase(rune) || rune.Value == 0xFE0F || rune.Value is >= 0x1F3FB and <= 0x1F3FF;
}
