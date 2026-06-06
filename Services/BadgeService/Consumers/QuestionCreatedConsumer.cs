using MassTransit;
using ExamApp.Foundation.Contracts;
using BadgeService.Services;
using Microsoft.Extensions.Configuration;

namespace BadgeService.Consumers;

public class QuestionCreatedConsumer : IConsumer<QuestionCreatedEvent>
{
    private readonly IQuestionAnalyzerService _analyzerService;
    private readonly ILogger<QuestionCreatedConsumer> _logger;
    private readonly bool _aiActive;

    public QuestionCreatedConsumer(
        IQuestionAnalyzerService analyzerService,
        ILogger<QuestionCreatedConsumer> logger,
        IConfiguration configuration)
    {
        _analyzerService = analyzerService;
        _logger = logger;
        _aiActive = configuration.GetValue<bool?>("QuestionAnalyzer:AIActive") ?? true;
    }

    public async Task Consume(ConsumeContext<QuestionCreatedEvent> context)
    {
        var @event = context.Message;

        _logger.LogInformation("📨 QuestionCreated event received. QuestionId: {QuestionId}, ClassificationSource: {ClassificationSource}",
            @event.QuestionId, @event.ClassificationSource);

        if (!_aiActive)
        {
            _logger.LogInformation("⏭️ AI analyzer is disabled by config. Skipping analysis for question {QuestionId}", @event.QuestionId);
            return;
        }

        // 🟢 Only call analyze endpoint if ClassificationSource is NOT "AI"
        if (@event.ClassificationSource != "AI")
        {
            try
            {
                await _analyzerService.SendQuestionForAnalysisAsync(@event.QuestionId, context.CancellationToken);
                _logger.LogInformation("✅ Question {QuestionId} sent for analysis", @event.QuestionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error sending question {QuestionId} for analysis", @event.QuestionId);
                throw; // Re-throw to let MassTransit handle retry
            }
        }
        else
        {
            _logger.LogInformation("⏭️ Skipping analysis for question {QuestionId} (ClassificationSource is AI)", @event.QuestionId);
        }
    }
}
