namespace BadgeService.Services;

/// <summary>
/// Interface for sending questions to external analyzer service.
/// </summary>
public interface IQuestionAnalyzerService
{
    Task SendQuestionForAnalysisAsync(int questionId, CancellationToken cancellationToken);
    Task SendStudyPageImageForAnalysisAsync(string imageUrl, CancellationToken cancellationToken);
}
// http://localhost:5678/webhook-test/c83391f6-b515-4b45-88f5-f9edc83b1cd7
// 