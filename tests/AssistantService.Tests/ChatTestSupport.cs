using System.Runtime.CompilerServices;
using System.Text.Json;
using AssistantService.Chat;
using AssistantService.Data;
using AssistantService.Tools;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AssistantService.Tests;

/// <summary>Scripted model: each call streams the next scripted list of updates and records what it was sent.</summary>
public sealed class FakeChatClient : IChatClient
{
    private readonly Queue<List<ChatResponseUpdate>> _script = new();
    public List<(List<ChatMessage> Messages, ChatOptions? Options)> Calls { get; } = [];

    public FakeChatClient Then(params ChatResponseUpdate[] updates)
    {
        _script.Enqueue([.. updates]);
        return this;
    }

    public static ChatResponseUpdate Text(string text) => new(ChatRole.Assistant, text);

    public static ChatResponseUpdate Call(string id, string name, object? args = null) => new()
    {
        Role = ChatRole.Assistant,
        Contents = [new FunctionCallContent(id, name,
            args is null ? new Dictionary<string, object?>() : JsonSerializer.Deserialize<Dictionary<string, object?>>(JsonSerializer.Serialize(args)))],
    };

    public static ChatResponseUpdate Usage(long input, long output, long cacheRead = 0, long cacheWrite = 0) => new()
    {
        Role = ChatRole.Assistant,
        Contents = [new UsageContent(new UsageDetails
        {
            InputTokenCount = input + cacheRead + cacheWrite,
            OutputTokenCount = output,
            CachedInputTokenCount = cacheRead,
            AdditionalCounts = cacheWrite == 0 ? null : new AdditionalPropertiesDictionary<long> { ["CacheCreationInputTokens"] = cacheWrite },
        })],
    };

    public Task<ChatResponse> GetResponseAsync(IEnumerable<ChatMessage> messages, ChatOptions? options = null, CancellationToken ct = default) =>
        throw new NotSupportedException();

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(IEnumerable<ChatMessage> messages,
        ChatOptions? options = null, [EnumeratorCancellation] CancellationToken ct = default)
    {
        Calls.Add(([.. messages], options is null ? null : options.Clone()));
        if (!_script.TryDequeue(out var updates))
            throw new InvalidOperationException("No scripted response left.");
        foreach (var u in updates)
        {
            await Task.Yield();
            yield return u;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}

public sealed class ListSink : IChatEventSink
{
    public List<(string Type, JsonElement Payload)> Events { get; } = [];

    public Task SendAsync(string type, object payload)
    {
        Events.Add((type, JsonSerializer.SerializeToElement(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
        return Task.CompletedTask;
    }

    public IEnumerable<string> Types => Events.Select(e => e.Type);
    public string Text => string.Concat(Events.Where(e => e.Type == "text").Select(e => e.Payload.GetProperty("delta").GetString()));
}

public sealed class StaticOptions<T>(T value) : IOptionsMonitor<T>
{
    public T CurrentValue { get; } = value;
    public T Get(string? name) => CurrentValue;
    public IDisposable? OnChange(Action<T, string?> listener) => null;
}

/// <summary>
/// Chat runner wired like Program.cs on the tool harness (SQLite, fake exam API) and a scripted model — or a real model
/// client (the golden set, #509).
/// </summary>
public sealed class ChatHarness(IChatClient? client = null) : IDisposable
{
    private readonly IChatClient? _client = client;
    public ToolHarness Tools { get; } = new();
    public FakeChatClient Model { get; } = new();
    public AssistantOptions Options { get; } = new() { Enabled = true, Roles = ["Student"], MaxToolCallsPerTurn = 3 };

    public InMemoryCounterStore Counters => _counters ??= new InMemoryCounterStore(Tools.Clock);
    public UsageLimiter Limiter => _limiter ??= new UsageLimiter(Counters, new StaticOptions<AssistantOptions>(Options), Tools.Clock);
    private InMemoryCounterStore? _counters;
    private UsageLimiter? _limiter;

    public ChatTurnRunner Runner(AssistantDbContext db) => new(db, new ToolRegistry(Tools.AllTools()), Tools.Executor(db),
        new ChatModel(_client ?? Model), Limiter, new StaticOptions<AssistantOptions>(Options), Tools.Clock, NullLogger<ChatTurnRunner>.Instance);

    /// <summary>Runs one turn as <paramref name="sub"/>; returns the events.</summary>
    public async Task<ListSink> TurnAsync(string message, Guid? conversationId = null, string sub = "student-1", ChatContext? context = null)
    {
        await using var db = Tools.NewDb();
        var runner = Runner(db);
        var tc = ToolHarness.Student(sub);
        var conversation = await runner.LoadOrCreateAsync(tc, conversationId, default) ?? throw new InvalidOperationException("not found");
        var sink = new ListSink();
        await runner.RunAsync(tc, conversation, new ChatRequest(conversationId, message, context), sink);
        return sink;
    }

    public static Guid ConversationOf(ListSink sink) =>
        sink.Events.Single(e => e.Type == "done").Payload.GetProperty("conversationId").GetGuid();

    public void Dispose() => Tools.Dispose();
}
