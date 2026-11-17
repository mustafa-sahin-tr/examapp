using System.Net;
using Anthropic.Exceptions;
using ExamApp.Api.Services.Labeling;

namespace ExamApp.Api.Tests.Services;

public class ClaudeTransientTests
{
    private static HttpRequestException Http() => new("x");

    public static TheoryData<Exception, bool> Cases => new()
    {
        { new LabelingAiException("Claude request failed.", new AnthropicRateLimitException(Http()) { StatusCode = HttpStatusCode.TooManyRequests, ResponseBody = "" }), true },
        { new LabelingAiException("Claude request failed.", new Anthropic5xxException(Http()) { StatusCode = HttpStatusCode.BadGateway, ResponseBody = "" }), true },
        { new AnthropicIOException("reset", Http()), true },
        { new AnthropicApiException("overloaded", Http()) { StatusCode = (HttpStatusCode)529, ResponseBody = "" }, true },
        { new LabelingAiException("Claude request failed.", new AnthropicBadRequestException(Http()) { StatusCode = HttpStatusCode.BadRequest, ResponseBody = "" }), false },
        { new LabelingAiException("Claude response did not match the schema."), false },
        { new InvalidOperationException("x"), false },
    };

    [Theory]
    [MemberData(nameof(Cases))]
    public void Only_rate_limits_overloads_and_connection_errors_are_transient(Exception error, bool transient) =>
        ClaudeTransient.Is(error).ShouldBe(transient);
}
