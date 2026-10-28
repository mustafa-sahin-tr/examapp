using MassTransit;
using ExamApp.Foundation.Contracts;
using BadgeService.Services;
using Microsoft.Extensions.Configuration;

namespace BadgeService.Consumers;

public class QuestionCreatedConsumer : IConsumer<QuestionCreatedEvent>
{
    /// <summary>Issue #577: <c>ClassificationSource</c> of questions whose topic came from a book's table of contents.</summary>
    public const string BookSource = "Book";

    /// <summary>Issue #590: source persisted for a teacher-locked topic (classified within it).</summary>
    public const string HumanSource = "Human";

    private readonly IQuestionClassifier _classifier;
    private readonly ILogger<QuestionCreatedConsumer> _logger;
    private readonly bool _aiActive;
    private readonly BookTocMode _bookTocMode;

    public QuestionCreatedConsumer(
        IQuestionClassifier classifier,
        ILogger<QuestionCreatedConsumer> logger,
        IConfiguration configuration)
    {
        _classifier = classifier;
        _logger = logger;
        _aiActive = configuration.GetValue<bool?>("QuestionAnalyzer:AIActive") ?? true;
        _bookTocMode = BookTocModeConfig.Read(configuration);
    }

    public async Task Consume(ConsumeContext<QuestionCreatedEvent> context)
    {
        var @event = context.Message;

        _logger.LogInformation("📨 QuestionCreated event received. QuestionId: {QuestionId}, ClassificationSource: {ClassificationSource}, GradeId: {GradeId}",
            @event.QuestionId, @event.ClassificationSource, @event.GradeId);

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
            // Issue #590: the teacher picked the taxonomy — never overwrite it. With subtopics nothing is left to classify;
            // with only a topic, classify subtopic + difficulty inside it and keep the source Human.
            if (@event.TaxonomyLocked)
            {
                if (@event.SubTopicIds is { Count: > 0 } || @event.TopicId is not > 0)
                {
                    _logger.LogInformation("⏭️ Skipping question {QuestionId} (taxonomy locked by the teacher)", @event.QuestionId);
                    return;
                }

                await _classifier.ClassifyWithinTopicAndPersistAsync(
                    @event.QuestionId, @event.GradeId, @event.TopicId.Value, HumanSource, context.CancellationToken);
                return;
            }

            // Issue #577: book table of contents (QuestionAnalyzer:BookTocMode).
            if (string.Equals(@event.ClassificationSource, BookSource, StringComparison.OrdinalIgnoreCase) &&
                _bookTocMode != BookTocMode.Always)
            {
                var hasSubTopic = @event.SubTopicIds is { Count: > 0 };
                if (_bookTocMode == BookTocMode.Skip || hasSubTopic)
                {
                    _logger.LogInformation("⏭️ Skipping question {QuestionId} (book TOC, mode {Mode}, subtopic {HasSubTopic})",
                        @event.QuestionId, _bookTocMode, hasSubTopic);
                    return;
                }

                if (@event.TopicId is > 0 and var topicId)
                {
                    await _classifier.ClassifyWithinTopicAndPersistAsync(
                        @event.QuestionId, @event.GradeId, topicId, BookSource, context.CancellationToken);
                    return;
                }
                // Hybrid without a topic: nothing to constrain to — classify like a human-authored question.
            }

            await _classifier.ClassifyAndPersistAsync(@event.QuestionId, @event.GradeId, context.CancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "❌ Error classifying question {QuestionId}", @event.QuestionId);
            throw; // Let MassTransit retry / dead-letter.
        }
    }
}
