using MassTransit;
using ExamApp.Foundation.Contracts;
using BadgeService.Services;
using Microsoft.Extensions.Configuration;

namespace BadgeService.Consumers;

public class QuestionCreatedConsumer : IConsumer<QuestionCreatedEvent>
{
    private readonly IQuestionClassifier _classifier;
    private readonly ILogger<QuestionCreatedConsumer> _logger;
    private readonly bool _aiActive;

    public QuestionCreatedConsumer(
        IQuestionClassifier classifier,
        ILogger<QuestionCreatedConsumer> logger,
        IConfiguration configuration)
    {
        _classifier = classifier;
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
            _logger.LogInformation("⏭️ AI classifier is disabled by config. Skipping question {QuestionId}", @event.QuestionId);
            return;
        }

        // Skip questions a human already classified (or a previous AI run).
        if (@event.ClassificationSource == "AI")
        {
            _logger.LogInformation("⏭️ Skipping question {QuestionId} (ClassificationSource is AI)", @event.QuestionId);
            return;
        }

        try
        {
            await _classifier.ClassifyAndPersistAsync(@event.QuestionId, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error classifying question {QuestionId}", @event.QuestionId);
            throw; // Let MassTransit retry / dead-letter.
        }
    }
}
