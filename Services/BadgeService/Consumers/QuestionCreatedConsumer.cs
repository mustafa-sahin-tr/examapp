using MassTransit;
using ExamApp.Foundation.Contracts;
using BadgeService.Services;

namespace BadgeService.Consumers;

public class QuestionCreatedConsumer : IConsumer<QuestionCreatedEvent>
{
    private readonly IQuestionAnalyzerService _analyzerService;
    private readonly ILogger<QuestionCreatedConsumer> _logger;

    public QuestionCreatedConsumer(IQuestionAnalyzerService analyzerService, ILogger<QuestionCreatedConsumer> logger)
    {
        _analyzerService = analyzerService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<QuestionCreatedEvent> context)
    {
        var @event = context.Message;

        _logger.LogInformation("📨 QuestionCreated event received. QuestionId: {QuestionId}, ClassificationSource: {ClassificationSource}",
            @event.QuestionId, @event.ClassificationSource);

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
