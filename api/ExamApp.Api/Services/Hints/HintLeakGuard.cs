using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ExamApp.Api.Services.Hints;

/// <summary>
/// Issue #1113: Claude'un yazdığı ipucu adımı cevabı açık ediyor mu? Doğru şıkkın metni (yeterince uzunsa) ya da "cevap / doğru şık B"
/// kalıbı geçen adım düşer. Kaba bir ağdır; asıl yasak istemdedir.
/// </summary>
public static partial class HintLeakGuard
{
    /// <summary>Bundan kısa şık metni ("3", "12") ipucunda doğal olarak geçebilir; metin eşleşmesi aranmaz.</summary>
    private const int MinComparableLength = 4;

    [GeneratedRegex(@"\b(cevap|yanıt|doğru\s+(şık|seçenek)|answer|option)\w*\s*(:|=|olarak|is)?\s*\(?([A-E])\)?(?![\p{L}\d])",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AnswerLetter();

    public static bool Leaks(string step, string correctLetter, string? correctText)
    {
        var match = AnswerLetter().Match(step);
        if (match.Success && string.Equals(match.Groups[4].Value, correctLetter, StringComparison.OrdinalIgnoreCase))
            return true;

        var answer = Normalize(correctText);
        return answer.Length >= MinComparableLength && Normalize(step).Contains(answer, StringComparison.Ordinal);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;
        var text = value.ToLower(new CultureInfo("tr-TR"));
        var builder = new StringBuilder(text.Length);
        var space = false;
        foreach (var c in text)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(c);
                space = false;
            }
            else if (!space && builder.Length > 0)
            {
                builder.Append(' ');
                space = true;
            }
        }
        return builder.ToString().Trim();
    }
}
