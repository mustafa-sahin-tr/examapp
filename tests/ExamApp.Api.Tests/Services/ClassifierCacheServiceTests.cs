using System.Net;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.Classifier;
using ExamApp.Api.Services.Taxonomy;
using ExamApp.Api.Tests.Support;
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
        _taxonomy.GetTreeAsync(Arg.Any<CancellationToken>()).Returns(tree);
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

        http.Requests.ShouldHaveSingleItem().RequestUri!.ToString().ShouldContain("/cachedContents?key=test-key");

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

    public void Dispose() => _db.Dispose();
}
