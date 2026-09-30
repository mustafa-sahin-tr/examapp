using System.Text;

namespace ExamApp.Foundation.Security;

/// <summary>
/// issue #105 (security D2): bildirim başlığı/gövdesi gibi tek satırlık GÖRÜNTÜLEME metinlerinin temizliği.
/// Yorum gövdesi için olan <c>CommentBodySanitizer</c>'dan farkı: reddetmez, her zaman kabul edilebilir bir metin döner.
/// Bidi override/isolate (U+202A–202E, U+2066–2069) ve sıfır genişlikli karakterler (U+200B–200F, U+FEFF) ayıklanır;
/// kontrol karakterleri (satır sonu/tab dahil) boşluğa çevrilir ve ardışık boşluklar tekleştirilir; NFC + trim;
/// sonra en fazla <c>maxLength</c> karaktere kesilir (surrogate çifti ortadan bölünmez).
/// </summary>
public static class DisplayTextSanitizer
{
    public static string Clean(string? text, int maxLength)
    {
        if (string.IsNullOrEmpty(text) || maxLength <= 0)
            return string.Empty;

        try
        {
            text = text.Normalize(NormalizationForm.FormC);
        }
        catch (ArgumentException)
        {
            // Eşlenmemiş surrogate: aşağıdaki döngü onları düşürür.
        }

        var sb = new StringBuilder(text.Length);
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    sb.Append(c).Append(text[i + 1]);
                    i++;
                }
                continue; // eşlenmemiş surrogate düşer
            }

            if (char.IsLowSurrogate(c) || IsInvisibleFormatting(c))
                continue;

            if (char.IsControl(c) || char.IsWhiteSpace(c))
            {
                if (sb.Length > 0 && sb[^1] != ' ')
                    sb.Append(' ');
                continue;
            }

            sb.Append(c);
        }

        var cleaned = sb.ToString().Trim();
        if (cleaned.Length <= maxLength)
            return cleaned;

        var cut = maxLength;
        if (char.IsHighSurrogate(cleaned[cut - 1]))
            cut--;
        return cleaned[..cut].TrimEnd();
    }

    private static bool IsInvisibleFormatting(char c) =>
        c is >= '‪' and <= '‮'
            or >= '⁦' and <= '⁩'
            or >= '​' and <= '‏'
            or '﻿';
}
