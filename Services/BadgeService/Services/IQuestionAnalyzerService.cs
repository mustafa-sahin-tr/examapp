namespace BadgeService.Services;

/// <summary>
/// Interface for sending questions to external analyzer service.
/// </summary>
public interface IQuestionAnalyzerService
{
    Task SendQuestionForAnalysisAsync(int questionId, CancellationToken cancellationToken);
}
