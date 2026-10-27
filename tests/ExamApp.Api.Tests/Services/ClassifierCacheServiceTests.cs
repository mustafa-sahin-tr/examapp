using System.Net;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.Classifier;
using ExamApp.Api.Services.Taxonomy;
using ExamApp.Api.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Tests.Services;

public class ClassifierCacheServiceTests : IDisposable
{
    private readonly TestDb _db = TestDb.Create();
    private readonly ITaxonomyService _taxonomy = Substitute.For<ITaxonomyService>();

    private static readonly string GoodGeminiBody =
        """{ "name": "cachedContents/abc123", "model": "models/x" }""";

    public ClassifierCacheServiceTests() => SetTaxonomy(subTopics: 3);

    private void SetTaxonomy(int subTopics)
    {
        var tree = new TaxonomyTreeDto
        {
            Grades = { new TaxonomyGradeDto { Id = 1, Name = "3" } },
            Subjects =
            {
                new TaxonomySubjectDto
                {
                    Id = 10, Name = "Mat",
                    Topics =
                    {
                        new TaxonomyTopicDto
                        {
                            Id = 20, Name = "Toplama", GradeId = 1, GradeName = "3",
                            SubTopics = Enumerable.Range(1, subTopics)
                                .Select(i => new TaxonomySubTopicDto { Id = 100 + i, Name = $"st{i}", TopicId = 20 })
                                .ToList(),
                        },
                    },
                },
            },
        };
        _taxonomy.GetTreeAsync(Arg.Any<int?>(), Arg.Any<bool>(), Arg.Any<CancellationToken>()).Returns(tree);
    }

    private ClassifierCacheService NewService(
        AppDbContext ctx, IHttpClientFactory http, string apiKey = "test-key")
        => new(ctx, _taxonomy, http,
            Options.Create(new GeminiCacheOptions { ApiKey = apiKey, Model = "models/def", BaseUrl = "https://g" }),
            NullLogger<ClassifierCacheService>.Instance);

    [Fact]
    public async Task Status_is_stale_and_empty_before_the_first_refresh()
    {
        await using var ctx = _db.NewContext();
        var status = await NewService(ctx, new StubHttp(HttpStatusCode.OK, GoodGeminiBody)).GetStatusAsync();

        status.Stale.ShouldBeTrue();
        status.CachedContentName.ShouldBeNull();
        status.ConfiguredInSettings.ShouldBeTrue();
    }

    [Fact]
    public async Task Status_reports_missing_api_key()
    {
        await using var ctx = _db.NewContext();
        var status = await NewService(ctx, new StubHttp(HttpStatusCode.OK, GoodGeminiBody), apiKey: "").GetStatusAsync();
        status.ConfiguredInSettings.ShouldBeFalse();
    }

