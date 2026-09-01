using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BadgeService.Models;
using Microsoft.Extensions.Options;

namespace BadgeService.Services;

/// <summary>
/// Gemini-backed implementation of <see cref="IQuestionClassifier"/>.
///
/// Pulls the question image from the exam API, sends it to Gemini together with the cached
/// taxonomy (subject/topic/subtopic list), and writes the returned classification back via
/// <c>PUT /api/questions/{id}/classification</c>. This is the in-process replacement for the
/// old n8n "analyze-question" workflow.
/// </summary>
public class GeminiQuestionClassifier : IQuestionClassifier
{
    private const int MaxSubTopics = 3;

    private const string Prompt =
        """
        Sen bir eğitim içeriği sınıflandırma asistanısın. Önbellekte (cached content) ders → konu → alt konu
        listesi, her biri kendi ID'siyle verildi.

        Sana görsel bir soru verilecek (soru kökü ve cevap şıkları tek görselde). Görevin:

        1. Sorunun hangi alt konuya (subTopic) ait olduğunu YALNIZCA önbellekteki listeden, listedeki ID'leri
           kullanarak belirle. Birden fazla alt konu uygunsa en olası olan ilk sırada olacak şekilde en fazla
           3 tane ekle. Listede olmayan ID uydurma.
        2. Hiçbir alt konu güvenilir şekilde eşleşmiyorsa subTopicIds'i boş bırak; mümkünse subjectId ve/veya
           topicId ver.
        3. Sorunun zorluğunu 1-10 arası belirle (1 = çok kolay, 10 = çok zor). Sınıf seviyesine göre değil,
           sorunun bilişsel yükü ve çözüm adımı sayısına göre değerlendir.
        4. Kısa bir gerekçe (reasoning) yaz.

        Sadece verilen JSON şemasına uygun yanıt ver.
        """;

    private static readonly object ResponseSchema = new
    {
        type = "OBJECT",
        properties = new
        {
            subTopicIds = new { type = "ARRAY", items = new { type = "INTEGER" } },
            subjectId = new { type = "INTEGER", nullable = true },
            topicId = new { type = "INTEGER", nullable = true },
            difficultyLevel = new { type = "INTEGER" },
            reasoning = new { type = "STRING" },
        },
        required = new[] { "subTopicIds", "difficultyLevel", "reasoning" },
    };

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IServiceTokenProvider _tokenProvider;
    private readonly GeminiOptions _gemini;
    private readonly string? _examApiBaseUrl;
    private readonly ILogger<GeminiQuestionClassifier> _logger;

    public GeminiQuestionClassifier(
        IHttpClientFactory httpClientFactory,
        IServiceTokenProvider tokenProvider,
        IOptions<GeminiOptions> gemini,
        IConfiguration configuration,
        ILogger<GeminiQuestionClassifier> logger)
    {
        _httpClientFactory = httpClientFactory;
        _tokenProvider = tokenProvider;
        _gemini = gemini.Value;
        _examApiBaseUrl = configuration["ExamApi:BaseUrl"]?.TrimEnd('/');
        _logger = logger;
    }

    public async Task ClassifyAndPersistAsync(int questionId, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(_gemini.ApiKey))
        {
            _logger.LogWarning("⚠️ Gemini not configured (ApiKey). Skipping classification for {QuestionId}.", questionId);
            return;
        }
        if (string.IsNullOrWhiteSpace(_examApiBaseUrl))
        {
            _logger.LogWarning("⚠️ ExamApi:BaseUrl not configured. Skipping classification for {QuestionId}.", questionId);
            return;
        }

