namespace ExamApp.Api.Services.Labeling;

/// <summary>
/// Issue #1183: çoktan seçmeli şıkların harfleri. Şıkkın harfi sırasıdır (0 → A); Claude'un okuduğu harf tam ve tekil bir
/// takımsa (A, B, C, … boşluksuz) sıra ondan alınır, aksi halde konumdan.
/// </summary>
public static class OptionLetters
{
    public const string Letters = "ABCDE";

    /// <summary>"A)", "b.", " (C) " → "A", "B", "C"; harf yoksa ya da A–E dışındaysa null.</summary>
    public static string? Normalize(string? raw)
    {
        var letter = raw?.Trim().TrimStart('(').FirstOrDefault() ?? '\0';
        var upper = char.ToUpperInvariant(letter);
        return Letters.Contains(upper) ? upper.ToString() : null;
    }

    /// <summary>Sıradaki harf (0 → "A"); A–E dışındaki sırada null.</summary>
    public static string? At(int index) => index >= 0 && index < Letters.Length ? Letters[index].ToString() : null;

    /// <summary>Harfler <c>A</c>'dan başlayıp boşluksuz ve tekil mi (ör. C, A, B → evet; A, A, B veya A, C → hayır).</summary>
    public static bool IsCompleteSet(IReadOnlyList<string?> letters) =>
        letters.Count > 0 && letters.Count <= Letters.Length
        && letters.OrderBy(l => l, StringComparer.Ordinal).SequenceEqual(Letters[..letters.Count].Select(c => c.ToString()));
}
