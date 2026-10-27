using System.Text;
using System.Text.RegularExpressions;

namespace ExamApp.Api.Services.Interactions;

/// <summary>
/// issue #582: <c>textSelect</c> metin satırlarını token'lara böler (saf, durumsuz). Claude satır düzeyinde döner (metin + satır
/// kutusu); token kutuları burada hesaplanır. Aynı kural TS'de de var (yazarın elle "satır ekle"si); paylaşılan fixture
/// <c>shared/fixtures/text-tokenize.v1.json</c> ikisini de kilitler.
/// <list type="number">
/// <item>Metin: NFC; <see cref="SlotAnswerNormalizer.WhitespaceClass"/> dizileri tek boşluk; baş/son boşluk kırpılır.</item>
/// <item>Parçalar (soldan sağa, <see cref="PiecePatternSource"/>): <c>( )</c> / <c>()</c> boşluk (gap, metni <c>"( )"</c>),
/// ya da boşluk ve gap içermeyen en uzun karakter dizisi (noktalama kelimeye yapışık kalır; <c>yaptın( )</c> → <c>yaptın</c> +
/// gap).</item>
/// <item>Kutu: satır kutusu içinde karakter konumuna oranlı yatay dilim (boşluklar dahil). Normalize metnin uzunluğu L, parça
/// [s, e) ise <c>x = X + floor(W·s/L)</c>, <c>sağ = X + floor(W·e/L)</c>, <c>genişlik = max(1, sağ − x)</c>; y ve yükseklik satırın.
/// Satır kutusu tam sayı piksel olmalı (TS tarafı önce yuvarlar).</item>
/// <item>Satır sonu tiresi: satırın son parçası gap değilse ve harf + <c>-</c> ile bitiyorsa, HEMEN sonraki satırın ilk parçası
/// (gap değilse) ona eklenir: metin tiresiz birleşir (<c>dolaş-</c> + <c>mış</c> → <c>dolaşmış</c>), kutular sırayla eklenir,
/// <c>line</c> ilk parçanınki. Zincirlenebilir, en çok <see cref="MaxBoxes"/> kutu. Boş satır zinciri keser.</item>
/// <item>Id'ler okuma sırasıyla: kelimeler <c>t1, t2…</c>, gap'ler <c>g1, g2…</c>. <c>line</c> = girdi satırının 0 tabanlı sırası
/// (boş satır sırayı tüketir, token üretmez).</item>
/// </list>
/// Sınır (token sayısı, metin uzunluğu) uygulanmaz; çağıran keser, kontrolcü doğrular.
/// </summary>
public static class TextSelectTokenizer
{
    public const string GapText = "( )";

    /// <summary>Bir token'ın en çok kutu sayısı (tirelenmiş kelime en çok üç satıra yayılır).</summary>
    public const int MaxBoxes = 3;

    /// <summary>Parça deseni; paylaşılan fixture'ın <c>$comment</c>'inde birebir yazılıdır (testle kilitli).</summary>
    public const string PiecePatternSource = @"\( ?\)|(?:(?!\( ?\))[^ ])+";

    private static readonly Regex Piece = new(PiecePatternSource, RegexOptions.CultureInvariant);
    private static readonly Regex Gap = new(@"^\( ?\)$", RegexOptions.CultureInvariant);
    private static readonly Regex WhitespaceRun = new(SlotAnswerNormalizer.WhitespaceClass + "+", RegexOptions.CultureInvariant);

    /// <summary>Tam sayı piksel kutu (soru bölgesine göre göreli).</summary>
    public readonly record struct TokenBox(int X, int Y, int Width, int Height);

    /// <summary>Girdi satırı: basılı metin (aynen) ve satır kutusu.</summary>
    public sealed record Line(string? Text, TokenBox Box);

    public sealed record Token(string Id, string Text, int Line, bool Gap, IReadOnlyList<TokenBox> Boxes);

    /// <summary>Tokenizer'ın metin ön işlemesi (NFC + boşluk dizileri tek boşluk + kırpma).</summary>
    public static string NormalizeLine(string? text)
        => string.IsNullOrEmpty(text) ? string.Empty : WhitespaceRun.Replace(text.Normalize(NormalizationForm.FormC), " ").Trim(' ');

    public static IReadOnlyList<Token> Tokenize(IReadOnlyList<Line> lines)
    {
        var drafts = new List<Draft>();
        Draft? open = null;
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            var text = NormalizeLine(line.Text);
            var pieces = Piece.Matches(text);
            for (var j = 0; j < pieces.Count; j++)
            {
                var piece = pieces[j];
                var gap = Gap.IsMatch(piece.Value);
                var box = Slice(line.Box, text.Length, piece.Index, piece.Index + piece.Length);
                if (j == 0 && open != null && !gap)
                {
                    open.Text = open.Text[..^1] + piece.Value;
                    open.Boxes.Add(box);
                    continue;
                }
                drafts.Add(new Draft(gap ? GapText : piece.Value, lineIndex, gap, [box]));
            }

            open = null;
            if (pieces.Count > 0 && drafts[^1] is { Gap: false } last && last.Boxes.Count < MaxBoxes && EndsWithLineBreakHyphen(last.Text))
                open = last;
        }

        var tokens = new List<Token>(drafts.Count);
        int words = 0, gaps = 0;
        foreach (var d in drafts)
            tokens.Add(new Token(d.Gap ? $"g{++gaps}" : $"t{++words}", d.Text, d.Line, d.Gap, d.Boxes));
        return tokens;
    }

    private static bool EndsWithLineBreakHyphen(string text) => text.Length >= 2 && text[^1] == '-' && char.IsLetter(text[^2]);

    private static TokenBox Slice(TokenBox line, int length, int start, int end)
    {
        if (length <= 0)
            return line;
        var left = line.X + (int)((long)line.Width * start / length);
        var right = line.X + (int)((long)line.Width * end / length);
        return new TokenBox(left, line.Y, Math.Max(1, right - left), line.Height);
    }

    private sealed class Draft(string text, int line, bool gap, List<TokenBox> boxes)
    {
        public string Text { get; set; } = text;
        public int Line { get; } = line;
        public bool Gap { get; } = gap;
        public List<TokenBox> Boxes { get; } = boxes;
    }
}
