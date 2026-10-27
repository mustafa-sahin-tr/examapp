using ExamApp.Api.Services.Labeling;
using Microsoft.Extensions.AI;

namespace ExamApp.Api.Tests.Services;

/// <summary>
/// Anthropic structured output <c>$ref</c>/<c>$defs</c> içeren şemayı reddeder (400 "References must be defined"). Claude'a
/// giden her yanıt şeması — çıkarıcı ve dedektörle AYNI yoldan (<c>GetResponseAsync&lt;T&gt;</c> → <c>ChatOptions.ResponseFormat</c>)
/// üretilip yakalanır — referans içermemeli. Aynı record tipi iki özellikte kullanılırsa MEAI ikinciyi <c>$ref</c> yapar.
/// </summary>
public class LabelingResponseSchemaTests
{
    public static TheoryData<string> SchemaTypes => new()
    {
        nameof(ActivityDrafts.MatrixDraft),
        nameof(ActivityDrafts.SlotDraft),
        nameof(ActivityDrafts.DragDraft),
        nameof(ActivityDrafts.MatchDraft),
        nameof(ClaudeLabelDetector.PageBlocks),
        nameof(ClaudeLabelDetector.OptionBoxes),
    };

    [Theory]
    [MemberData(nameof(SchemaTypes))]
    public async Task ResponseSchemaSentToClaude_HasNoReferences(string type)
    {
        var schema = type switch
        {
            nameof(ActivityDrafts.MatrixDraft) => await CaptureAsync<ActivityDrafts.MatrixDraft>(),
            nameof(ActivityDrafts.SlotDraft) => await CaptureAsync<ActivityDrafts.SlotDraft>(),
            nameof(ActivityDrafts.DragDraft) => await CaptureAsync<ActivityDrafts.DragDraft>(),
            nameof(ActivityDrafts.MatchDraft) => await CaptureAsync<ActivityDrafts.MatchDraft>(),
            nameof(ClaudeLabelDetector.PageBlocks) => await CaptureAsync<ClaudeLabelDetector.PageBlocks>(),
            nameof(ClaudeLabelDetector.OptionBoxes) => await CaptureAsync<ClaudeLabelDetector.OptionBoxes>(),
            _ => throw new ArgumentOutOfRangeException(nameof(type)),
        };

        schema.ShouldNotBeNullOrEmpty();
        schema.ShouldNotContain("$ref");
        schema.ShouldNotContain("$defs");
        schema.ShouldNotContain("definitions");
    }

    private static async Task<string?> CaptureAsync<T>()
    {
        var chat = new CapturingChatClient();
        await chat.GetResponseAsync<T>([new ChatMessage(ChatRole.User, "x")], new ChatOptions { ModelId = "claude-test" });
        return chat.Schema;
    }

    private sealed class CapturingChatClient : IChatClient
    {
        public string? Schema { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default)
        {
            Schema = (options?.ResponseFormat as ChatResponseFormatJson)?.Schema?.GetRawText();
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, "{}")));
        }

        public IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }
}
