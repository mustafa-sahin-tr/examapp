using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos;
using ExamApp.Api.Services.TaxonomySearch;
using ExamApp.Api.Tests.Support;

namespace ExamApp.Api.Tests.Services;

/// <summary>Issue #499: Türkçe normalizasyon, sınıf daraltma, puan sırası.</summary>
public sealed class TaxonomySearchServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly int _grade5;
    private readonly int _grade6;
    private readonly int _math5Angles;
    private readonly int _triangle;

    public TaxonomySearchServiceTests()
    {
        using var ctx = _db.NewContext();
        var grade5 = new Grade { Name = "5. Sınıf" };
        var grade6 = new Grade { Name = "6. Sınıf" };
        var math = new Subject { Name = "Matematik" };
        var turkish = new Subject { Name = "Türkçe" };
        var empty = new Subject { Name = "Felsefe" };
        ctx.AddRange(grade5, grade6, math, turkish, empty);
        ctx.SaveChanges();
        var angles5 = new Topic { Name = "Açılar", SubjectId = math.Id, GradeId = grade5.Id };
        var angles6 = new Topic { Name = "Açıların Ölçüsü", SubjectId = math.Id, GradeId = grade6.Id };
        var fractions = new Topic { Name = "Kesirler", SubjectId = math.Id, GradeId = grade5.Id };
        var reading = new Topic { Name = "Okuduğunu Anlama", SubjectId = turkish.Id, GradeId = grade5.Id };
        var waterCycle = new Topic { Name = "Su", SubjectId = turkish.Id, GradeId = grade6.Id };
        ctx.AddRange(angles5, angles6, fractions, reading, waterCycle);
        ctx.SaveChanges();
        var triangle = new SubTopic { Name = "Üçgende Açı", TopicId = angles5.Id };
        var addition = new SubTopic { Name = "Kesirlerde Toplama", TopicId = fractions.Id };
        ctx.AddRange(triangle, addition);
        ctx.SaveChanges();
        (_grade5, _grade6, _math5Angles, _triangle) = (grade5.Id, grade6.Id, angles5.Id, triangle.Id);
    }

    public void Dispose() => _db.Dispose();

    private async Task<List<TaxonomySearchResultDto>> SearchAsync(string query, int? grade = null, int take = 10)
    {
        await using var ctx = _db.NewContext();
        return await new TaxonomySearchService(ctx).SearchAsync(query, grade, take);
    }

    [Theory]
    [InlineData("İĞNE IŞIK Şapka", "igne isik sapka")]
    [InlineData("ÇÖÜ çöü ğş", "cou cou gs")]
    [InlineData("Açılar", "acilar")]
    public void Normalize_folds_Turkish_letters(string text, string expected) =>
        TaxonomySearchService.Normalize(text).ShouldBe(expected);

    [Theory]
    [InlineData("matematik açılar")]
    [InlineData("MATEMATİK AÇILAR")]
    [InlineData("matematik acilar")]
    public async Task Case_and_Turkish_letters_do_not_change_the_result(string query)
    {
        var first = (await SearchAsync(query, _grade5))[0];

        (first.TopicId, first.SubTopicId, first.Path).ShouldBe(((int?)_math5Angles, (int?)null, "Matematik › Açılar"));
        first.GradeName.ShouldBe("5. Sınıf");
    }

    private async Task<int> AddTopicAsync(string name, string subject = "Din Kültürü")
    {
        await using var ctx = _db.NewContext();
        var owner = ctx.Subjects.FirstOrDefault(s => s.Name == subject) ?? new Subject { Name = subject };
        var topic = new Topic { Name = name, Subject = owner, GradeId = _grade5 };
        ctx.Add(topic);
        await ctx.SaveChangesAsync();
        return topic.Id;
    }

    [Theory]
    [InlineData("kuran")]
    [InlineData("kur'an")]
    [InlineData("KUR’AN")]
    public async Task An_apostrophe_does_not_split_a_word(string query)
    {
        var topic = await AddTopicAsync("Kur'an-ı Kerim");

        (await SearchAsync(query, _grade5))[0].TopicId.ShouldBe(topic);
    }

    [Fact]
    public async Task A_suffix_after_an_apostrophe_still_finds_the_name()
    {
        var topic = await AddTopicAsync("Türkiye Coğrafyası");

        (await SearchAsync("türkiye'nin", _grade5))[0].TopicId.ShouldBe(topic);
    }

    [Fact]
    public async Task Words_late_in_a_long_name_can_be_found()
    {
        var topic = await AddTopicAsync("bir iki uc dort bes alti yedi sekiz dokuz onuncukelime");

        (await SearchAsync("onuncukelime", _grade5))[0].TopicId.ShouldBe(topic);
    }

    [Fact]
    public async Task One_word_in_both_subject_and_topic_does_not_outrank_the_subject()
    {
        await AddTopicAsync("Matematik Problemleri", "Matematik");

        (await SearchAsync("matematik", _grade5))[0].Path.ShouldBe("Matematik");
    }

    [Fact]
    public async Task The_student_grade_is_read_for_the_default()
    {
        await using var ctx = _db.NewContext();
        ctx.Add(new Student { UserId = 901, StudentNumber = "S901", SchoolName = "s", GradeId = _grade6 });
        await ctx.SaveChangesAsync();
        var service = new TaxonomySearchService(ctx);

        (await service.GetStudentGradeIdAsync(901)).ShouldBe(_grade6);
        (await service.GetStudentGradeIdAsync(902)).ShouldBeNull();
    }

    [Fact]
    public async Task A_short_prefix_finds_the_same_topic()
    {
        var results = await SearchAsync("aci", _grade5);

        results[0].TopicId.ShouldBe(_math5Angles);
        results.ShouldContain(r => r.SubTopicId == _triangle);
    }

    [Fact]
    public async Task An_inflected_word_finds_the_name()
    {
        (await SearchAsync("açıları", _grade5))[0].TopicId.ShouldBe(_math5Angles);
        (await SearchAsync("açıların", _grade6))[0].Path.ShouldBe("Matematik › Açıların Ölçüsü");
    }

    [Fact]
    public async Task A_short_name_is_not_taken_for_the_stem_of_a_longer_word()
    {
        (await SearchAsync("sulama", _grade6)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Topics_under_a_match_are_not_listed_for_their_parent_alone()
    {
        // "Üçgende Açı" alt konusu Açılar'ın altında ama adında "açılar" geçmiyor: üst konusu yüzünden listelenmez.
        (await SearchAsync("açılar", _grade5)).ShouldHaveSingleItem().Path.ShouldBe("Matematik › Açılar");

        // Adı gerçekten eşleşen alt konu listelenir, konunun ardından.
        (await SearchAsync("kesirler", _grade5)).Select(r => r.Path).ShouldBe(["Matematik › Kesirler", "Matematik › Kesirler › Kesirlerde Toplama"]);
    }

    [Fact]
    public async Task Subject_alone_comes_before_anything_else_and_only_once_per_grade()
    {
        var results = await SearchAsync("matematik");

        results.Select(r => r.GradeId).ShouldBe([_grade5, _grade6]);
        results.ShouldAllBe(r => r.TopicId == null && r.Path == "Matematik");
    }

    [Fact]
    public async Task A_subject_without_topics_is_not_a_result()
    {
        (await SearchAsync("felsefe")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Subject_and_topic_together_score_higher_than_the_topic_alone()
    {
        var together = (await SearchAsync("matematik açılar", _grade5))[0].Score;
        var alone = (await SearchAsync("açılar", _grade5))[0].Score;

        together.ShouldBeGreaterThan(alone);
    }

    [Fact]
    public async Task Every_word_must_appear_in_the_path()
    {
        (await SearchAsync("türkçe açılar", _grade5)).ShouldBeEmpty();
    }

    [Fact]
    public async Task Grade_narrows_the_results_and_no_grade_searches_all()
    {
        (await SearchAsync("açı", _grade6)).ShouldAllBe(r => r.GradeId == _grade6);
        var all = await SearchAsync("açı");
        all.Select(r => r.GradeId).Distinct().Count().ShouldBe(2);
    }

    [Fact]
    public async Task Take_limits_the_results()
    {
        (await SearchAsync("a", take: 1)).Count.ShouldBe(1);
    }

    [Fact]
    public async Task A_deleted_topic_is_not_found()
    {
        await using (var ctx = _db.NewContext())
        {
            ctx.Topics.Remove(ctx.Topics.Single(t => t.Id == _math5Angles));
            await ctx.SaveChangesAsync();
        }

        (await SearchAsync("açılar", _grade5)).ShouldBeEmpty();
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("  a ", false)]
    [InlineData("!!", false)]
    [InlineData("a.", false)]
    [InlineData("a b", true)]
    [InlineData("bir iki uc dort bes alti yedi sekiz", true)]
    [InlineData("bir iki uc dort bes alti yedi sekiz dokuz", false)]
    [InlineData("aç", true)]
    [InlineData("  matematik  ", true)]
    public void Query_rules(string? query, bool valid) =>
        TaxonomySearchService.IsValidQuery(query).ShouldBe(valid);

    [Fact]
    public void A_very_long_query_is_refused() =>
        TaxonomySearchService.IsValidQuery(new string('a', TaxonomySearchService.MaxQueryLength + 1)).ShouldBeFalse();
}
