using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;

namespace ExamApp.Api.Tests.Models;

/// <summary>issue #471: <see cref="QuestionBankQuery"/> doğrulama, sayfa boyutu, kapsam ve kaynak ayrıştırma kuralları.</summary>
public class QuestionBankQueryTests
{
    // ---- IsValid ----

    [Fact]
    public void IsValid_DefaultFilter_ReturnsTrue()
    {
        new QuestionBankQuery().IsValid().ShouldBeTrue();
    }

    [Theory]
    [InlineData("mine")]
    [InlineData("all")]
    [InlineData("ALL")]
    [InlineData("Mine")]
    [InlineData("")]
    [InlineData(null)]
    public void IsValid_KnownOrEmptyScope_ReturnsTrue(string? scope)
    {
        new QuestionBankQuery { Scope = scope }.IsValid().ShouldBeTrue();
    }

    [Theory]
    [InlineData("everyone")]
    [InlineData("own")]
    [InlineData("all ")]
    public void IsValid_UnknownScope_ReturnsFalse(string scope)
    {
        new QuestionBankQuery { Scope = scope }.IsValid().ShouldBeFalse();
    }

    [Theory]
    [InlineData("AI")]
    [InlineData("ai")]
    [InlineData("Human")]
    [InlineData(null)]
    [InlineData("")]
    public void IsValid_KnownOrEmptySource_ReturnsTrue(string? source)
    {
        new QuestionBankQuery { Source = source }.IsValid().ShouldBeTrue();
    }

    [Theory]
    [InlineData("Bogus")]
    [InlineData("5")]   // sayısal değer Enum.TryParse'dan geçer ama tanımlı değil
    [InlineData("0")]
    [InlineData("AI,Human")]
    public void IsValid_UnknownOrNumericSource_ReturnsFalse(string source)
    {
        new QuestionBankQuery { Source = source }.IsValid().ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    [InlineData(-1)]
    public void IsValid_MinDifficultyOutsideOneToTen_ReturnsFalse(int min)
    {
        new QuestionBankQuery { MinDifficulty = min }.IsValid().ShouldBeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(11)]
    public void IsValid_MaxDifficultyOutsideOneToTen_ReturnsFalse(int max)
    {
        new QuestionBankQuery { MaxDifficulty = max }.IsValid().ShouldBeFalse();
    }

    [Fact]
    public void IsValid_DifficultyBoundsOneAndTen_ReturnsTrue()
    {
        new QuestionBankQuery { MinDifficulty = 1, MaxDifficulty = 10 }.IsValid().ShouldBeTrue();
    }

    [Fact]
    public void IsValid_MinGreaterThanMax_ReturnsFalse()
    {
        new QuestionBankQuery { MinDifficulty = 6, MaxDifficulty = 5 }.IsValid().ShouldBeFalse();
    }

    [Fact]
    public void IsValid_MinEqualsMax_ReturnsTrue()
    {
        new QuestionBankQuery { MinDifficulty = 5, MaxDifficulty = 5 }.IsValid().ShouldBeTrue();
    }

    [Fact]
    public void IsValid_NegativeSkip_ReturnsFalse()
    {
        new QuestionBankQuery { Skip = -1 }.IsValid().ShouldBeFalse();
    }

    [Fact]
    public void IsValid_ZeroSkip_ReturnsTrue()
    {
        new QuestionBankQuery { Skip = 0 }.IsValid().ShouldBeTrue();
    }

    [Fact]
    public void IsValid_SubTopicIdsAboveLimit_ReturnsFalse()
    {
        var tooMany = Enumerable.Range(1, QuestionBankQuery.MaxSubTopicIds + 1).ToArray();

        new QuestionBankQuery { SubTopicIds = tooMany }.IsValid().ShouldBeFalse();
    }

    [Fact]
    public void IsValid_SubTopicIdsAtLimit_ReturnsTrue()
    {
        var atLimit = Enumerable.Range(1, QuestionBankQuery.MaxSubTopicIds).ToArray();

        new QuestionBankQuery { SubTopicIds = atLimit }.IsValid().ShouldBeTrue();
    }

    // ---- EffectiveTake ----

    [Theory]
    [InlineData(0)]
    [InlineData(-10)]
    public void EffectiveTake_NonPositiveTake_UsesDefault(int take)
    {
        new QuestionBankQuery { Take = take }.EffectiveTake.ShouldBe(QuestionBankQuery.DefaultTake);
    }

    [Fact]
    public void EffectiveTake_DefaultInstance_IsDefaultTake()
    {
        new QuestionBankQuery().EffectiveTake.ShouldBe(24);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(10, 10)]
    [InlineData(60, 60)]
    [InlineData(61, 60)]
    [InlineData(10_000, 60)]
    public void EffectiveTake_PositiveTake_IsCappedAtMaxTake(int take, int expected)
    {
        new QuestionBankQuery { Take = take }.EffectiveTake.ShouldBe(expected);
    }

    // ---- IsAllScope ----

    [Theory]
    [InlineData("all", true)]
    [InlineData("ALL", true)]
    [InlineData("All", true)]
    [InlineData("mine", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("everything", false)]
    public void IsAllScope_OnlyAllIsTrueCaseInsensitively(string? scope, bool expected)
    {
        new QuestionBankQuery { Scope = scope }.IsAllScope.ShouldBe(expected);
    }

    // ---- ParsedSource ----

    [Theory]
    [InlineData("AI", ClassificationSource.AI)]
    [InlineData("ai", ClassificationSource.AI)]
    [InlineData("Human", ClassificationSource.Human)]
    [InlineData("human", ClassificationSource.Human)]
    public void ParsedSource_KnownValueCaseInsensitively_ReturnsEnum(string source, ClassificationSource expected)
    {
        new QuestionBankQuery { Source = source }.ParsedSource.ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Bogus")]
    public void ParsedSource_EmptyOrUnknown_ReturnsNull(string? source)
    {
        new QuestionBankQuery { Source = source }.ParsedSource.ShouldBeNull();
    }

    // ---- Default page size sabitleri ----

    [Fact]
    public void Constants_DefaultTakeAndMaxTake_MatchContract()
    {
        QuestionBankQuery.DefaultTake.ShouldBe(24);
        QuestionBankQuery.MaxTake.ShouldBe(60);
        QuestionBankQuery.MaxSubTopicIds.ShouldBe(50);
    }

    // ---- Item / page DTO varsayılanları ----

    [Fact]
    public void QuestionBankItemDto_Defaults_HaveEmptySubTopicsAndNullClassification()
    {
        var item = new QuestionBankItemDto();

        item.SubTopics.ShouldNotBeNull();
        item.SubTopics.ShouldBeEmpty();
        item.ClassificationSource.ShouldBeNull();
        item.IsClassified.ShouldBeFalse();
        item.HasCorrectAnswer.ShouldBeFalse();
        item.IsOwn.ShouldBeFalse();
    }

    [Fact]
    public void QuestionBankPageDto_Defaults_HaveEmptyItemsAndZeroTotal()
    {
        var page = new QuestionBankPageDto();

        page.Items.ShouldNotBeNull();
        page.Items.ShouldBeEmpty();
        page.Total.ShouldBe(0);
        page.Skip.ShouldBe(0);
        page.Take.ShouldBe(0);
    }
}
