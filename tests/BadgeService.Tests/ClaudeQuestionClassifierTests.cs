using System.Net;
using BadgeService.Models;
using BadgeService.Services;
using BadgeService.Tests.Support;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BadgeService.Tests;

// issue #469: Claude soru sınıflandırıcısı — taksonomi önbellekli sistem mesajında, yapılandırılmış yanıt,
// taksonomi dışı id reddi, maliyet/token kaydı ve Gemini ↔ Claude seçimi.
public class ClaudeQuestionClassifierTests
{
    private const string ExamApi = "https://exam.test";

    // subject 1 → topic 10 → subtopics 100, 101; subject 2 → topic 20 → subtopic 200
    private const string TaxonomyJson =
        """{"subjects":[{"id":1,"name":"Matematik","topics":[{"id":10,"name":"Sayılar","grade":"5","subTopics":[{"id":100,"name":"Doğal sayılar"},{"id":101,"name":"Kesirler"}]}]},{"id":2,"name":"Fen","topics":[{"id":20,"name":"Madde","grade":"5","subTopics":[{"id":200,"name":"Hal değişimi"}]}]}]}""";

    private static string TaxonomyResponse(string json = TaxonomyJson) =>
        System.Text.Json.JsonSerializer.Serialize(new { taxonomy = json, hash = "abc123", subTopicCount = 3 });

    private static StubHttp HappyHttp() => new StubHttp()
        .On("/api/questions/classifier-taxonomy", HttpStatusCode.OK, TaxonomyResponse())
        .OnBytes("/image", new byte[] { 1, 2, 3, 4 })
        .On("/classification", HttpStatusCode.OK, "{}");

    private static ClaudeQuestionClassifier NewClassifier(
        StubHttp http, FakeChatClient? chat, ClaudeClassifierOptions? options = null, IMemoryCache? cache = null)
    {
        var tokenProvider = Substitute.For<IServiceTokenProvider>();
        tokenProvider.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("test-token");

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ExamApi:BaseUrl"] = ExamApi })
            .Build();

        return new ClaudeQuestionClassifier(
            http,
            tokenProvider,
            Options.Create(options ?? new ClaudeClassifierOptions { ApiKey = "k", Model = "claude-haiku-5-5" }),
            config,
            cache ?? new MemoryCache(new MemoryCacheOptions()),
            NullLoggerFactory.Instance,
            chat);
    }

    [Fact]
    public async Task Skips_entirely_when_the_api_key_is_not_configured()
    {
        var http = HappyHttp();

        await NewClassifier(http, chat: null).ClassifyAndPersistAsync(1, null, default);

        http.Requests.ShouldBeEmpty();
    }

    [Fact]
    public async Task Persists_the_normalized_classification_with_source_AI()
    {
        var http = HappyHttp();
        var chat = new FakeChatClient("""{ "subTopicIds": [101, 101, 100], "subjectId": 1, "topicId": 0, "difficultyLevel": 42, "reasoning": "kesir" }""");

        await NewClassifier(http, chat).ClassifyAndPersistAsync(42, null, default);

        http.Requests.Single(r => r.RequestUri!.ToString().Contains("/42/classification")).Method.ShouldBe(HttpMethod.Put);
        var body = http.BodyMatching("/classification");
        body.ShouldContain("\"subTopicIds\":[101,100]");     // deduped, order kept
        body.ShouldContain("\"subjectId\":1");
        body.ShouldContain("\"topicId\":null");              // 0 -> null
        body.ShouldContain("\"difficulty\":10");             // clamped from 42
        body.ShouldContain("\"classificationSource\":\"AI\"");
        // service token on every exam API call
        http.Requests.ShouldAllBe(r => r.Headers.Authorization!.Parameter == "test-token");
    }

