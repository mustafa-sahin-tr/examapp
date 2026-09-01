using System.Net;
using BadgeService.Services;
using BadgeService.Tests.Support;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BadgeService.Tests;

public class GeminiQuestionClassifierTests
{
    private const string ExamApi = "https://exam.test";

    private static string GeminiText(string json) =>
        $$"""
        { "candidates": [ { "content": { "parts": [ { "text": {{System.Text.Json.JsonSerializer.Serialize(json)}} } ] } } ] }
        """;

    private static GeminiQuestionClassifier NewClassifier(
        StubHttp http, GeminiOptions? options = null)
    {
        var tokenProvider = Substitute.For<IServiceTokenProvider>();
        tokenProvider.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("test-token");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ExamApi:BaseUrl"] = ExamApi })
            .Build();

        return new GeminiQuestionClassifier(
            http,
            tokenProvider,
            Options.Create(options ?? new GeminiOptions { ApiKey = "k", Model = "models/gemini-x" }),
            config,
            NullLogger<GeminiQuestionClassifier>.Instance);
    }

    private static StubHttp HappyHttp(string geminiInnerJson) => new StubHttp()
        .On("/api/questions/classifier-cache", HttpStatusCode.OK, """{ "cachedContentName": "cachedContents/abc", "model": "models/gemini-x" }""")
        .OnBytes("/image", new byte[] { 1, 2, 3, 4 })
        .On(":generateContent", HttpStatusCode.OK, GeminiText(geminiInnerJson))
        .On("/classification", HttpStatusCode.OK, "{}");

    [Fact]
    public async Task Skips_entirely_when_the_api_key_is_not_configured()
    {
        var http = HappyHttp("{}");
        await NewClassifier(http, new GeminiOptions { ApiKey = "" }).ClassifyAndPersistAsync(1, default);
        http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Persists_the_normalized_classification_on_the_happy_path()
    {
        var http = HappyHttp("""{ "subTopicIds": [7, 7, 8, 9, 10], "subjectId": 3, "topicId": -1, "difficultyLevel": 99, "reasoning": "x" }""");

        await NewClassifier(http).ClassifyAndPersistAsync(42, default);

        http.Requests.Single(r => r.RequestUri!.ToString().Contains("/classification")).Method.ShouldBe(HttpMethod.Put);
        var body = http.BodyMatching("/classification");
        body.ShouldContain("\"difficulty\":10");        // clamped from 99
        body.ShouldContain("\"subjectId\":3");
        body.ShouldContain("\"topicId\":null");         // -1 -> null
        body.ShouldContain("\"subTopicIds\":[7,8,9]");  // deduped + capped at 3
        body.ShouldContain("\"classificationSource\":\"AI\"");
    }

    [Fact]
    public async Task Falls_back_to_the_configured_cached_content_when_the_pointer_is_unavailable()
    {
        var http = new StubHttp()
            .On("/api/questions/classifier-cache", HttpStatusCode.ServiceUnavailable, "down")
            .OnBytes("/image", new byte[] { 9 })
            .On(":generateContent", HttpStatusCode.OK, GeminiText("""{ "subTopicIds": [1], "difficultyLevel": 3, "reasoning": "r" }"""))
            .On("/classification", HttpStatusCode.OK, "{}");

        await NewClassifier(http, new GeminiOptions { ApiKey = "k", Model = "models/gemini-x", CachedContent = "cachedContents/fallback" })
            .ClassifyAndPersistAsync(5, default);

        http.BodyMatching(":generateContent").ShouldContain("cachedContents/fallback");
    }

    [Fact]
    public async Task Skips_when_there_is_no_cache_pointer_and_no_configured_fallback()
    {
        var http = new StubHttp()
            .On("/api/questions/classifier-cache", HttpStatusCode.OK, "{}");

        await NewClassifier(http).ClassifyAndPersistAsync(5, default);

        http.Requests.ShouldHaveSingleItem(); // only the pointer probe, nothing else
    }

    [Fact]
    public async Task Throws_when_gemini_returns_no_candidates()
    {
        var clean = new StubHttp()
            .On("/api/questions/classifier-cache", HttpStatusCode.OK, """{ "cachedContentName": "cachedContents/abc" }""")
            .OnBytes("/image", new byte[] { 1 })
            .On(":generateContent", HttpStatusCode.OK, """{ "candidates": [] }""");

        await Should.ThrowAsync<InvalidOperationException>(() => NewClassifier(clean).ClassifyAndPersistAsync(9, default));
    }

    [Fact]
    public async Task Throws_when_gemini_responds_with_an_http_error()
    {
        var http = new StubHttp()
            .On("/api/questions/classifier-cache", HttpStatusCode.OK, """{ "cachedContentName": "cachedContents/abc" }""")
            .OnBytes("/image", new byte[] { 1 })
            .On(":generateContent", HttpStatusCode.InternalServerError, "boom");

        await Should.ThrowAsync<InvalidOperationException>(() => NewClassifier(http).ClassifyAndPersistAsync(9, default));
    }

    [Fact]
    public async Task Throws_when_the_image_fetch_fails()
    {
        var http = new StubHttp()
            .On("/api/questions/classifier-cache", HttpStatusCode.OK, """{ "cachedContentName": "cachedContents/abc" }""")
            .On("/image", HttpStatusCode.NotFound, "missing");

        await Should.ThrowAsync<InvalidOperationException>(() => NewClassifier(http).ClassifyAndPersistAsync(9, default));
    }
}
