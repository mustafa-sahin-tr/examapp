using Anthropic;
using Anthropic.Models.Messages;
using BadgeService.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BadgeService.Services;

/// <summary>
/// Claude-backed implementation of <see cref="IQuestionClassifier"/> (issue #469), selected with
/// <c>QuestionAnalyzer:Provider=Claude</c>. Same flow as <see cref="GeminiQuestionClassifier"/>: fetch the question
/// image from the exam API, classify it against the subject/topic/subtopic taxonomy, write the result back with
/// <c>ClassificationSource=AI</c>.
///
/// Uses the same stack as the exam API's labeling pre-annotator: Microsoft.Extensions.AI <see cref="IChatClient"/>
/// over the Anthropic SDK. The taxonomy (fetched from <c>GET /api/questions/classifier-taxonomy</c>) sits at the
/// end of the system message with a prompt-cache breakpoint — the Claude counterpart of the Gemini cached content —
/// so per question only the image and the short instruction are billed at the full input price. The answer is
/// structured output (JSON schema of a private response record); an id outside the taxonomy rejects
/// the whole result (logged, thrown → the message lands in the endpoint's error queue, as before).
///
/// Grade context (issue #565): when the event carries a <c>GradeId</c> the taxonomy is fetched with
/// <c>?gradeId=</c> (only that grade's subjects/topics/subtopics) and memory-cached per grade
/// (<c>{TaxonomyCacheKey}:{gradeId}</c>); the system message (instructions + taxonomy) is therefore a per-grade
/// prompt-cache prefix. An empty grade taxonomy skips the question with a warning — it never falls back to the
/// full taxonomy — and an id from another grade is rejected like any id outside the (filtered) taxonomy.
/// A null grade keeps the previous behaviour (whole taxonomy).
/// </summary>
public sealed class ClaudeQuestionClassifier : IQuestionClassifier, IDisposable
{
    internal const string TaxonomyCacheKey = "claude-question-classifier:taxonomy";

    // Cache write/read prices relative to the base input price (Anthropic prompt caching).
    private const decimal CacheWrite5mMultiplier = 1.25m;
    private const decimal CacheWrite1hMultiplier = 2m;
    private const decimal CacheReadMultiplier = 0.1m;

    // Instructions + taxonomy form the cached prefix: keep them byte-stable (no dates, ids or per-question text).
    internal const string Instructions =
        """
        Sen bir eğitim içeriği sınıflandırma asistanısın. Aşağıda ders → konu → alt konu taksonomisi JSON olarak,
        her öğe kendi "id" değeriyle verildi.

        Sana görsel bir soru verilecek (soru kökü ve cevap şıkları tek görselde). Görevin:

        1. Sorunun hangi alt konuya (subTopicIds) ait olduğunu YALNIZCA taksonomideki id'leri kullanarak belirle.
           Birden fazla alt konu uygunsa en olası olan ilk sırada olacak şekilde en fazla 3 tane ver.
           Taksonomide olmayan id uydurma.
        2. Hiçbir alt konu güvenilir şekilde eşleşmiyorsa subTopicIds'i boş bırak; mümkünse subjectId ve/veya
           topicId ver (yine yalnız taksonomideki id'ler).
        3. Sorunun zorluğunu (difficultyLevel) 1-10 arası belirle (1 = çok kolay, 10 = çok zor). Sınıf seviyesine
           göre değil, sorunun bilişsel yükü ve çözüm adımı sayısına göre değerlendir.
        4. Kısa bir gerekçe (reasoning) yaz.
        """;

    internal const string UserInstruction = "Bu soruyu sınıflandır.";

    private const string AiSource = "AI";
    private const string BookSource = "Book";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceTokenProvider _tokenProvider;
    private readonly ClaudeClassifierOptions _options;
    private readonly IMemoryCache _cache;
    private readonly string? _examApiBaseUrl;
    private readonly ILogger<ClaudeQuestionClassifier> _logger;
    private readonly IChatClient? _chat;