    [Fact]
    public async Task Sends_the_taxonomy_in_a_prompt_cached_system_message_and_the_image_in_the_user_message()
    {
        var http = HappyHttp();
        var chat = new FakeChatClient("""{ "subTopicIds": [200], "difficultyLevel": 3, "reasoning": "r" }""");

        await NewClassifier(http, chat, new ClaudeClassifierOptions { ApiKey = "k", Model = "claude-sonnet-5-5", MaxOutputTokens = 1234 })
            .ClassifyAndPersistAsync(7, null, default);

        var call = chat.Calls.ShouldHaveSingleItem();
        call.Options!.ModelId.ShouldBe("claude-sonnet-5-5");
        call.Options.MaxOutputTokens.ShouldBe(1234);
        call.Options.ResponseFormat.ShouldBeOfType<ChatResponseFormatJson>().Schema.ShouldNotBeNull();

        var system = call.Messages.Single(m => m.Role == ChatRole.System);
        var taxonomyBlock = system.Contents.OfType<TextContent>().Last();
        taxonomyBlock.Text.ShouldEndWith(TaxonomyJson);                 // verbatim — cache prefix must be byte-stable
        // breakpoint after instructions + taxonomy (WithCacheControl stores it in AdditionalProperties)
        taxonomyBlock.AdditionalProperties.ShouldNotBeNull().Values.ShouldContain(v => v is Anthropic.Models.Messages.CacheControlEphemeral);
        system.Contents.OfType<TextContent>().First().Text.ShouldBe(ClaudeQuestionClassifier.Instructions);

        var user = call.Messages.Single(m => m.Role == ChatRole.User);
        var image = user.Contents.OfType<DataContent>().ShouldHaveSingleItem();
        image.Data.ToArray().ShouldBe(new byte[] { 1, 2, 3, 4 });
        image.MediaType.ShouldBe("image/jpeg");
        user.Contents.OfType<TextContent>().ShouldNotContain(t => t.Text.Contains(TaxonomyJson)); // nothing volatile in the prefix
    }

    [Fact]
    public async Task System_prompt_is_identical_across_questions_so_the_cache_prefix_matches()
    {
        var http = HappyHttp();
        var chat = new FakeChatClient("""{ "subTopicIds": [100], "difficultyLevel": 3, "reasoning": "r" }""");
        var classifier = NewClassifier(http, chat);

        await classifier.ClassifyAndPersistAsync(1, null, default);
        await classifier.ClassifyAndPersistAsync(2, null, default);

        string SystemText(FakeChatClient.Call c) =>
            string.Concat(c.Messages.Single(m => m.Role == ChatRole.System).Contents.OfType<TextContent>().Select(t => t.Text));
        SystemText(chat.Calls[0]).ShouldBe(SystemText(chat.Calls[1]));
    }

    [Fact]
    public async Task Reuses_the_fetched_taxonomy_until_it_expires()
    {
        var http = HappyHttp();
        var chat = new FakeChatClient("""{ "subTopicIds": [100], "difficultyLevel": 3, "reasoning": "r" }""");
        var classifier = NewClassifier(http, chat);

        await classifier.ClassifyAndPersistAsync(1, null, default);
        await classifier.ClassifyAndPersistAsync(2, null, default);

        http.Requests.Count(r => r.RequestUri!.ToString().Contains("classifier-taxonomy")).ShouldBe(1);
    }

    [Fact]
    public async Task Fetches_the_taxonomy_every_time_when_taxonomy_caching_is_off()
    {
        var http = HappyHttp();
        var chat = new FakeChatClient("""{ "subTopicIds": [100], "difficultyLevel": 3, "reasoning": "r" }""");
        var classifier = NewClassifier(http, chat, new ClaudeClassifierOptions { ApiKey = "k", TaxonomyCacheSeconds = 0 });

        await classifier.ClassifyAndPersistAsync(1, null, default);
        await classifier.ClassifyAndPersistAsync(2, null, default);

        http.Requests.Count(r => r.RequestUri!.ToString().Contains("classifier-taxonomy")).ShouldBe(2);
    }

    [Theory]
    [InlineData("""{ "subTopicIds": [100, 999], "difficultyLevel": 3, "reasoning": "r" }""")]
    [InlineData("""{ "subTopicIds": [], "subjectId": 77, "difficultyLevel": 3, "reasoning": "r" }""")]
    [InlineData("""{ "subTopicIds": [], "subjectId": 1, "topicId": 99, "difficultyLevel": 3, "reasoning": "r" }""")]
    public async Task Rejects_a_result_with_an_id_outside_the_taxonomy_and_persists_nothing(string claudeJson)
    {
        var http = HappyHttp();

        var ex = await Should.ThrowAsync<InvalidOperationException>(
            () => NewClassifier(http, new FakeChatClient(claudeJson)).ClassifyAndPersistAsync(5, null, default));

        ex.Message.ShouldContain("not in the taxonomy");
        http.Requests.ShouldNotContain(r => r.RequestUri!.ToString().Contains("/classification"));
    }

