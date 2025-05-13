using MassTransit;
using ExamApp.Foundation.Contracts;
using BadgeService.Services;
namespace BadgeService.Consumers;

public class AnswerSubmittedConsumer : IConsumer<AnswerSubmittedEvent>
{
     private readonly BadgeEvaluator _evaluator;

     public AnswerSubmittedConsumer(BadgeEvaluator evaluator)
    {
        _evaluator = evaluator;
    }
    public async Task Consume(ConsumeContext<AnswerSubmittedEvent> context)
    {
        var userId = context.Message.UserId;
        var clientId = context.Message.ClientId;
        await _evaluator.EvaluateAnswerSubmittedAsync(userId,clientId);
    }
}
