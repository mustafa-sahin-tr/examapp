using System;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BadgeService.Services;

/// <summary>
/// Service for sending question data to external analyzer endpoint.
/// Uses ServiceTokenProvider-like pattern for authentication via JWT Bearer token.
/// </summary>
public class QuestionAnalyzerService : IQuestionAnalyzerService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly IServiceTokenProvider _tokenProvider;
    private readonly ILogger<QuestionAnalyzerService> _logger;

    public QuestionAnalyzerService(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        IServiceTokenProvider tokenProvider,
        ILogger<QuestionAnalyzerService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _tokenProvider = tokenProvider;
        _logger = logger;
    }


    public async Task SendStudyPageImageForAnalysisAsync(string imageUrl, CancellationToken cancellationToken)
    {
        var analyzerBaseUrl = _configuration["QuestionAnalyzer:BaseUrl"];
        if (string.IsNullOrWhiteSpace(analyzerBaseUrl))
        {
            _logger.LogWarning("⚠️ QuestionAnalyzer:BaseUrl is not configured. Skipping analysis request.");
            return;
        }

        try
        {
            // Get service-to-service token
            var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);

            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var analyzeUrl = $"{analyzerBaseUrl.TrimEnd('/')}/webhook/analyze-study-page";

            var payload = new { imageUrl };

            _logger.LogInformation("📤 Sending study page image to analyzer at {Url}", analyzeUrl);

            var response = await httpClient.PostAsJsonAsync(analyzeUrl, payload, cancellationToken);
            var content = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)  
            {
                _logger.LogError("❌ Analyzer returned {StatusCode}: {Content}", response.StatusCode, content);
                response.EnsureSuccessStatusCode(); // Throw if not success
            }

            _logger.LogInformation("✅ Study page image analysis request accepted. ImageUrl: {ImageUrl}, Status: {StatusCode}, Response: {Content}",
                imageUrl, response.StatusCode, content);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error sending study page image for analysis. ImageUrl: {ImageUrl}", imageUrl);
            throw;
        }
    }

    public async Task SendQuestionForAnalysisAsync(int questionId, CancellationToken cancellationToken)
    {
        var analyzerBaseUrl = _configuration["QuestionAnalyzer:BaseUrl"];
        if (string.IsNullOrWhiteSpace(analyzerBaseUrl))
        {
            _logger.LogWarning("⚠️ QuestionAnalyzer:BaseUrl is not configured. Skipping analysis request.");
            return;
        }

        try
        {
            // Get service-to-service token
            var token = await _tokenProvider.GetAccessTokenAsync(cancellationToken);

            var httpClient = _httpClientFactory.CreateClient();
            httpClient.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

            var analyzeUrl = $"{analyzerBaseUrl.TrimEnd('/')}/webhook/analyze-question";

            var payload = new { questionId };

            _logger.LogInformation("📤 Sending question {QuestionId} to analyzer at {Url}", questionId, analyzeUrl);

            var response = await httpClient.PostAsJsonAsync(analyzeUrl, payload, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var content = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogError("❌ Analyzer returned {StatusCode}: {Content}", response.StatusCode, content);
                response.EnsureSuccessStatusCode(); // Throw if not success
            }

            _logger.LogInformation("✅ Question {QuestionId} analysis request accepted (Status: {StatusCode}, Response: {Content})",
                questionId, response.StatusCode, await response.Content.ReadAsStringAsync(cancellationToken));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error sending question {QuestionId} for analysis", questionId);
            throw;
        }
    }
}
