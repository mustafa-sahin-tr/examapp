using System;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ExamApp.Api.Data;
using ExamApp.Api.Models.Dtos.Admin;
using ExamApp.Api.Services.Taxonomy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ExamApp.Api.Services.Classifier;

public class ClassifierCacheService : IClassifierCacheService
{
    private readonly AppDbContext _context;
    private readonly ITaxonomyService _taxonomy;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GeminiCacheOptions _options;
    private readonly ILogger<ClassifierCacheService> _logger;

    private const string SystemInstruction =
        "Bu önbellek, soru sınıflandırması için ders → konu → alt konu taksonomisini içerir. " +
        "Her öğenin kendi 'id' değeri vardır. Sınıflandırma yaparken YALNIZCA bu listedeki id'leri kullan.";

    public ClassifierCacheService(
        AppDbContext context,
        ITaxonomyService taxonomy,
        IHttpClientFactory httpClientFactory,
        IOptions<GeminiCacheOptions> options,
        ILogger<ClassifierCacheService> logger)
    {
        _context = context;
        _taxonomy = taxonomy;
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ClassifierCacheStatusDto> GetStatusAsync(CancellationToken ct = default)
    {
        var config = await _context.ClassifierCacheConfigs.FindAsync(new object[] { ClassifierCacheConfig.SingletonId }, ct);
        var (payload, subTopicCount) = await BuildTaxonomyPayloadAsync(ct);
        var currentHash = Sha256(payload);

        return new ClassifierCacheStatusDto
        {
            CachedContentName = config?.CachedContentName,
            Model = config?.Model ?? _options.Model,
            RefreshedAt = config?.RefreshedAt,
            SubTopicCount = config?.SubTopicCount ?? 0,
            ConfiguredInSettings = !string.IsNullOrWhiteSpace(_options.ApiKey),
            Stale = config?.CachedContentName == null || config.TaxonomyHash != currentHash,
        };
    }

    public async Task<(string? CachedContentName, string Model)> GetActivePointerAsync(CancellationToken ct = default)
    {
        var config = await _context.ClassifierCacheConfigs.FindAsync(new object[] { ClassifierCacheConfig.SingletonId }, ct);
        return (config?.CachedContentName, config?.Model ?? _options.Model);
    }

    public async Task<ClassifierCacheRefreshResultDto> RefreshAsync(int userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            return Fail("Gemini API anahtarı yapılandırılmamış (Gemini:ApiKey).");

        var (payload, subTopicCount) = await BuildTaxonomyPayloadAsync(ct);
        if (subTopicCount == 0)
            return Fail("Taksonomi boş — cache oluşturulamadı.");

        string cachedContentName;
        try
        {
            cachedContentName = await CreateCachedContentAsync(payload, ct);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Gemini cached content oluşturulamadı");
            return Fail($"Gemini cache oluşturulamadı: {ex.Message}");
        }

        var now = DateTime.UtcNow;
        var config = await _context.ClassifierCacheConfigs.FindAsync(new object[] { ClassifierCacheConfig.SingletonId }, ct);
        if (config == null)
        {
            config = new ClassifierCacheConfig { Id = ClassifierCacheConfig.SingletonId };
            _context.ClassifierCacheConfigs.Add(config);
        }

        config.CachedContentName = cachedContentName;
        config.Model = _options.Model;
        config.TaxonomyHash = Sha256(payload);
        config.SubTopicCount = subTopicCount;
        config.RefreshedAt = now;
        config.RefreshedByUserId = userId;
        await _context.SaveChangesAsync(ct);

        _logger.LogInformation(
            "Classifier cache refreshed: {Name} ({SubTopics} subtopics) by user {UserId}",
            cachedContentName, subTopicCount, userId);

        return new ClassifierCacheRefreshResultDto
        {
            Success = true,
            Message = "Sınıflandırma cache'i güncellendi.",
            CachedContentName = cachedContentName,
            SubTopicCount = subTopicCount,
            RefreshedAt = now,
        };
    }

    public async Task RefreshIfStaleAsync(int userId)
    {
        var status = await GetStatusAsync();
        if (!status.Stale)
        {
            _logger.LogDebug("Classifier cache already current; reconcile skipped.");
            return;
        }
        if (!status.ConfiguredInSettings)
        {
            _logger.LogWarning("Classifier cache is stale but Gemini:ApiKey is not configured; reconcile skipped.");
            return;
        }

        var result = await RefreshAsync(userId);
        if (!result.Success)
            _logger.LogWarning("Scheduled classifier cache refresh failed: {Message}", result.Message);
    }

    /// <summary>Compact JSON tree the classifier prompt refers to. Returns (json, subtopicCount).</summary>
    private async Task<(string Payload, int SubTopicCount)> BuildTaxonomyPayloadAsync(CancellationToken ct)
    {
        var tree = await _taxonomy.GetTreeAsync(ct);

        var model = new
        {
            subjects = tree.Subjects.Select(s => new
            {
                id = s.Id,
                name = s.Name,
                topics = s.Topics.Select(t => new
                {
                    id = t.Id,
                    name = t.Name,
                    grade = t.GradeName,
                    subTopics = t.SubTopics.Select(st => new { id = st.Id, name = st.Name }),
                }),
            }),
        };

        var subTopicCount = tree.Subjects.Sum(s => s.Topics.Sum(t => t.SubTopics.Count));
        var json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = false });
        return (json, subTopicCount);
    }

    private async Task<string> CreateCachedContentAsync(string taxonomyJson, CancellationToken ct)
    {
        var client = _httpClientFactory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(_options.TimeoutSeconds);

        var body = new
        {
            model = _options.Model,
            systemInstruction = new { parts = new[] { new { text = SystemInstruction } } },
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = taxonomyJson } } },
            },
            ttl = _options.Ttl,
        };

        var url = $"{_options.BaseUrl.TrimEnd('/')}/cachedContents?key={_options.ApiKey}";
        using var content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        var response = await client.PostAsync(url, content, ct);
        var payload = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"{(int)response.StatusCode} {response.ReasonPhrase}. {Truncate(payload)}");

        using var doc = JsonDocument.Parse(payload);
        if (!doc.RootElement.TryGetProperty("name", out var nameEl) || nameEl.GetString() is not { Length: > 0 } name)
            throw new InvalidOperationException($"Yanıtta 'name' yok: {Truncate(payload)}");

        return name;
    }

    private static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexStringLower(bytes);
    }

    private static string Truncate(string value) => value.Length <= 1500 ? value : value[..1500];

    private static ClassifierCacheRefreshResultDto Fail(string message) =>
        new() { Success = false, Message = message };
}
