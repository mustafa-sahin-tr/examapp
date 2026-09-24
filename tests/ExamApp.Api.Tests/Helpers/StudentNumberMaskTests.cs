using ExamApp.Api.Helpers;

namespace ExamApp.Api.Tests.Helpers;

/// <summary>
/// Issue #262: admin öğrenci listesinde öğrenci numarası kısmi — yalnızca son 4 karakter; ≤ 4 karakter tamamen gizli;
/// önek sabit uzunlukta (numaranın uzunluğu sızmaz).
/// </summary>
public class StudentNumberMaskTests
{
    [Theory]
    [InlineData("20241234", "****1234")]
    [InlineData("12345", "****2345")]
    [InlineData("  A-2024-0042  ", "****0042")] // kırpılır
    [InlineData("123456789012345", "****2345")] // uzunluk sızmaz
    public void Shows_only_the_last_four_characters(string input, string expected)
        => StudentNumberMask.Apply(input).ShouldBe(expected);

    [Theory]
    [InlineData("1")]
    [InlineData("12")]
    [InlineData("1234")]
    [InlineData(" 1234 ")]
    public void Four_or_fewer_characters_are_fully_masked(string input)
        => StudentNumberMask.Apply(input).ShouldBe("****");

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Missing_number_stays_empty_string(string? input)
        => StudentNumberMask.Apply(input).ShouldBe(string.Empty);

    [Fact]
    public void Suffix_does_not_split_a_surrogate_pair()
        => StudentNumberMask.Apply("12345\U0001F600abc").ShouldBe("****abc");
}
