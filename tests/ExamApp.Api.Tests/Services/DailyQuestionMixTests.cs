using ExamApp.Api.Services.Practice;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #697: günlük set tekrar sorularını, yeni soruları ve başka konuları karıştırır.</summary>
public class DailyQuestionMixTests
{
    private static List<DailyPoolQuestion> Pool(int count, int? topicId = null, int start = 1) =>
        Enumerable.Range(start, count).Select(i => new DailyPoolQuestion(i, topicId)).ToList();

    private static readonly HashSet<int> NoRecent = new();

    [Fact]
    public void DueQuestions_ComeUpToTheShare()
    {
        var ids = DailyQuestionSetService.SelectMixedQuestions(Pool(20), NoRecent, [3, 4, 5], dueShare: 2, null, count: 5, seed: 1);

        ids.Count.ShouldBe(5);
        ids.ShouldContain(3);
        ids.ShouldContain(4);
        ids.ShouldNotContain(5);
    }

    [Fact]
    public void NoDueQuestions_SetIsStillComplete()
    {
        var ids = DailyQuestionSetService.SelectMixedQuestions(Pool(20), NoRecent, [], dueShare: 2, null, count: 5, seed: 1);

        ids.Count.ShouldBe(5);
        ids.Distinct().Count().ShouldBe(5);
    }

    [Fact]
    public void DueQuestion_ReturnsEvenIfAnsweredRecently()
    {
        var recent = new HashSet<int> { 3 };
        var ids = DailyQuestionSetService.SelectMixedQuestions(Pool(20), recent, [3], dueShare: 2, null, count: 5, seed: 1);

        ids.ShouldContain(3);
    }

    [Fact]
    public void RecentlyAnswered_NotDue_StaysDeprioritised()
    {
        var recent = new HashSet<int> { 1, 2 };
        var ids = DailyQuestionSetService.SelectMixedQuestions(Pool(10), recent, [], dueShare: 2, null, count: 5, seed: 1);

        ids.ShouldNotContain(1);
        ids.ShouldNotContain(2);
    }

    [Fact]
    public void LastPractisedTopic_IsUsedOnlyWhenOthersRunOut()
    {
        var pool = Pool(3, topicId: 7).Concat(Pool(3, topicId: 8, start: 10)).ToList();

        var ids = DailyQuestionSetService.SelectMixedQuestions(pool, NoRecent, [], dueShare: 2, lastTopicId: 7, count: 3, seed: 1);
        ids.ShouldBe([10, 11, 12], ignoreOrder: true);

        var more = DailyQuestionSetService.SelectMixedQuestions(pool, NoRecent, [], dueShare: 2, lastTopicId: 7, count: 5, seed: 1);
        more.Count.ShouldBe(5);
    }

    [Fact]
    public void DueQuestionOutsideThePool_IsIgnored()
    {
        var ids = DailyQuestionSetService.SelectMixedQuestions(Pool(5), NoRecent, [99], dueShare: 2, null, count: 3, seed: 1);

        ids.ShouldNotContain(99);
        ids.Count.ShouldBe(3);
    }

    [Fact]
    public void SameSeed_GivesTheSameSet()
    {
        var a = DailyQuestionSetService.SelectMixedQuestions(Pool(20), NoRecent, [3], dueShare: 2, null, count: 5, seed: 42);
        var b = DailyQuestionSetService.SelectMixedQuestions(Pool(20), NoRecent, [3], dueShare: 2, null, count: 5, seed: 42);

        a.ShouldBe(b);
    }
}
