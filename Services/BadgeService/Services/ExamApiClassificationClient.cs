using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using BadgeService.Models;

namespace BadgeService.Services;

/// <summary>
/// The exam API calls every question classifier shares (Gemini, Claude): fetch the question image,
/// fetch the live taxonomy, and write the result back via <c>PUT /api/questions/{id}/classification</c>.
/// All calls carry the BadgeService service token.
/// </summary>
public sealed class ExamApiClassificationClient(IHttpClientFactory httpClientFactory, string examApiBaseUrl)
{
    private const int MaxSubTopics = 3;

    private readonly string _baseUrl = examApiBaseUrl.TrimEnd('/');

    public async Task<QuestionImage> FetchQuestionImageAsync(int questionId, string token, CancellationToken ct)
    {
        var client = CreateClient(token);

        var url = $"{_baseUrl}/api/questions/{questionId}/image?variant=v1";
        var response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new InvalidOperationException(
                $"Failed to fetch image for question {questionId}: {(int)response.StatusCode} {response.ReasonPhrase}. {body}");
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        return new QuestionImage(
            await response.Content.ReadAsByteArrayAsync(ct),
            mediaType is not null && mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase) ? mediaType : "image/jpeg");
    }

    /// <summary>
    /// <c>GET /api/questions/classifier-taxonomy</c> (issue #469); with <paramref name="gradeId"/> only that grade's
    /// subjects/topics/subtopics (<c>?gradeId=</c>, issue #565).
    /// </summary>
    public Task<ClassifierTaxonomy> FetchTaxonomyAsync(int? gradeId, string token, CancellationToken ct) =>
        FetchTaxonomyAsync(gradeId, topicId: null, token, ct);

    /// <summary>Issue #577: with <paramref name="topicId"/> only that topic (<c>&amp;topicId=</c>, book TOC hybrid mode).</summary>
    public async Task<ClassifierTaxonomy> FetchTaxonomyAsync(int? gradeId, int? topicId, string token, CancellationToken ct)
    {
        var client = CreateClient(token);

        var url = $"{_baseUrl}/api/questions/classifier-taxonomy";
        var query = new List<string>(2);
        if (gradeId.HasValue)
            query.Add($"gradeId={gradeId.Value}");
        if (topicId.HasValue)
            query.Add($"topicId={topicId.Value}");
        if (query.Count > 0)
            url += "?" + string.Join("&", query);
        var response = await client.GetAsync(url, ct);
        var payload = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Failed to fetch classifier taxonomy: {(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(payload)}");
        }

        using var doc = JsonDocument.Parse(payload);
        var taxonomy = doc.RootElement.TryGetProperty("taxonomy", out var t) ? t.GetString() : null;
        var hash = doc.RootElement.TryGetProperty("hash", out var h) ? h.GetString() : null;
        if (string.IsNullOrWhiteSpace(taxonomy))
            throw new InvalidOperationException("Classifier taxonomy response had no 'taxonomy'.");

        return ClassifierTaxonomy.Parse(taxonomy, hash ?? string.Empty);
    }

    /// <summary>
    /// <paramref name="classificationSource"/>: <c>AI</c> by default; the book-TOC hybrid mode (issue #577) persists
    /// <c>Book</c> — topic/subject came from the book, only subtopic + difficulty from the model.
    /// </summary>
    public async Task PersistAsync(
        int questionId, QuestionClassificationResult result, string token, CancellationToken ct, string classificationSource = "AI")
    {
        var client = CreateClient(token);

        var body = new
        {
            subjectId = result.SubjectId,
            topicId = result.TopicId,
            subTopicIds = result.SubTopicIds.ToArray(),
            classificationSource,
            difficulty = result.DifficultyLevel,
        };

        var url = $"{_baseUrl}/api/questions/{questionId}/classification";
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

    /// <summary>Drops non-positive/duplicate ids, keeps at most 3 subtopics and clamps difficulty to 1-10.</summary>
    public static void Normalize(QuestionClassificationResult result)
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

    public static string Truncate(string value) => value.Length <= 2000 ? value : value[..2000];

    private HttpClient CreateClient(string token)
    {
        var client = httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

public sealed record QuestionImage(byte[] Bytes, string MediaType);
