using System.Globalization;
using System.Text;

namespace ExamApp.Foundation.Text;

/// <summary>
/// Turkish-aware search text: one normalisation for every in-memory search (taxonomy search #499 in the exam API, the
/// assistant's help search #506). Matching is done in code, not left to a DB collation or the <c>unaccent</c> extension.
/// </summary>
public static class SearchText
{
    /// <summary>"açıları" finds "Açılar"; short words are not stems ("su" searches for "su", not "sulama").</summary>
    public const int MinStemLength = 4;

    /// <summary>Lower case, Turkish and other accents removed (İ, I, ı → i; ş → s; ğ → g; ç → c; ö → o; ü → u).</summary>
    public static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var c in text.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            builder.Append(c == 'ı' ? 'i' : char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    /// <summary>Distinct normalised words; letters and digits only.</summary>
    public static string[] Tokenize(string text)
    {
        var tokens = new List<string>();
        var current = new StringBuilder();
        foreach (var c in Normalize(text))
        {
            // An apostrophe does not split a word: "Kur'an" → "kuran", "Türkiye'nin" → "turkiyenin".
            if (c is '\'' or '’' or 'ʼ')
                continue;

            if (char.IsLetterOrDigit(c))
            {
                current.Append(c);
                continue;
            }

            if (current.Length > 0)
                tokens.Add(current.ToString());
            current.Clear();
        }

        if (current.Length > 0)
            tokens.Add(current.ToString());
        return tokens.Distinct().ToArray();
    }

    /// <returns>2 the same word, 1 a prefix or a suffixed form ("veli" ↔ "velimi"), 0 no match.</returns>
    public static int MatchOf(IEnumerable<string> nameTokens, string query)
    {
        var result = 0;
        foreach (var name in nameTokens)
        {
            if (name == query)
                return 2;
            if (name.StartsWith(query, StringComparison.Ordinal)
                || (name.Length >= MinStemLength && query.StartsWith(name, StringComparison.Ordinal)))
                result = 1;
        }

        return result;
    }
}
