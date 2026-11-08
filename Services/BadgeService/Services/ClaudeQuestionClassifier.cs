using Anthropic;
using Anthropic.Models.Messages;
using BadgeService.Models;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Unicode;

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

    /// <summary>
    /// Issue #1047: version of the fixed prompt parts (<see cref="Instructions"/>, <see cref="TagInstructions"/>, the user
    /// instructions and prefixes, both response schemas). Bump it whenever one of them changes; the exam API stores it on the
    /// question (<c>Questions.ClassifierRevision</c>) so questions classified with an older prompt can be found and re-run.
    /// </summary>
    public const int SchemaRevision = 1;

    // Instructions + taxonomy form the cached prefix: keep them byte-stable (no dates, ids or per-question text).
    internal const string Instructions =
        """
        Sen bir eğitim içeriği sınıflandırma asistanısın. Aşağıda ders → konu → alt konu taksonomisi JSON olarak,
        her öğe kendi "id" değeriyle verildi.

        Sana bir soru verilecek: ya görsel (soru kökü ve cevap şıkları tek görselde) ya da düz metin (yönerge, soru
        kökü, şıklar ya da etkinlik öğeleri; ör. doğru/yanlış ifadeleri, sınıflandırılacak kavramlar, eşleştirilecek
        çiftler). Görevin:

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

    /// <summary>
    /// Issue #1047: the outcome / skill step. The closed lists differ per question (grade + subject outcomes), so they go in
    /// the user message; this system message stays byte-stable.
    /// </summary>
    internal const string TagInstructions =
        """
        Sen bir eğitim içeriği etiketleme asistanısın. Sana bir soru (görsel ya da düz metin) ve iki kapalı liste
        verilecek: MEB kazanımları (learningOutcomes) ve TYMM becerileri (skills), her öğe kendi "id" değeriyle. Görevin:

        1. Sorunun doğrudan ölçtüğü kazanımları (learningOutcomeIds) YALNIZCA verilen kazanım listesindeki id'lerle seç;
           en uygun olan ilk sırada, en fazla 3 tane. Hiçbiri açıkça uymuyorsa boş bırak. Liste boşsa boş bırak.
        2. Sorunun çözümünde öne çıkan becerileri (skillIds) YALNIZCA verilen beceri listesindeki id'lerle seç; 0-2 tane.
           Belirgin bir beceri yoksa boş bırak. Liste boşsa boş bırak.
        3. Listede olmayan id uydurma. Kısa bir gerekçe (reasoning) yaz.
        """;

    internal const string TagUserInstruction = "Bu sorunun kazanımlarını ve becerilerini seç.";

    internal const int MaxLearningOutcomes = 3;
    internal const int MaxSkills = 2;

    // Turkish letters as-is (not \uXXXX): fewer tokens, readable in logs.
    private static readonly JsonSerializerOptions TagListJson = new() { Encoder = JavaScriptEncoder.Create(UnicodeRanges.All) };

    /// <summary>Issue #590: the text of a question without an image precedes <see cref="UserInstruction"/>.</summary>
    internal const string TextQuestionPrefix = "Soru (metin):\n";

    /// <summary>
    /// The text of a question that has an image follows the image: the instruction, passage and item labels often sit
    /// outside the cropped image (an activity's "Tabloyu tamamlayınız" with only the table cropped).
    /// </summary>
    internal const string ImageTextPrefix = "Görselin yanındaki metin (yönerge, paragraf, şıklar):\n";

    private const string AiSource = "AI";

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
        ClassifyCoreAsync(questionId, gradeId, topicId: null, AiSource, cancellationToken);

    /// <summary>
    /// Issue #577 (book TOC hybrid) / #590 (teacher-locked topic): taxonomy narrowed to <paramref name="topicId"/>
    /// (<c>&amp;topicId=</c>, cached per grade + topic). The result keeps the given topic (subject untouched) and is persisted
    /// with <paramref name="classificationSource"/>; an empty topic taxonomy (no subtopics) skips with a warning.
    /// </summary>
    public Task ClassifyWithinTopicAndPersistAsync(
        int questionId, int? gradeId, int topicId, string classificationSource, CancellationToken cancellationToken) =>
        ClassifyCoreAsync(questionId, gradeId, topicId, classificationSource, cancellationToken);

    private async Task ClassifyCoreAsync(
        int questionId, int? gradeId, int? topicId, string classificationSource, CancellationToken cancellationToken)
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

        // Issue #590: a question without an image (text-authored activity, text MCQ) is classified from its text.
        // With an image the text goes along too: the instruction outside the crop decides the subject.
        var image = await examApi.TryFetchQuestionImageAsync(questionId, token, cancellationToken);
        var text = await examApi.FetchQuestionTextAsync(questionId, token, cancellationToken);
        if (image is null && string.IsNullOrWhiteSpace(text))
        {
            _logger.LogWarning("⚠️ Question {QuestionId} has neither an image nor text. Skipping classification.", questionId);
            return;
        }
        var (result, usage) = await CallClaudeAsync(image, text, taxonomy, cancellationToken);
        LogUsage(questionId, gradeId, "classification", usage);

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
            // Book TOC / teacher-locked topic: keep it (subject untouched); only subtopic + difficulty are the model's.
            // Every subtopic is already inside this topic (filtered taxonomy, FindUnknownId above).
            result.TopicId = topicId;
            result.SubjectId = null;
        }

        _logger.LogInformation(
            "🔎 Question {QuestionId} — Claude result: subTopics=[{SubTopics}], subjectId={SubjectId}, topicId={TopicId}, difficulty={Difficulty}",
            questionId, string.Join(",", result.SubTopicIds), result.SubjectId, result.TopicId, result.DifficultyLevel);

        await examApi.PersistAsync(questionId, result, token, cancellationToken, classificationSource, SchemaRevision);

        _logger.LogInformation("✅ Question {QuestionId} classification persisted", questionId);

        // Issue #1047: outcomes + skills against the subject just written. A failure here keeps the classification
        // (no retry of the whole message for an optional step).
        try
        {
            await TagCoreAsync(examApi, questionId, gradeId, image, text, token, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "⚠️ Question {QuestionId} — outcome / skill tagging failed; classification kept.", questionId);
        }
    }

    /// <summary>Issue #1047: outcome / skill step alone (taxonomy already set by the book or the teacher).</summary>
    public async Task TagAndPersistAsync(int questionId, int? gradeId, CancellationToken cancellationToken)
    {
        if (_chat is null)
        {
            _logger.LogWarning("⚠️ Claude not configured (Anthropic:ApiKey). Skipping tagging for {QuestionId}.", questionId);
            return;
        }
        if (string.IsNullOrWhiteSpace(_examApiBaseUrl))
        {
            _logger.LogWarning("⚠️ ExamApi:BaseUrl not configured. Skipping tagging for {QuestionId}.", questionId);
            return;
        }

        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);
        var examApi = new ExamApiClassificationClient(_httpClientFactory, _examApiBaseUrl);

        var image = await examApi.TryFetchQuestionImageAsync(questionId, token, cancellationToken);
        var text = await examApi.FetchQuestionTextAsync(questionId, token, cancellationToken);
        if (image is null && string.IsNullOrWhiteSpace(text))
        {
            _logger.LogWarning("⚠️ Question {QuestionId} has neither an image nor text. Skipping tagging.", questionId);
            return;
        }

        await TagCoreAsync(examApi, questionId, gradeId, image, text, token, cancellationToken);
    }

    private async Task TagCoreAsync(
        ExamApiClassificationClient examApi, int questionId, int? gradeId, QuestionImage? image, string? text, string token,
        CancellationToken ct)
    {
        var options = await examApi.FetchTagOptionsAsync(questionId, gradeId, token, ct);
        if (options.LearningOutcomes.Count == 0 && options.Skills.Count == 0)
        {
            _logger.LogInformation("⏭️ Question {QuestionId} — no outcomes / skills to pick (already linked or none for its grade + subject).", questionId);
            return;
        }

        var (tags, usage) = await CallClaudeForTagsAsync(image, text, options, ct);
        LogUsage(questionId, gradeId, "tags", usage);

        var outcomeIds = (tags.LearningOutcomeIds ?? []).Where(id => id > 0).Distinct().Take(MaxLearningOutcomes).ToList();
        var skillIds = (tags.SkillIds ?? []).Where(id => id > 0).Distinct().Take(MaxSkills).ToList();

        var offeredOutcomes = options.LearningOutcomes.Select(o => o.Id).ToHashSet();
        var offeredSkills = options.Skills.Select(o => o.Id).ToHashSet();
        var unknown = outcomeIds.Where(id => !offeredOutcomes.Contains(id)).Select(id => $"learningOutcome:{id}")
            .Concat(skillIds.Where(id => !offeredSkills.Contains(id)).Select(id => $"skill:{id}"))
            .FirstOrDefault();
        if (unknown is not null)
        {
            _logger.LogWarning(
                "🚫 Question {QuestionId} — Claude tags rejected: {UnknownId} is not in the offered lists. outcomes=[{Outcomes}], skills=[{Skills}], reasoning={Reasoning}",
                questionId, unknown, string.Join(",", outcomeIds), string.Join(",", skillIds), tags.Reasoning);
            throw new InvalidOperationException($"Claude tags for question {questionId} rejected: {unknown} is not in the offered lists.");
        }

        if (outcomeIds.Count == 0 && skillIds.Count == 0)
        {
            _logger.LogInformation("🔎 Question {QuestionId} — Claude picked no outcomes / skills.", questionId);
            return;
        }

        await examApi.PersistTagsAsync(questionId, gradeId, outcomeIds, skillIds, SchemaRevision, token, ct);
        _logger.LogInformation(
            "✅ Question {QuestionId} tags persisted: outcomes=[{Outcomes}], skills=[{Skills}]",
            questionId, string.Join(",", outcomeIds), string.Join(",", skillIds));
    }

    private void LogUsage(int questionId, int? gradeId, string step, ClassificationUsage usage) =>
        _logger.LogInformation(
            "💰 Question {QuestionId} (grade {GradeId}) — Claude {Model} {Step} usage: input={InputTokens}, cacheWrite={CacheWriteTokens}, " +
            "cacheRead={CacheReadTokens}, output={OutputTokens}, estimatedCost=${CostUsd}",
            questionId, gradeId?.ToString() ?? "-", _options.Model, step, usage.InputTokens, usage.CacheWriteTokens, usage.CacheReadTokens, usage.OutputTokens,
            EstimateCostUsd(usage).ToString("0.######", System.Globalization.CultureInfo.InvariantCulture));

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

    private ChatOptions CreateChatOptions() => new()
    {
        ModelId = _options.Model,
        MaxOutputTokens = _options.MaxOutputTokens,
        Reasoning = ParseEffort(_options.Effort) is { } effort ? new ReasoningOptions { Effort = effort } : null,
    };

    private async Task<(QuestionClassificationResult Result, ClassificationUsage Usage)> CallClaudeAsync(
        QuestionImage? image, string? text, ClassifierTaxonomy taxonomy, CancellationToken ct)
    {
        var chatOptions = CreateChatOptions();

        List<ChatMessage> messages =
        [
            new(ChatRole.System,
            [
                new TextContent(Instructions),
                // Cache breakpoint at the end of the taxonomy: instructions + taxonomy are reused across questions.
                new TextContent("Taksonomi (JSON):\n" + taxonomy.Json).WithCacheControl(ParseTtl(_options.CacheTtl)),
            ]),
            // Question (image and its text, or text when there is no image) before the instruction: Claude reads it first.
            new(ChatRole.User, QuestionContents(image, text)),
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

    private async Task<(ClaudeTags Tags, ClassificationUsage Usage)> CallClaudeForTagsAsync(
        QuestionImage? image, string? text, ClassifierTagOptions options, CancellationToken ct)
    {
        List<ChatMessage> messages =
        [
            new(ChatRole.System, TagInstructions),
            new(ChatRole.User, TagContents(image, text, options)),
        ];

        var response = await _chat!.GetResponseAsync<ClaudeTags>(messages, CreateChatOptions(), cancellationToken: ct);
        var usage = ReadUsage(response);

        if (response.FinishReason == ChatFinishReason.ContentFilter)
            throw new InvalidOperationException("Claude declined to tag the question.");
        if (response.FinishReason == ChatFinishReason.Length)
            throw new InvalidOperationException("Claude tag response was truncated (MaxOutputTokens).");
        if (!response.TryGetResult(out var tags) || tags is null)
            throw new InvalidOperationException("Claude tag response did not match the schema.");

        return (tags, usage);
    }

    /// <summary>Issue #1047: the question, then both closed lists (compact JSON), then the instruction.</summary>
    internal static List<AIContent> TagContents(QuestionImage? image, string? text, ClassifierTagOptions options)
    {
        var contents = QuestionContents(image, text, TagUserInstruction);
        var outcomes = JsonSerializer.Serialize(
            options.LearningOutcomes.Select(o => new { id = o.Id, code = o.Code, description = o.Description }), TagListJson);
        var skills = JsonSerializer.Serialize(options.Skills.Select(s => new { id = s.Id, code = s.Code, name = s.Name }), TagListJson);
        contents.Insert(contents.Count - 1, new TextContent(
            "Kazanımlar (learningOutcomes, JSON):\n" + outcomes + "\n\nBeceriler (skills, JSON):\n" + skills));
        return contents;
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

    internal static List<AIContent> QuestionContents(QuestionImage? image, string? text, string instruction = UserInstruction)
    {
        var contents = new List<AIContent>(3);
        if (image is not null)
        {
            contents.Add(new DataContent(image.Bytes, image.MediaType));
            if (!string.IsNullOrWhiteSpace(text))
                contents.Add(new TextContent(ImageTextPrefix + text));
        }
        else
        {
            contents.Add(new TextContent(TextQuestionPrefix + text));
        }
        contents.Add(new TextContent(instruction));
        return contents;
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

    // Issue #1047: outcome / skill step schema (positional record for the same reason as above).
    private sealed record ClaudeTags(List<int>? LearningOutcomeIds, List<int>? SkillIds, string? Reasoning);
}

/// <summary>Uncached input, cache-write, cache-read and output tokens of one classification call.</summary>
public readonly record struct ClassificationUsage(long InputTokens, long CacheWriteTokens, long CacheReadTokens, long OutputTokens);
