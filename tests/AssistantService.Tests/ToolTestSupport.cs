using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using AssistantService.Data;
using AssistantService.Tools;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AssistantService.Tests;

/// <summary>
/// Fake exam API: answers by "METHOD path" (path without the leading slash, with query) and records calls. A route
/// registered with <see cref="OnAny"/> answers that path whatever the query, computed from the request (the golden set, #509).
/// </summary>
public sealed class FakeExamApi : HttpMessageHandler
{
    public sealed record Call(string Method, string PathAndQuery, string? Authorization, string? AcceptLanguage, string? Body);

    private readonly Dictionary<string, (HttpStatusCode Status, string? Json)> _routes = new();
    public List<Call> Calls { get; } = [];

    public FakeExamApi On(string method, string pathAndQuery, object? body, HttpStatusCode status = HttpStatusCode.OK)
    {
        _routes[$"{method} {pathAndQuery}"] = (status, body is null ? null : body as string ?? JsonSerializer.Serialize(body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        return this;
    }

    public FakeExamApi OnAny(string method, string path, Func<Uri, string?, object?> respond, HttpStatusCode status = HttpStatusCode.OK)
    {
        _computed[$"{method} {path}"] = (respond, status);
        return this;
    }

    private readonly Dictionary<string, (Func<Uri, string?, object?> Respond, HttpStatusCode Status)> _computed = new();

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri!.PathAndQuery.TrimStart('/');
        Calls.Add(new Call(request.Method.Method, path, request.Headers.Authorization?.ToString(),
            request.Headers.AcceptLanguage.ToString(), request.Content is null ? null : await request.Content.ReadAsStringAsync(ct)));
        var body = Calls[^1].Body;
        if (!_routes.TryGetValue($"{request.Method.Method} {path}", out var route))
        {
            if (!_computed.TryGetValue($"{request.Method.Method} {request.RequestUri.AbsolutePath.TrimStart('/')}", out var respond))
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            var computed = respond.Respond(request.RequestUri, body);
            route = (respond.Status, computed is null ? null : JsonSerializer.Serialize(computed, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        }
        var response = new HttpResponseMessage(route.Status);
        if (route.Json is not null)
            response.Content = new StringContent(route.Json, Encoding.UTF8, "application/json");
        return response;
    }
}

public sealed class ManualClock(DateTimeOffset now) : TimeProvider
{
    public DateTimeOffset Now { get; set; } = now;
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Tool host wired like Program.cs, on SQLite and a fake exam API.</summary>
public sealed class ToolHarness : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public FakeExamApi Api { get; } = new();
    public ManualClock Clock { get; } = new(new DateTimeOffset(2026, 11, 23, 10, 0, 0, TimeSpan.Zero));
    public DownstreamClient Downstream { get; }

    /// <summary>#510: the explanation quota's counters and limit.</summary>
    public AssistantService.Chat.InMemoryCounterStore Counters { get; }
    public AssistantService.AssistantOptions Options { get; } = new();

    public ToolHarness()
    {
        _connection.Open();
        using var db = NewDb();
        db.Database.EnsureCreated();
        Downstream = new DownstreamClient(new HttpClient(Api) { BaseAddress = new Uri("http://exam-api.test/") });
        Counters = new AssistantService.Chat.InMemoryCounterStore(Clock);
    }

    public AssistantDbContext NewDb() =>
        new(new DbContextOptionsBuilder<AssistantDbContext>().UseSqlite(_connection).Options);

    public IReadOnlyList<IAssistantTool> AllTools() =>
    [
        new GetMyProfileTool(Downstream), new SearchTaxonomyTool(Downstream), new GetMyWeakTopicsTool(Downstream),
        new GetMyPerformanceSummaryTool(Downstream), new GetDailySetStatusTool(Downstream), new ListMyCompletedTestsTool(Downstream),
        new GetTestResultTool(Downstream), new StartDailyPracticeTool(Downstream), new StartPracticeSessionTool(Downstream),
        new ExplainQuestionTool(Downstream, Counters, new StaticOptions<AssistantService.AssistantOptions>(Options), Clock),
        new GetMyRecentMistakesTool(Downstream),
        new SearchHelpTool(Guide), new UiOpenScreenTool(), new UiSuggestActionsTool(),
        new ListMyChildrenTool(Downstream), new GetChildWeeklySummaryTool(Downstream), new ListChildAssignmentsTool(Downstream),
        new GetChildTestResultTool(Downstream), new GetChildWeakTopicsTool(Downstream), new GetChildProgressTool(Downstream),
        new GetChildScheduleTool(Downstream),
    ];

    /// <summary>Issue #1336: the shared tools (help, screens, chips) serve both roles.</summary>
    public static readonly string[] SharedTools = ["search_help", "ui_open_screen", "ui_suggest_actions"];

    /// <summary>The real embedded guide (#506); loaded once, it is immutable.</summary>
    public static readonly AssistantService.Help.HelpIndex Guide = AssistantService.Help.HelpIndex.LoadEmbedded();

    public ToolExecutor Executor(AssistantDbContext db) =>
        new(new ToolRegistry(AllTools()), db, Clock, NullLogger<ToolExecutor>.Instance);

    public static ToolContext Student(string sub = "student-1", Guid? conversationId = null) =>
        new(sub, ["Student"], "token-" + sub, "tr-TR", conversationId);

    public static ToolContext Parent(string sub = "parent-1", Guid? conversationId = null) =>
        new(sub, ["Parent"], "token-" + sub, "tr-TR", conversationId);

    public async Task<Guid> ConversationAsync(string sub = "student-1")
    {
        await using var db = NewDb();
        var c = new Conversation { Id = Guid.NewGuid(), UserSub = sub, Role = "Student", CreatedAt = Clock.Now.UtcDateTime, UpdatedAt = Clock.Now.UtcDateTime };
        db.Conversations.Add(c);
        await db.SaveChangesAsync();
        return c.Id;
    }

    public static JsonElement Args(object? args = null) =>
        JsonDocument.Parse(args is null ? "{}" : JsonSerializer.Serialize(args)).RootElement;

    public void Dispose() => _connection.Dispose();
}