    public ClaudeQuestionClassifier(
        IHttpClientFactory httpClientFactory,
        IServiceTokenProvider tokenProvider,
        IOptions<ClaudeClassifierOptions> options,
        IConfiguration configuration,
        IMemoryCache cache,
        ILoggerFactory loggerFactory)
        : this(httpClientFactory, tokenProvider, options, configuration, cache, loggerFactory, CreateChatClient(options.Value, loggerFactory))
    {
    }

    /// <summary>Test seam: <paramref name="chat"/> replaces the Anthropic client (<see langword="null"/> = no API key).</summary>
    internal ClaudeQuestionClassifier(
        IHttpClientFactory httpClientFactory,
        IServiceTokenProvider tokenProvider,
        IOptions<ClaudeClassifierOptions> options,
        IConfiguration configuration,
        IMemoryCache cache,
        ILoggerFactory loggerFactory,
        IChatClient? chat)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _options = options.Value;
        _cache = cache;
        _examApiBaseUrl = configuration["ExamApi:BaseUrl"]?.TrimEnd('/');
        _logger = loggerFactory.CreateLogger<ClaudeQuestionClassifier>();
        _chat = chat;
    }

    public Task ClassifyAndPersistAsync(int questionId, int? gradeId, CancellationToken cancellationToken) =>
        ClassifyCoreAsync(questionId, gradeId, topicId: null, cancellationToken);

    /// <summary>
    /// Issue #577 (book TOC hybrid): taxonomy narrowed to <paramref name="topicId"/> (<c>&amp;topicId=</c>, cached per
    /// grade + topic). The result keeps the book's topic (subject untouched) and is persisted with source <c>Book</c>;
    /// an empty topic taxonomy (no subtopics) skips with a warning.
    /// </summary>
    public Task ClassifyWithinTopicAndPersistAsync(int questionId, int? gradeId, int topicId, CancellationToken cancellationToken) =>
        ClassifyCoreAsync(questionId, gradeId, topicId, cancellationToken);

    private async Task ClassifyCoreAsync(int questionId, int? gradeId, int? topicId, CancellationToken cancellationToken)
    {
        if (_chat is null)
        {
            _logger.LogWarning("⚠️ Claude not configured (Anthropic:ApiKey). Skipping classification for {QuestionId}.", questionId);
            return;
        }
        if (string.IsNullOrWhiteSpace(_examApiBaseUrl))
        {
            _logger.LogWarning("⚠️ ExamApi:BaseUrl not configured. Skipping classification for {QuestionId}.", questionId);
            return;
        }

        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        var examApi = new ExamApiClassificationClient(_httpClientFactory, _examApiBaseUrl);

        var taxonomy = await GetTaxonomyAsync(examApi, gradeId, topicId, token, cancellationToken);
        if (taxonomy.SubTopicCount == 0)
        {
            if (topicId.HasValue)
                _logger.LogWarning("⚠️ Classifier taxonomy for topic {TopicId} (grade {GradeId}) has no subtopics. Skipping question {QuestionId}; book topic kept.", topicId, gradeId?.ToString() ?? "-", questionId);
            else if (gradeId.HasValue)
                _logger.LogWarning("⚠️ Classifier taxonomy for grade {GradeId} is empty. Skipping question {QuestionId} (no fallback to the full taxonomy).", gradeId, questionId);
            else
                _logger.LogWarning("⚠️ Classifier taxonomy is empty. Skipping question {QuestionId}.", questionId);
            return;
        }

        var image = await examApi.FetchQuestionImageAsync(questionId, token, cancellationToken);
        var (result, usage) = await CallClaudeAsync(image, taxonomy, cancellationToken);

        _logger.LogInformation(
            "💰 Question {QuestionId} (grade {GradeId}) — Claude {Model} usage: input={InputTokens}, cacheWrite={CacheWriteTokens}, " +
            "cacheRead={CacheReadTokens}, output={OutputTokens}, estimatedCost=${CostUsd}",
            questionId, gradeId?.ToString() ?? "-", _options.Model, usage.InputTokens, usage.CacheWriteTokens, usage.CacheReadTokens, usage.OutputTokens,
            EstimateCostUsd(usage).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture));

        ExamApiClassificationClient.Normalize(result);

        if (taxonomy.FindUnknownId(result) is { } unknown)
        {
            _logger.LogWarning(
                "🚫 Question {QuestionId} — Claude result rejected: {UnknownId} is not in the taxonomy (hash {TaxonomyHash}). " +
                "subTopics=[{SubTopics}], subjectId={SubjectId}, topicId={TopicId}, reasoning={Reasoning}",
                questionId, unknown, taxonomy.Hash, string.Join(",", result.SubTopicIds), result.SubjectId, result.TopicId, result.Reasoning);
            throw new InvalidOperationException(
                $"Claude classification for question {questionId} rejected: {unknown} is not in the taxonomy.");
        }

        if (topicId.HasValue)
        {
            // Book TOC: the topic is the book's — keep it (subject untouched); only subtopic + difficulty are the model's.
            // Every subtopic is already inside this topic (filtered taxonomy, FindUnknownId above).
            result.TopicId = topicId;
            result.SubjectId = null;
        }

        _logger.LogInformation(
            "🔎 Question {QuestionId} — Claude result: subTopics=[{SubTopics}], subjectId={SubjectId}, topicId={TopicId}, difficulty={Difficulty}",
            questionId, string.Join(",", result.SubTopicIds), result.SubjectId, result.TopicId, result.DifficultyLevel);

        await examApi.PersistAsync(questionId, result, token, cancellationToken, topicId.HasValue ? BookSource : AiSource);

        _logger.LogInformation("✅ Question {QuestionId} classification persisted", questionId);
    }

    internal static string TaxonomyCacheKeyFor(int? gradeId) =>
        gradeId.HasValue ? $"{TaxonomyCacheKey}:{gradeId.Value}" : TaxonomyCacheKey;

    /// <summary>Issue #577: topic-filtered taxonomies get their own key (<c>…:{grade|-}:topic:{id}</c>), no collision with grade keys.</summary>
    internal static string TaxonomyCacheKeyFor(int? gradeId, int? topicId) =>
        topicId.HasValue
            ? $"{TaxonomyCacheKey}:{(gradeId.HasValue ? gradeId.Value.ToString(System.Globalization.CultureInfo.InvariantCulture) : "-")}:topic:{topicId.Value}"
            : TaxonomyCacheKeyFor(gradeId);

    private async Task<ClassifierTaxonomy> GetTaxonomyAsync(
        ExamApiClassificationClient examApi, int? gradeId, int? topicId, string token, CancellationToken ct)
    {
        var cacheKey = TaxonomyCacheKeyFor(gradeId, topicId);
        if (_cache.TryGetValue(cacheKey, out ClassifierTaxonomy? cached) && cached is not null)
            return cached;

        var taxonomy = await examApi.FetchTaxonomyAsync(gradeId, topicId, token, ct);
        if (_options.TaxonomyCacheSeconds > 0)
            _cache.Set(cacheKey, taxonomy, TimeSpan.FromSeconds(_options.TaxonomyCacheSeconds));
        return taxonomy;
    }

    private async Task<(QuestionClassificationResult Result, ClassificationUsage Usage)> CallClaudeAsync(
        QuestionImage image, ClassifierTaxonomy taxonomy, CancellationToken ct)
    {
        var chatOptions = new ChatOptions
        {
            ModelId = _options.Model,
            MaxOutputTokens = _options.MaxOutputTokens,
            Reasoning = ParseEffort(_options.Effort) is { } effort ? new ReasoningOptions { Effort = effort } : null,
        };

        List<ChatMessage> messages =
        [
            new(ChatRole.System,
            [
                new TextContent(Instructions),
                // Cache breakpoint at the end of the taxonomy: instructions + taxonomy are reused across questions.
                new TextContent("Taksonomi (JSON):\n" + taxonomy.Json).WithCacheControl(ParseTtl(_options.CacheTtl)),
            ]),
            // Image before text: Claude reads the question first, then the instruction.
            new(ChatRole.User, [new DataContent(image.Bytes, image.MediaType), new TextContent(UserInstruction)]),
        ];

        var response = await _chat!.GetResponseAsync<ClaudeClassification>(messages, chatOptions, cancellationToken: ct);
        var usage = ReadUsage(response);

        if (response.FinishReason == ChatFinishReason.ContentFilter)
            throw new InvalidOperationException("Claude declined to classify the question.");
        if (response.FinishReason == ChatFinishReason.Length)
            throw new InvalidOperationException("Claude classification response was truncated (MaxOutputTokens).");
        if (!response.TryGetResult(out var result) || result is null)
            throw new InvalidOperationException("Claude classification response did not match the schema.");

        return (new QuestionClassificationResult
        {
            SubTopicIds = result.SubTopicIds ?? [],
            SubjectId = result.SubjectId,
            TopicId = result.TopicId,
            DifficultyLevel = result.DifficultyLevel,
            Reasoning = result.Reasoning,
        }, usage);
    }

    /// <summary>
    /// Token usage split the way Anthropic bills it. Read from the raw Anthropic message when available
    /// (cache writes are not in MEAI <see cref="UsageDetails"/>); otherwise from <see cref="UsageDetails"/>.
    /// </summary>
    internal static ClassificationUsage ReadUsage(ChatResponse response)
    {
        if (response.RawRepresentation is Message { Usage: { } raw })
        {
            return new ClassificationUsage(
                raw.InputTokens,
                raw.CacheCreationInputTokens ?? 0,
                raw.CacheReadInputTokens ?? 0,
                raw.OutputTokens);
        }

        var cached = response.Usage?.CachedInputTokenCount ?? 0;
        return new ClassificationUsage(
            Math.Max(0, (response.Usage?.InputTokenCount ?? 0) - cached),
            0,
            cached,
            response.Usage?.OutputTokenCount ?? 0);
    }

    internal decimal EstimateCostUsd(ClassificationUsage usage)
    {
        var writeMultiplier = _options.CacheTtl == "5m" ? CacheWrite5mMultiplier : CacheWrite1hMultiplier;
        var inputPrice = _options.InputUsdPerMTok / 1_000_000m;
        return usage.InputTokens * inputPrice
            + usage.CacheWriteTokens * inputPrice * writeMultiplier
            + usage.CacheReadTokens * inputPrice * CacheReadMultiplier
            + usage.OutputTokens * (_options.OutputUsdPerMTok / 1_000_000m);
    }

    private static IChatClient? CreateChatClient(ClaudeClassifierOptions options, ILoggerFactory loggerFactory)
    {
        if (string.IsNullOrWhiteSpace(options.ApiKey))
            return null;

        // AsIChatClient: the Anthropic SDK's MEAI bridge (honours WithCacheControl on content).
        // UseLogging/UseOpenTelemetry: gen_ai.* spans in the Aspire dashboard, as in the exam API.
        return new AnthropicClient { ApiKey = options.ApiKey.Trim() }
            .AsIChatClient()
            .AsBuilder()
            .UseLogging(loggerFactory)
            .UseOpenTelemetry()
            .Build();
    }

    private static Ttl ParseTtl(string? value) => value == "5m" ? Ttl.Ttl5m : Ttl.Ttl1h;

    private static ReasoningEffort? ParseEffort(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "low" => ReasoningEffort.Low,
        "medium" => ReasoningEffort.Medium,
        "high" => ReasoningEffort.High,
        "xhigh" => ReasoningEffort.ExtraHigh,
        _ => null,
    };

    public void Dispose() => _chat?.Dispose();

    // Response schema — GetResponseAsync<T> sends it as output_config.format. A positional record on purpose: the
    // SDK bridge silently drops the format for QuestionClassificationResult (settable class) and the model would
    // answer free-form.
    private sealed record ClaudeClassification(
        List<int>? SubTopicIds, int? SubjectId, int? TopicId, int DifficultyLevel, string? Reasoning);
}

/// <summary>Uncached input, cache-write, cache-read and output tokens of one classification call.</summary>
public readonly record struct ClassificationUsage(long InputTokens, long CacheWriteTokens, long CacheReadTokens, long OutputTokens);
