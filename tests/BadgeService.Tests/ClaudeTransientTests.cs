using System.Net;
using Anthropic.Exceptions;
using BadgeService.Consumers;
using BadgeService.Services;

namespace BadgeService.Tests;

// Soru sınıflandırıcısı yalnız Claude'un geçici hatasında yeniden dener (QuestionCreatedConsumerDefinition).
public class ClaudeTransientTests
{
    private static HttpRequestException Http() => new("x");

    [Fact]
    public void Rate_limits_overloads_and_connection_errors_are_retried_other_errors_are_not()
    {
        Assert.True(ClaudeTransient.Is(new AnthropicRateLimitException(Http()) { StatusCode = HttpStatusCode.TooManyRequests, ResponseBody = "" }));
        Assert.True(ClaudeTransient.Is(new InvalidOperationException("wrapped",
            new AnthropicApiException("overloaded", Http()) { StatusCode = (HttpStatusCode)529, ResponseBody = "" })));
        Assert.True(ClaudeTransient.Is(new AnthropicIOException("reset", Http())));
        Assert.False(ClaudeTransient.Is(new AnthropicBadRequestException(Http()) { StatusCode = HttpStatusCode.BadRequest, ResponseBody = "" }));
        Assert.False(ClaudeTransient.Is(new InvalidOperationException("Claude classification response did not match the schema.")));
    }

    [Fact]
    public void The_classifier_runs_on_its_own_queue_with_a_small_concurrency()
    {
        Assert.Equal("badge-question-classifier", QuestionCreatedConsumerDefinition.QueueName);
        Assert.InRange(QuestionCreatedConsumerDefinition.ConcurrentMessageLimit, 1, 4);
        Assert.True(QuestionCreatedConsumerDefinition.RetryIntervals.Sum(t => t.TotalMinutes) >= 15);
    }
}