        var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);

        // The taxonomy cache is owned by the exam API (admin rebuilds it there when
        // the taxonomy drifts). Fall back to the static config value if unset.
        var (cachedContent, model) = await ResolveCachePointerAsync(token, cancellationToken);
        if (string.IsNullOrWhiteSpace(cachedContent))
        {
            _logger.LogWarning(
                "⚠️ No classifier cache configured (neither exam API pointer nor Gemini:CachedContent). Skipping question {QuestionId}.",
                questionId);
            return;
        }

        var imageBytes = await FetchQuestionImageAsync(questionId, token, cancellationToken);
        var result = await CallGeminiAsync(imageBytes, cachedContent, model, cancellationToken);

        if (result == null)
        {
            throw new InvalidOperationException($"Gemini returned no usable classification for question {questionId}.");
        }

        Normalize(result);

        _logger.LogInformation(
            "🔎 Question {QuestionId} — Gemini result: subTopics=[{SubTopics}], subjectId={SubjectId}, topicId={TopicId}, difficulty={Difficulty}",
            questionId, string.Join(",", result.SubTopicIds), result.SubjectId, result.TopicId, result.DifficultyLevel);

        await PersistAsync(questionId, result, token, cancellationToken);

        _logger.LogInformation("✅ Question {QuestionId} classification persisted", questionId);
    }

    private async Task<(string? CachedContent, string Model)> ResolveCachePointerAsync(string token, CancellationToken ct)
    {
        try
        {
            var client = _httpClientFactory.CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var response = await client.GetAsync($"{_examApiBaseUrl}/api/questions/classifier-cache", ct);
            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                var name = doc.RootElement.TryGetProperty("cachedContentName", out var n) ? n.GetString() : null;
                var model = doc.RootElement.TryGetProperty("model", out var m) ? m.GetString() : null;
                if (!string.IsNullOrWhiteSpace(name))
                    return (name, string.IsNullOrWhiteSpace(model) ? _gemini.Model : model!);
            }
            else
            {
                _logger.LogWarning("Classifier cache pointer fetch returned {Status}; using configured value", (int)response.StatusCode);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Classifier cache pointer fetch failed; using configured value");
        }

        return (string.IsNullOrWhiteSpace(_gemini.CachedContent) ? null : _gemini.CachedContent, _gemini.Model);
    }

    private async Task<byte[]> FetchQuestionImageAsync(int questionId, string token, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var url = $"{_examApiBaseUrl}/api/questions/{questionId}/image?variant=v1";
        var response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Failed to fetch image for question {questionId}: {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
        }

        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    private async Task<QuestionClassificationResult?> CallGeminiAsync(byte[] imageBytes, string cachedContent, string model, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(_gemini.TimeoutSeconds);

        var requestBody = new
        {
            cachedContent = cachedContent,
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new object[]
                    {
                        new { inlineData = new { mimeType = "image/jpeg", data = Convert.ToBase64String(imageBytes) } },
                        new { text = Prompt },
                    },
                },
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                responseSchema = ResponseSchema,
            },
        };

        var url = $"{_gemini.BaseUrl.TrimEnd('/')}/{model.TrimStart('/')}:generateContent?key={_gemini.ApiKey}";
        using var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");

        var response = await client.PostAsync(url, content, ct);
        var payload = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Gemini generateContent failed: {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(payload)}");
        }

        return ParseGeminiResponse(payload);
    }

    private QuestionClassificationResult? ParseGeminiResponse(string payload)
    {
        using var doc = JsonDocument.Parse(payload);

        if (!doc.RootElement.TryGetProperty("candidates", out var candidates) || candidates.GetArrayLength() == 0)
        {
            _logger.LogWarning("⚠️ Gemini response had no candidates: {Payload}", Truncate(payload));
            return null;
        }

        var text = candidates[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        return JsonSerializer.Deserialize<QuestionClassificationResult>(text);
    }

    private static void Normalize(QuestionClassificationResult result)
    {
        result.SubTopicIds = result.SubTopicIds
            .Where(id => id > 0)
            .Distinct()
            .Take(MaxSubTopics)
            .ToList();

        if (result.SubjectId is <= 0) result.SubjectId = null;
        if (result.TopicId is <= 0) result.TopicId = null;

        result.DifficultyLevel = Math.Clamp(result.DifficultyLevel, 1, 10);
    }

    private async Task PersistAsync(int questionId, QuestionClassificationResult result, string token, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var body = new
        {
            subjectId = result.SubjectId,
            topicId = result.TopicId,
            subTopicIds = result.SubTopicIds.ToArray(),
            classificationSource = "AI",
            difficulty = result.DifficultyLevel,
        };

        var url = $"{_examApiBaseUrl}/api/questions/{questionId}/classification";
        var bodyJson = JsonSerializer.Serialize(body);
        using var content = new StringContent(bodyJson, Encoding.UTF8, "application/json");

        var response = await client.PutAsync(url, content, ct);
        if (!response.IsSuccessStatusCode)
        {
            var payload = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Failed to persist classification for question {questionId}: {(int)response.StatusCode} {response.ReasonPhrase}. " +
                $"Request: {bodyJson}. Response: {Truncate(payload)}");
        }
    }

    private static string Truncate(string value) => value.Length <= 2000 ? value : value[..2000];
}
