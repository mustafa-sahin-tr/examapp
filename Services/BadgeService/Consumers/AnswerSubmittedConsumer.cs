using MassTransit;
using ExamApp.Foundation.Contracts;
using BadgeService.Services;
namespace BadgeService.Consumers;

public class AnswerSubmittedConsumer : IConsumer<AnswerSubmittedEvent>
{
    private readonly AnswerSubmissionAggregationService _aggregationService;
    private readonly BadgeEvaluator _evaluator;

    public AnswerSubmittedConsumer(AnswerSubmissionAggregationService aggregationService, BadgeEvaluator evaluator)
    {
        _aggregationService = aggregationService;
        _evaluator = evaluator;
    }

    public async Task Consume(ConsumeContext<AnswerSubmittedEvent> context)
    {
        var message = context.Message;

        await _aggregationService.ProcessAsync(message, context.CancellationToken);
        await _evaluator.EvaluateAnswerSubmittedAsync(message.UserId, message.ClientId, context.CancellationToken);
    }
}
