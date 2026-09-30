using System.Text;

namespace ExamApp.Api.Helpers;

/// <summary>
/// issue #105 (security O2): herkese açık yorum gövdesinin temizliği. Sıra:
/// <list type="number">
/// <item>\r\n ve tek \r → \n (tarayıcı/işletim sistemi farkı; \r reddedilmez, normalize edilir).</item>
/// <item>Unicode NFC normalizasyonu (eşlenmemiş surrogate → geçersiz).</item>
/// <item>\n ve \t dışındaki C0/C1 kontrol karakterleri (NUL ve DEL dahil) → REDDEDİLİR.</item>
/// <item>Bidi override/isolate (U+202A–U+202E, U+2066–U+2069) ve sıfır genişlikli karakterler (U+200B–U+200F, U+FEFF)
/// → sessizce AYIKLANIR (görünmez; metni ters çevirip sahte içerik/kimlik gösterimi yapılamasın).</item>
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

        var text = body.Replace("\r\n", "\n").Replace('\r', '\n');

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

        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            // char.IsControl: U+0000–U+001F ve U+007F–U+009F (C0, DEL, C1).
            if (char.IsControl(c) && c != '\n' && c != '\t')
                return Outcome.InvalidCharacters;

            if (IsStripped(c))
                continue;

            builder.Append(c);
        }

        text = builder.ToString().Trim();
        if (text.Length == 0)
            return Outcome.Required;
        if (text.Length > maxLength)
            return Outcome.TooLong;

        sanitized = text;
        return Outcome.Ok;
    }

    private static bool IsStripped(char c) =>
        c is >= '‪' and <= '‮'   // LRE, RLE, PDF, LRO, RLO
            or >= '⁦' and <= '⁩' // LRI, RLI, FSI, PDI
            or >= '​' and <= '‏' // ZWSP, ZWNJ, ZWJ, LRM, RLM
            or '﻿';                   // ZWNBSP / BOM
}
