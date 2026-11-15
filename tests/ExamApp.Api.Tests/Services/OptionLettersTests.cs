using ExamApp.Api.Services.Labeling;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #1183: şık harfleri — Claude'un okuduğu etiketin normalleştirilmesi ve tam takım kontrolü.</summary>
public class OptionLettersTests
{
    [Theory]
    [InlineData("A)", "A")]
    [InlineData(" b. ", "B")]
    [InlineData("(C)", "C")]
    [InlineData("E", "E")]
    [InlineData("F)", null)]
    [InlineData("Şık 1", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void Normalize_KeepsOnlyALetterFromAToE(string? raw, string? expected) =>
        OptionLetters.Normalize(raw).ShouldBe(expected);

    [Fact]
    public void IsCompleteSet_NeedsDistinctLettersFromAWithoutGaps()
    {
        OptionLetters.IsCompleteSet(["C", "A", "B"]).ShouldBeTrue();
        OptionLetters.IsCompleteSet(["A", "A", "B"]).ShouldBeFalse();
        OptionLetters.IsCompleteSet(["A", "C"]).ShouldBeFalse();
        OptionLetters.IsCompleteSet(["A", null]).ShouldBeFalse();
        OptionLetters.IsCompleteSet([]).ShouldBeFalse();
    }
}
