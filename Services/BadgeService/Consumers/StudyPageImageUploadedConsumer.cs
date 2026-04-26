using BadgeService.Services;
using ExamApp.Foundation.Contracts;
using MassTransit;

namespace BadgeService.Consumers;

public class StudyPageImageUploadedConsumer : IConsumer<StudyPageImageUploadedEvent>
{

    private readonly IQuestionAnalyzerService _analyzerService;

    private readonly ILogger<StudyPageImageUploadedConsumer> _logger;

    public StudyPageImageUploadedConsumer(IQuestionAnalyzerService analyzerService, ILogger<StudyPageImageUploadedConsumer> logger)
    {
        _analyzerService = analyzerService;
        _logger = logger;
    }

    public async Task Consume(ConsumeContext<StudyPageImageUploadedEvent> context)
    {
        var @event = context.Message;

        _logger.LogInformation("📨 StudyPageImageUploaded event received. ImageUrl: {ImageUrl}, ClassificationSource: {ClassificationSource}",
            @event.ImageUrl, @event.ClassificationSource);

        // 🟢 Only call analyze endpoint if ClassificationSource is NOT "AI"
        if (@event.ClassificationSource != "AI")
        {
            try
            {
                await _analyzerService.SendStudyPageImageForAnalysisAsync(@event.ImageUrl, context.CancellationToken);
                _logger.LogInformation("✅ Study page image sent for analysis. ImageUrl: {ImageUrl}", @event.ImageUrl);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error sending study page image for analysis. ImageUrl: {ImageUrl}", @event.ImageUrl);
                throw; // Re-throw to let MassTransit handle retry
            }
        }
        else
        {
            _logger.LogInformation("⏭️ Skipping analysis for study page image {ImageUrl} (ClassificationSource is AI)", @event.ImageUrl);
        }




    }
}