    [Fact]
    public async Task Accepts_subtopics_from_different_topics_like_a_human_edit()
    {
        var http = HappyHttp();

        await NewClassifier(http, new FakeChatClient("""{ "subTopicIds": [100, 200], "difficultyLevel": 3, "reasoning": "r" }"""))
            .ClassifyAndPersistAsync(5, null, default);

        http.BodyMatching("/classification").ShouldContain("\"subTopicIds\":[100,200]");
    }

    [Fact]
    public async Task Skips_when_the_taxonomy_is_empty()
    {
        var http = new StubHttp()
            .On("/api/questions/classifier-taxonomy", HttpStatusCode.OK, TaxonomyResponse("""{"subjects":[]}"""));
        var chat = new FakeChatClient("{}");

        await NewClassifier(http, chat).ClassifyAndPersistAsync(5, null, default);

        chat.Calls.ShouldBeEmpty();
        http.Requests.ShouldHaveSingleItem();
    }

    private static IEnumerable<string> TaxonomyUrls(StubHttp http) =>
        http.Requests.Select(r => r.RequestUri!.ToString()).Where(u => u.Contains("classifier-taxonomy"));

    // issue #565: grade context
    [Fact]
    public async Task With_a_grade_requests_the_filtered_taxonomy_and_persists_the_result()
    {
        var http = HappyHttp();
        var chat = new FakeChatClient("""{ "subTopicIds": [101], "difficultyLevel": 3, "reasoning": "r" }""");

        await NewClassifier(http, chat).ClassifyAndPersistAsync(8, 5, default);

        TaxonomyUrls(http).ShouldHaveSingleItem().ShouldEndWith("/api/questions/classifier-taxonomy?gradeId=5");
        chat.Calls.ShouldHaveSingleItem();
        http.BodyMatching("/classification").ShouldContain("\"subTopicIds\":[101]");
    }

    [Fact]
    public async Task With_a_grade_an_empty_filtered_taxonomy_skips_without_calling_Claude_or_falling_back()
    {
        var http = new StubHttp()
            .On("/api/questions/classifier-taxonomy", HttpStatusCode.OK, TaxonomyResponse("""{"subjects":[]}"""))
            .OnBytes("/image", new byte[] { 1 })
            .On("/classification", HttpStatusCode.OK, "{}");
        var chat = new FakeChatClient("{}");

        await NewClassifier(http, chat).ClassifyAndPersistAsync(8, 9, default);

        chat.Calls.ShouldBeEmpty();
        TaxonomyUrls(http).ShouldHaveSingleItem().ShouldEndWith("?gradeId=9"); // no second, unfiltered fetch
        http.Requests.ShouldNotContain(r => r.RequestUri!.ToString().Contains("/classification"));
    }

    [Fact]
    public async Task Without_a_grade_requests_the_unfiltered_taxonomy()
    {
        var http = HappyHttp();
        var chat = new FakeChatClient("""{ "subTopicIds": [100], "difficultyLevel": 3, "reasoning": "r" }""");

        await NewClassifier(http, chat).ClassifyAndPersistAsync(8, null, default);

        TaxonomyUrls(http).ShouldHaveSingleItem().ShouldEndWith("/api/questions/classifier-taxonomy");
    }

    [Fact]
    public async Task Taxonomy_is_cached_per_grade_without_collisions()
    {
        var http = HappyHttp();
        var chat = new FakeChatClient("""{ "subTopicIds": [100], "difficultyLevel": 3, "reasoning": "r" }""");
        var classifier = NewClassifier(http, chat);

        await classifier.ClassifyAndPersistAsync(1, 4, default);
        await classifier.ClassifyAndPersistAsync(2, 5, default);
        await classifier.ClassifyAndPersistAsync(3, 4, default);   // cached
        await classifier.ClassifyAndPersistAsync(4, null, default); // separate (unfiltered) key

        TaxonomyUrls(http).Count(u => u.EndsWith("?gradeId=4")).ShouldBe(1);
        TaxonomyUrls(http).Count(u => u.EndsWith("?gradeId=5")).ShouldBe(1);
        TaxonomyUrls(http).Count(u => !u.Contains("gradeId")).ShouldBe(1);
        ClaudeQuestionClassifier.TaxonomyCacheKeyFor(4).ShouldBe("claude-question-classifier:taxonomy:4");
        ClaudeQuestionClassifier.TaxonomyCacheKeyFor(null).ShouldBe(ClaudeQuestionClassifier.TaxonomyCacheKey);
    }

