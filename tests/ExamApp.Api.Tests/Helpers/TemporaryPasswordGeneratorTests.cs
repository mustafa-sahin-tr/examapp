using ExamApp.Api.Helpers;

namespace ExamApp.Api.Tests.Helpers;

/// <summary>Issue #156: geçici şifre üretici — uzunluk, sınıflar, belirsiz karakter yok, tekrar yok.</summary>
public class TemporaryPasswordGeneratorTests
{
    private static readonly char[] Ambiguous = ['0', 'O', 'o', '1', 'l', 'I'];

    [Fact]
    public void Default_is_16_chars_with_every_class_and_only_allowed_characters()
    {
        var allowed = TemporaryPasswordGenerator.Upper + TemporaryPasswordGenerator.Lower
                      + TemporaryPasswordGenerator.Digits + TemporaryPasswordGenerator.Symbols;

        for (var i = 0; i < 2_000; i++)
        {
            var generated = TemporaryPasswordGenerator.Generate();

            generated.Length.ShouldBe(16);
            generated.ShouldContain(c => TemporaryPasswordGenerator.Upper.Contains(c));
            generated.ShouldContain(c => TemporaryPasswordGenerator.Lower.Contains(c));
            generated.ShouldContain(c => TemporaryPasswordGenerator.Digits.Contains(c));
            generated.ShouldContain(c => TemporaryPasswordGenerator.Symbols.Contains(c));
            generated.ShouldAllBe(c => allowed.Contains(c));
            generated.IndexOfAny(Ambiguous).ShouldBe(-1);
        }
    }

    [Fact]
    public void Alphabets_exclude_ambiguous_characters()
    {
        var all = TemporaryPasswordGenerator.Upper + TemporaryPasswordGenerator.Lower
                  + TemporaryPasswordGenerator.Digits + TemporaryPasswordGenerator.Symbols;
        all.IndexOfAny(Ambiguous).ShouldBe(-1);
        all.ShouldNotContain(' ');
        all.ShouldNotContain('"');
        all.ShouldNotContain('\'');
        all.ShouldNotContain('\\');
    }

    [Fact]
    public void Values_are_unique_and_class_positions_are_shuffled()
    {
        var values = Enumerable.Range(0, 5_000).Select(_ => TemporaryPasswordGenerator.Generate()).ToList();

        values.Distinct().Count().ShouldBe(values.Count);
        // Zorunlu karakterler hep ilk 4 konumda kalsaydı ilk karakter her zaman büyük harf olurdu.
        values.Select(v => v[0]).ShouldContain(c => !TemporaryPasswordGenerator.Upper.Contains(c));
    }

    [Theory]
    [InlineData(4)]
    [InlineData(32)]
    public void Honors_custom_length(int length)
        => TemporaryPasswordGenerator.Generate(length).Length.ShouldBe(length);

    [Fact]
    public void Rejects_length_below_class_count()
        => Should.Throw<ArgumentOutOfRangeException>(() => TemporaryPasswordGenerator.Generate(3));
}