    [Fact]
    public async Task Refresh_fails_without_an_api_key()
    {
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx, new StubHttp(HttpStatusCode.OK, GoodGeminiBody), apiKey: " ").RefreshAsync(userId: 1);

        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("API anahtar");
    }

    [Fact]
    public async Task Refresh_fails_on_an_empty_taxonomy()
    {
        SetTaxonomy(subTopics: 0);
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx, new StubHttp(HttpStatusCode.OK, GoodGeminiBody)).RefreshAsync(userId: 1);

        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("boş");
    }

    [Fact]
    public async Task Refresh_calls_gemini_and_persists_the_pointer()
    {
        var http = new StubHttp(HttpStatusCode.OK, GoodGeminiBody);
        await using (var ctx = _db.NewContext())
        {
            var result = await NewService(ctx, http).RefreshAsync(userId: 42);

            result.Success.ShouldBeTrue();
            result.CachedContentName.ShouldBe("cachedContents/abc123");
            result.SubTopicCount.ShouldBe(3);
        }

        var sent = http.Requests.ShouldHaveSingleItem();
        sent.RequestUri!.ToString().ShouldEndWith("/cachedContents");
        sent.RequestUri!.ToString().ShouldNotContain("key=");
        sent.Headers.GetValues("x-goog-api-key").ShouldBe(new[] { "test-key" });

        await using var check = _db.NewContext();
        var row = await check.ClassifierCacheConfigs.FindAsync(ClassifierCacheConfig.SingletonId);
        row.ShouldNotBeNull();
        row!.CachedContentName.ShouldBe("cachedContents/abc123");
        row.Model.ShouldBe("models/def");
        row.SubTopicCount.ShouldBe(3);
        row.RefreshedByUserId.ShouldBe(42);
        row.TaxonomyHash.ShouldNotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Refresh_surfaces_a_gemini_error()
    {
        var http = new StubHttp(HttpStatusCode.BadRequest, """{"error":"cache too small"}""");
        await using var ctx = _db.NewContext();
        var result = await NewService(ctx, http).RefreshAsync(userId: 1);

        result.Success.ShouldBeFalse();
        result.Message.ShouldContain("400");
    }

    [Fact]
    public async Task Status_is_current_right_after_a_refresh_then_stale_again_when_taxonomy_changes()
    {
        var http = new StubHttp(HttpStatusCode.OK, GoodGeminiBody);
        await using (var ctx = _db.NewContext())
            await NewService(ctx, http).RefreshAsync(userId: 1);

        await using (var ctx = _db.NewContext())
            (await NewService(ctx, http).GetStatusAsync()).Stale.ShouldBeFalse();

        SetTaxonomy(subTopics: 5); // taxonomy drifted
        await using (var ctx = _db.NewContext())
            (await NewService(ctx, http).GetStatusAsync()).Stale.ShouldBeTrue();
    }

    [Fact]
    public async Task RefreshIfStale_is_a_no_op_when_the_cache_is_current()
    {
        var http = new StubHttp(HttpStatusCode.OK, GoodGeminiBody);
        await using (var ctx = _db.NewContext())
            await NewService(ctx, http).RefreshAsync(userId: 1);
        var callsAfterFirstRefresh = http.Requests.Count;

        await using (var ctx = _db.NewContext())
            await NewService(ctx, http).RefreshIfStaleAsync(userId: 0);

        http.Requests.Count.ShouldBe(callsAfterFirstRefresh); // no second Gemini call
    }

    [Fact]
    public async Task RefreshIfStale_rebuilds_when_stale()
    {
        var http = new StubHttp(HttpStatusCode.OK, GoodGeminiBody);
        await using var ctx = _db.NewContext();
        await NewService(ctx, http).RefreshIfStaleAsync(userId: 0);

        http.Requests.ShouldNotBeEmpty();
        (await _db.NewContext().ClassifierCacheConfigs.FindAsync(ClassifierCacheConfig.SingletonId))
            .ShouldNotBeNull();
    }

    [Fact]
    public async Task ActivePointer_falls_back_to_the_configured_model_when_unset()
    {
        await using var ctx = _db.NewContext();
        var (name, model) = await NewService(ctx, new StubHttp(HttpStatusCode.OK, GoodGeminiBody)).GetActivePointerAsync();

        name.ShouldBeNull();
        model.ShouldBe("models/def");
    }

    // ---- Issue #565: classifier taxonomy filtered by grade (real TaxonomyService over SQLite) ----

    private sealed record GradeTax(int Grade3, int Grade4, int MathId, int ScienceId, int Math3SubTopic, int Math4SubTopic);

    /// <summary>
    /// Matematik linked to grades 3 and 4 with a same-named "Kesirler" topic in each; Fen linked only to grade 4.
    /// </summary>
    private async Task<GradeTax> SeedGradeTaxonomyAsync()
    {
        await using var ctx = _db.NewContext();
        var g3 = new Grade { Name = "3" };
        var g4 = new Grade { Name = "4" };
        var math = new Subject { Name = "Matematik" };
        var science = new Subject { Name = "Fen" };
        ctx.AddRange(g3, g4, math, science);
        await ctx.SaveChangesAsync();
        ctx.GradeSubjects.AddRange(
            new GradeSubject { SubjectId = math.Id, GradeId = g3.Id },
            new GradeSubject { SubjectId = math.Id, GradeId = g4.Id },
            new GradeSubject { SubjectId = science.Id, GradeId = g4.Id });
        var math3 = new Topic { Name = "Kesirler", SubjectId = math.Id, GradeId = g3.Id };
        var math4 = new Topic { Name = "Kesirler", SubjectId = math.Id, GradeId = g4.Id };
        var sci4 = new Topic { Name = "Kuvvet", SubjectId = science.Id, GradeId = g4.Id };
        ctx.Topics.AddRange(math3, math4, sci4);
        await ctx.SaveChangesAsync();
        var st3 = new SubTopic { Name = "Basit kesirler", TopicId = math3.Id };
        var st4 = new SubTopic { Name = "Basit kesirler", TopicId = math4.Id };
        var stSci = new SubTopic { Name = "Sürtünme", TopicId = sci4.Id };
        ctx.SubTopics.AddRange(st3, st4, stSci);
        await ctx.SaveChangesAsync();
        return new GradeTax(g3.Id, g4.Id, math.Id, science.Id, st3.Id, st4.Id);
    }

    private ClassifierCacheService NewServiceWithRealTaxonomy(AppDbContext ctx)
        => new(ctx, new TaxonomyService(ctx, Substitute.For<Hangfire.IBackgroundJobClient>()),
            new StubHttp(HttpStatusCode.OK, GoodGeminiBody),
            Options.Create(new GeminiCacheOptions { ApiKey = "k", Model = "models/def", BaseUrl = "https://g" }),
            NullLogger<ClassifierCacheService>.Instance);

    private static List<(int Id, List<(int TopicId, List<int> SubTopicIds)> Topics)> Parse(string taxonomy)
    {
        using var doc = System.Text.Json.JsonDocument.Parse(taxonomy);
        return doc.RootElement.GetProperty("subjects").EnumerateArray()
            .Select(s => (s.GetProperty("id").GetInt32(),
                s.GetProperty("topics").EnumerateArray()
                    .Select(t => (t.GetProperty("id").GetInt32(),
                        t.GetProperty("subTopics").EnumerateArray().Select(st => st.GetProperty("id").GetInt32()).ToList()))
                    .ToList()))
            .ToList();
    }

    [Fact]
    public async Task Taxonomy_for_a_grade_keeps_only_that_grades_subjects_and_topics_and_changes_the_hash()
    {
        var tax = await SeedGradeTaxonomyAsync();
        await using var ctx = _db.NewContext();
        var service = NewServiceWithRealTaxonomy(ctx);

        var all = await service.GetTaxonomyAsync();
        var grade3 = await service.GetTaxonomyAsync(tax.Grade3);

        all.SubTopicCount.ShouldBe(3);
        grade3.SubTopicCount.ShouldBe(1);
        grade3.Hash.ShouldNotBe(all.Hash);

        var subject = Parse(grade3.Taxonomy).ShouldHaveSingleItem(); // Fen is not linked to grade 3
        subject.Id.ShouldBe(tax.MathId);
        var topic = subject.Topics.ShouldHaveSingleItem();           // grade 4's same-named "Kesirler" dropped
        topic.SubTopicIds.ShouldBe(new[] { tax.Math3SubTopic });

        var grade4 = await service.GetTaxonomyAsync(tax.Grade4);
        grade4.SubTopicCount.ShouldBe(2);
        Parse(grade4.Taxonomy).SelectMany(s => s.Topics).SelectMany(t => t.SubTopicIds)
            .ShouldNotContain(tax.Math3SubTopic);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task Taxonomy_without_a_positive_grade_is_the_whole_taxonomy(int? gradeId)
    {
        await SeedGradeTaxonomyAsync();
        await using var ctx = _db.NewContext();
        var service = NewServiceWithRealTaxonomy(ctx);

        var unfiltered = await service.GetTaxonomyAsync();
        var result = await service.GetTaxonomyAsync(gradeId);

        result.Taxonomy.ShouldBe(unfiltered.Taxonomy);
        result.Hash.ShouldBe(unfiltered.Hash);
        Parse(result.Taxonomy).Count.ShouldBe(2);
    }

    [Fact]
    // issue #577: ?topicId= yalnız o konuyu (ve dersini) bırakır; sınıfla birlikte de çalışır, başka sınıfın konusu boş döner.
    public async Task Taxonomy_for_a_topic_keeps_only_that_topic()
    {
        var tax = await SeedGradeTaxonomyAsync();
        int math4Topic;
        await using (var read = _db.NewContext())
            math4Topic = await read.Topics.Where(t => t.GradeId == tax.Grade4 && t.SubjectId == tax.MathId).Select(t => t.Id).SingleAsync();
        await using var ctx = _db.NewContext();
        var service = NewServiceWithRealTaxonomy(ctx);

        var topicOnly = await service.GetTaxonomyAsync(topicId: math4Topic);
        var withGrade = await service.GetTaxonomyAsync(tax.Grade4, math4Topic);
        var otherGrade = await service.GetTaxonomyAsync(tax.Grade3, math4Topic);

        topicOnly.SubTopicCount.ShouldBe(1);
        var subject = Parse(topicOnly.Taxonomy).ShouldHaveSingleItem();
        subject.Id.ShouldBe(tax.MathId);
        subject.Topics.ShouldHaveSingleItem().TopicId.ShouldBe(math4Topic);
        withGrade.Taxonomy.ShouldBe(topicOnly.Taxonomy);
        otherGrade.SubTopicCount.ShouldBe(0);
        Parse(otherGrade.Taxonomy).ShouldBeEmpty();
    }

    [Fact]
    public async Task Gemini_status_keeps_using_the_whole_taxonomy()
    {
        await using var ctx = _db.NewContext();
        await NewService(ctx, new StubHttp(HttpStatusCode.OK, GoodGeminiBody)).GetStatusAsync();

        await _taxonomy.Received().GetTreeAsync(null, Arg.Any<bool>(), Arg.Any<CancellationToken>());
        await _taxonomy.DidNotReceive().GetTreeAsync(Arg.Is<int?>(g => g != null), Arg.Any<bool>(), Arg.Any<CancellationToken>());
    }

    public void Dispose() => _db.Dispose();
}