    [Fact]
    public async Task With_a_grade_an_id_outside_the_filtered_taxonomy_is_rejected()
    {
        // grade 5 taxonomy only knows subtopic 100; Claude answers with 200 (another grade's subtopic)
        var filtered = """{"subjects":[{"id":1,"name":"Matematik","topics":[{"id":10,"name":"Sayılar","grade":"5","subTopics":[{"id":100,"name":"Doğal sayılar"}]}]}]}""";
        var http = new StubHttp()
            .On("/api/questions/classifier-taxonomy", HttpStatusCode.OK, TaxonomyResponse(filtered))
            .OnBytes("/image", new byte[] { 1 })
            .On("/classification", HttpStatusCode.OK, "{}");

        await Should.ThrowAsync<InvalidOperationException>(() =>
            NewClassifier(http, new FakeChatClient("""{ "subTopicIds": [200], "difficultyLevel": 3, "reasoning": "r" }""")).ClassifyAndPersistAsync(8, 5, default));

        http.Requests.ShouldNotContain(r => r.RequestUri!.ToString().Contains("/classification"));
    }

    [Fact]
    public async Task Throws_when_the_taxonomy_fetch_fails()
    {
        var http = new StubHttp()
            .On("/api/questions/classifier-taxonomy", HttpStatusCode.Forbidden, "nope");

        await Should.ThrowAsync<InvalidOperationException>(
            () => NewClassifier(http, new FakeChatClient("{}")).ClassifyAndPersistAsync(5, null, default));
    }

    [Fact]
    public async Task Throws_when_the_response_was_truncated()
    {
        var http = HappyHttp();
        var chat = new FakeChatClient("""{ "subTopicIds": [10""", ChatFinishReason.Length);

        await Should.ThrowAsync<InvalidOperationException>(() => NewClassifier(http, chat).ClassifyAndPersistAsync(5, null, default));
        http.Requests.ShouldNotContain(r => r.RequestUri!.ToString().Contains("/classification"));
    }

    [Fact]
    public async Task Throws_when_the_response_does_not_match_the_schema()
    {
        var http = HappyHttp();

        await Should.ThrowAsync<InvalidOperationException>(
            () => NewClassifier(http, new FakeChatClient("not json")).ClassifyAndPersistAsync(5, null, default));
    }

