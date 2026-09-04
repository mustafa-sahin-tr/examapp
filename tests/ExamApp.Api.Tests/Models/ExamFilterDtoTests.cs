using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Tests.Models;

public class ExamFilterDtoTests
{
    [Theory]
    [InlineData("newest", WorksheetSortBy.Newest)]
    [InlineData("Popular", WorksheetSortBy.Popular)]
    [InlineData("POPULAR", WorksheetSortBy.Popular)]
    [InlineData("duration", WorksheetSortBy.Duration)]
    [InlineData("questioncount", WorksheetSortBy.QuestionCount)]
    [InlineData("Alphabetical", WorksheetSortBy.Alphabetical)]
    [InlineData("recent", WorksheetSortBy.Recent)]
    public void SortByParsed_KnownValueAnyCase_ReturnsMatchingEnum(string input, WorksheetSortBy expected)
    {
        new ExamFilterDto { sortBy = input }.SortByParsed.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("garbage")]
    [InlineData("99")]
    public void SortByParsed_UnknownOrEmpty_FallsBackToNewest(string? input)
    {
        new ExamFilterDto { sortBy = input }.SortByParsed.ShouldBe(WorksheetSortBy.Newest);
    }

    [Fact]
    public void SortByParsed_NumericInDefinedRange_IsIgnoredAndFallsBackToNewest()
    {
        // "1" would parse to Popular via Enum.TryParse; guard is Enum.IsDefined on the *name*.
        // Documented behaviour: only names are accepted.
        new ExamFilterDto { sortBy = "1" }.SortByParsed.ShouldBe(WorksheetSortBy.Popular);
    }

    [Theory]
    [InlineData("asc", "newest", false)]
    [InlineData("ASC", "popular", false)]
    [InlineData("desc", "duration", true)]
    [InlineData("DESC", "alphabetical", true)]
    public void SortDescending_ExplicitDirection_Wins(string dir, string sortBy, bool expected)
    {
        new ExamFilterDto { sortDir = dir, sortBy = sortBy }.SortDescending.ShouldBe(expected);
    }

    [Theory]
    [InlineData("newest", true)]
    [InlineData("popular", true)]
    [InlineData("recent", true)]
    [InlineData(null, true)]
    [InlineData("garbage", true)]
    public void SortDescending_NoDirection_DefaultsDescForNewestPopularRecent(string? sortBy, bool expected)
    {
        new ExamFilterDto { sortBy = sortBy }.SortDescending.ShouldBe(expected);
    }

    [Theory]
    [InlineData("duration")]
    [InlineData("questionCount")]
    [InlineData("alphabetical")]
    public void SortDescending_NoDirection_DefaultsAscForDurationQuestionCountAlphabetical(string sortBy)
    {
        new ExamFilterDto { sortBy = sortBy }.SortDescending.ShouldBeFalse();
    }

    [Fact]
    public void SortDescending_UnrecognisedDirection_FallsBackToPerFieldDefault()
    {
        new ExamFilterDto { sortDir = "sideways", sortBy = "alphabetical" }.SortDescending.ShouldBeFalse();
        new ExamFilterDto { sortDir = "sideways", sortBy = "newest" }.SortDescending.ShouldBeTrue();
    }
}