    [Fact]
    public void Reads_usage_from_UsageDetails_when_there_is_no_raw_Anthropic_message()
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, "{}"))
        {
            Usage = new UsageDetails { InputTokenCount = 5_000, CachedInputTokenCount = 4_000, OutputTokenCount = 120 },
        };

        ClaudeQuestionClassifier.ReadUsage(response).ShouldBe(new ClassificationUsage(1_000, 0, 4_000, 120));
    }

    [Theory]
    [InlineData("1h", 5.1)]   // 1 + 2 (write x2) + 0.1 (read x0.1) + 2 (output)
    [InlineData("5m", 4.35)]  // 1 + 1.25 + 0.1 + 2
    public void Estimates_cost_with_cache_write_and_read_prices(string ttl, double expectedUsd)
    {
        var classifier = NewClassifier(new StubHttp(), chat: null, new ClaudeClassifierOptions
        {
            ApiKey = "k", InputUsdPerMTok = 1m, OutputUsdPerMTok = 2m, CacheTtl = ttl,
        });

        classifier.EstimateCostUsd(new ClassificationUsage(1_000_000, 1_000_000, 1_000_000, 1_000_000))
            .ShouldBe((decimal)expectedUsd);
    }

    [Theory]
    [InlineData(null, typeof(ClaudeQuestionClassifier))]
    [InlineData("Gemini", typeof(GeminiQuestionClassifier))]
    [InlineData("claude", typeof(ClaudeQuestionClassifier))]
    public void Provider_config_selects_the_classifier(string? provider, Type expected)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [QuestionClassifierRegistration.ProviderKey] = provider })
            .Build();

        var services = new ServiceCollection().AddQuestionClassifier(config);

        services.Single(d => d.ServiceType == typeof(IQuestionClassifier)).ImplementationType.ShouldBe(expected);
    }

    [Fact]
    public void Unknown_provider_fails_at_startup()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { [QuestionClassifierRegistration.ProviderKey] = "OpenAI" })
            .Build();

        Should.Throw<InvalidOperationException>(() => new ServiceCollection().AddQuestionClassifier(config));
    }

    [Fact]
    public void Taxonomy_lookup_finds_unknown_ids_only()
    {
        var taxonomy = ClassifierTaxonomy.Parse(TaxonomyJson, "h");

        taxonomy.SubTopicCount.ShouldBe(3);
        taxonomy.FindUnknownId(new QuestionClassificationResult { SubTopicIds = [100, 101], SubjectId = 1, TopicId = 10 }).ShouldBeNull();
        taxonomy.FindUnknownId(new QuestionClassificationResult { SubTopicIds = [] }).ShouldBeNull();
        taxonomy.FindUnknownId(new QuestionClassificationResult { SubTopicIds = [10] }).ShouldBe("subTopicId 10"); // a topic id is not a subtopic id
    }

    private const string AnthropicMessageResponse =
        """
        {"id":"msg_1","type":"message","role":"assistant","model":"claude-haiku-5-5",
         "content":[{"type":"text","text":"{\"subTopicIds\":[100],\"difficultyLevel\":4,\"reasoning\":\"r\"}"}],
         "stop_reason":"end_turn","stop_sequence":null,
         "usage":{"input_tokens":900,"output_tokens":50,"cache_creation_input_tokens":4000,"cache_read_input_tokens":0}}
        """;

    [Fact]
    // Real Anthropic SDK MEAI bridge over a fake HTTP handler: the wire request carries the cache breakpoint on the
    // taxonomy system block and a JSON-schema output format, and the raw usage (cache writes included) is read back.
    public async Task Anthropic_bridge_sends_cache_control_on_the_taxonomy_block_and_structured_output()
    {
        var anthropicHttp = new CapturingHandler(AnthropicMessageResponse);
        using var chat = new Anthropic.AnthropicClient { ApiKey = "k", HttpClient = new HttpClient(anthropicHttp), MaxRetries = 0 }
            .AsIChatClient();
        var http = HappyHttp();
        var tokenProvider = Substitute.For<IServiceTokenProvider>();
        tokenProvider.GetAccessTokenAsync(Arg.Any<CancellationToken>()).Returns("test-token");
        var classifier = new ClaudeQuestionClassifier(
            http, tokenProvider,
            Options.Create(new ClaudeClassifierOptions { ApiKey = "k", Model = "claude-haiku-5-5" }),
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ExamApi:BaseUrl"] = ExamApi }).Build(),
            new MemoryCache(new MemoryCacheOptions()), NullLoggerFactory.Instance, chat);

        await classifier.ClassifyAndPersistAsync(3, null, default);

        using var request = System.Text.Json.JsonDocument.Parse(anthropicHttp.Bodies.ShouldHaveSingleItem());
        var root = request.RootElement;
        root.GetProperty("model").GetString().ShouldBe("claude-haiku-5-5");
        var systemBlocks = root.GetProperty("system").EnumerateArray().ToList();
        systemBlocks[^1].GetProperty("text").GetString()!.ShouldEndWith(TaxonomyJson);
        var cacheControl = systemBlocks[^1].GetProperty("cache_control");
        cacheControl.GetProperty("type").GetString().ShouldBe("ephemeral");
        cacheControl.GetProperty("ttl").GetString().ShouldBe("1h");
        root.GetProperty("output_config").GetProperty("format").GetProperty("type").GetString().ShouldBe("json_schema");
        http.BodyMatching("/classification").ShouldContain("\"subTopicIds\":[100]");

        var response = await chat.GetResponseAsync("x", new ChatOptions { ModelId = "claude-haiku-5-5" });
        ClaudeQuestionClassifier.ReadUsage(response).ShouldBe(new ClassificationUsage(900, 4_000, 0, 50));
    }

    private sealed class CapturingHandler(string responseJson) : HttpMessageHandler
    {
        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (Bodies.Count == 0)
                Bodies.Add(await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, System.Text.Encoding.UTF8, "application/json"),
            };
        }
    }

    /// <summary>Records each call and answers with a fixed assistant text.</summary>
    private sealed class FakeChatClient(string responseText, ChatFinishReason? finishReason = null) : IChatClient
    {
        public sealed record Call(List<ChatMessage> Messages, ChatOptions? Options);

        public List<Call> Calls { get; } = [];

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Calls.Add(new Call(messages.ToList(), options));
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, responseText))
            {
                FinishReason = finishReason ?? ChatFinishReason.Stop,
                Usage = new UsageDetails { InputTokenCount = 3_000, CachedInputTokenCount = 2_500, OutputTokenCount = 80 },
            });
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
