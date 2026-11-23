using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Anthropic.Models.Messages;
using AssistantService.Data;
using AssistantService.Tools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AiChatRole = Microsoft.Extensions.AI.ChatRole;
using DbMessage = AssistantService.Data.Message;

namespace AssistantService.Chat;

/// <summary>The model client, or none when <c>Anthropic:ApiKey</c> is empty (the gate then answers 503).</summary>
public sealed class ChatModel(IChatClient? client)
{
    public IChatClient? Client { get; } = client;
}

/// <summary>Where a turn's events go (SSE in production, a list in tests).</summary>
public interface IChatEventSink
{
    Task SendAsync(string type, object payload);
}

public sealed record ChatRequest(Guid? ConversationId, string Message, ChatContext? Context);

public readonly record struct TurnUsage(long InputTokens, long CacheWriteTokens, long CacheReadTokens, long OutputTokens, decimal CostUsd)
{
    public static TurnUsage operator +(TurnUsage a, TurnUsage b) => new(a.InputTokens + b.InputTokens,
        a.CacheWriteTokens + b.CacheWriteTokens, a.CacheReadTokens + b.CacheReadTokens, a.OutputTokens + b.OutputTokens, a.CostUsd + b.CostUsd);
}

/// <summary>
/// Issue #504: one chat turn. The user's message and the model's replies, tool calls and tool results are stored only when
/// the turn ends normally (a new conversation row is created first), so a turn cut by an expired token (the client refreshes and resends) leaves no half turn.
/// Message text and tool results are never logged — only tool names, durations, tokens, cost and a hash of the message.
/// </summary>
public sealed class ChatTurnRunner(
    AssistantDbContext db,
    ToolRegistry registry,
    ToolExecutor executor,
    ChatModel model,
    UsageLimiter limiter,
    IOptionsMonitor<AssistantOptions> options,
    TimeProvider clock,
    ILogger<ChatTurnRunner> logger)
{
    public const int MaxMessageLength = 2000;
    private const decimal CacheWrite5mMultiplier = 1.25m;
    private const decimal CacheWrite1hMultiplier = 2m;
    private const decimal CacheReadMultiplier = 0.1m;

    private static readonly JsonSerializerOptions Json = AIJsonUtilities.DefaultOptions;

    /// <summary>Routes where the assistant is closed (a test or activity being solved) — refused on the server too.</summary>
    public static bool IsClosedRoute(string? route) =>
        route is not null && (route.StartsWith("/testsolve", StringComparison.OrdinalIgnoreCase)
            || route.StartsWith("/activity/", StringComparison.OrdinalIgnoreCase));

    /// <summary>The caller's conversation, a new one (null id), or null when the id is not the caller's.</summary>
    public async Task<Conversation?> LoadOrCreateAsync(ToolContext context, Guid? id, CancellationToken ct)
    {
        if (id is { } existing)
            return await db.Conversations.AsTracking().FirstOrDefaultAsync(c => c.Id == existing && c.UserSub == context.UserSub, ct);
        var now = clock.GetUtcNow().UtcDateTime;
        return new Conversation
        {
            Id = Guid.NewGuid(), UserSub = context.UserSub, Role = PrimaryRole(context.Roles), CreatedAt = now, UpdatedAt = now,
        };
    }

    public async Task RunAsync(ToolContext baseContext, Conversation conversation, ChatRequest request, IChatEventSink sink)
    {
        var o = options.CurrentValue;
        var ct = baseContext.CancellationToken;
        var context = baseContext with { ConversationId = conversation.Id };
        var client = model.Client ?? throw new InvalidOperationException("Assistant model not configured.");
        var isNew = db.Entry(conversation).State == EntityState.Detached;
        var now = clock.GetUtcNow();

        var history = isNew ? [] : await LoadHistoryAsync(conversation.Id, o.MaxHistoryMessages, ct);
        if (isNew)
        {
            // Stored first: pending actions of this turn reference it. Removed again if the turn is aborted.
            conversation.Title = request.Message.Length <= 60 ? request.Message.Trim() : request.Message[..60].Trim() + "…";
            db.Conversations.Add(conversation);
            await db.SaveChangesAsync(ct);
        }
        var userMessage = new AiChatMessage(AiChatRole.User, request.Message.Trim());
        List<AiChatMessage> messages =
        [
            new(AiChatRole.System,
            [
                // Cache breakpoint: tools + stable prompt are identical for every turn of the role.
                new TextContent(SystemPrompt.Stable(conversation.Role)).WithCacheControl(o.CacheTtl == "1h" ? Ttl.Ttl1h : Ttl.Ttl5m),
                new TextContent(SystemPrompt.Context(request.Context, now)),
            ]),
            .. history,
            userMessage,
        ];
        var turnMessages = new List<(AiChatMessage Message, TurnUsage Usage)> { (userMessage, default) };

        var chatOptions = new ChatOptions
        {
            ModelId = o.Model,
            MaxOutputTokens = o.MaxOutputTokens,
            Tools = ToolDeclarations(context.Roles),
            ToolMode = ChatToolMode.Auto,
            Reasoning = Effort(o.Effort) is { } effort ? new ReasoningOptions { Effort = effort } : null,
        };

        var saved = false;
        try
        {
            var total = default(TurnUsage);
            var toolCalls = 0;
            var crisis = false;
            var turnWatch = Stopwatch.StartNew();
            TimeSpan? firstToken = null;
            var hash = Hash(request.Message);

            for (var round = 0; ; round++)
            {
                if (round > 0 && !await limiter.HasBudgetAsync(context.UserSub))
                {
                    await sink.SendAsync("error", new { code = "AssistantBudgetExceeded" });
                    break;
                }

                var updates = new List<ChatResponseUpdate>();
                await foreach (var update in client.GetStreamingResponseAsync(messages, chatOptions, ct))
                {
                    updates.Add(update);
                    var text = update.Text;
                    if (!string.IsNullOrEmpty(text))
                    {
                        firstToken ??= turnWatch.Elapsed;
                        await sink.SendAsync("text", new { delta = text });
                    }
                }
                var response = updates.ToChatResponse();
                var usage = ReadUsage(response.Usage, o);
                total += usage;
                await limiter.RecordSpendAsync(context.UserSub, usage.CostUsd);

                for (var i = 0; i < response.Messages.Count; i++)
                    turnMessages.Add((response.Messages[i], i == response.Messages.Count - 1 ? usage : default));
                messages.AddRange(response.Messages);

                if (response.FinishReason == ChatFinishReason.ContentFilter)
                {
                    await sink.SendAsync("error", new { code = "AssistantDeclined" });
                    break;
                }

                var calls = response.Messages.SelectMany(m => m.Contents).OfType<FunctionCallContent>().ToList();
                if (calls.Count == 0)
                    break;

                var results = new List<AIContent>();
                foreach (var call in calls)
                {
                    if (call.Name == SystemPrompt.CrisisToolName)
                    {
                        crisis = true;
                        results.Add(new FunctionResultContent(call.CallId, "shown"));
                        continue;
                    }
                    if (toolCalls >= o.MaxToolCallsPerTurn)
                    {
                        results.Add(new FunctionResultContent(call.CallId, ModelResult(ToolResult.Error("ToolCallLimitReached"))));
                        continue;
                    }
                    toolCalls++;
                    var result = await InvokeToolAsync(context, call, sink, hash);
                    if (result is null)
                        return; // token expired: nothing of the turn is stored; the client refreshes and resends it
                    results.Add(new FunctionResultContent(call.CallId, ModelResult(result)));
                }
                var toolMessage = new AiChatMessage(AiChatRole.Tool, results);
                messages.Add(toolMessage);
                turnMessages.Add((toolMessage, default));

                if (crisis)
                {
                    // #496: fixed safe answer, not generated.
                    await sink.SendAsync("text", new { delta = SystemPrompt.CrisisResponse });
                    var fixedAnswer = new AiChatMessage(AiChatRole.Assistant, SystemPrompt.CrisisResponse);
                    turnMessages.Add((fixedAnswer, default));
                    break;
                }
                if (toolCalls >= o.MaxToolCallsPerTurn)
                    chatOptions.ToolMode = ChatToolMode.None; // one more call to answer, no further tools
            }

            var lastAssistantId = await SaveTurnAsync(conversation, turnMessages, total, o.Model);
            saved = true;
            logger.LogInformation(
                "[Assistant] turn {Hash} conversation {ConversationId}: tools={ToolCalls} firstTokenMs={FirstTokenMs} totalMs={TotalMs} " +
                "input={Input} cacheWrite={CacheWrite} cacheRead={CacheRead} output={Output} costUsd={Cost} crisis={Crisis} prompt=r{Revision}",
                hash, conversation.Id, toolCalls, (long?)firstToken?.TotalMilliseconds, (long)turnWatch.Elapsed.TotalMilliseconds,
                total.InputTokens, total.CacheWriteTokens, total.CacheReadTokens, total.OutputTokens, total.CostUsd, crisis, SystemPrompt.Revision);
            await sink.SendAsync("done", new
            {
                conversationId = conversation.Id,
                messageId = lastAssistantId,
                usage = new
                {
                    inputTokens = total.InputTokens, cacheWriteTokens = total.CacheWriteTokens, cacheReadTokens = total.CacheReadTokens,
                    outputTokens = total.OutputTokens, costUsd = total.CostUsd,
                },
            });
        }
        finally
        {
            // An aborted first turn (expired token, model error, client gone) leaves no empty conversation behind.
            if (isNew && !saved)
            {
                db.ChangeTracker.Clear();
                await db.Conversations.Where(c => c.Id == conversation.Id).ExecuteDeleteAsync(CancellationToken.None);
            }
        }
    }

    /// <summary>Runs one tool call and streams its events. Null = the user's token expired (turn aborted).</summary>
    private async Task<ToolResult?> InvokeToolAsync(ToolContext context, FunctionCallContent call, IChatEventSink sink, string hash)
    {
        await sink.SendAsync("tool_start", new { tool = call.Name });
        var watch = Stopwatch.StartNew();
        var args = JsonSerializer.SerializeToElement(call.Arguments ?? new Dictionary<string, object?>(), Json);
        var invocation = await executor.InvokeAsync(context, call.Name, args);
        var result = invocation.Result;
        logger.LogInformation("[Assistant] turn {Hash} tool {Tool} ok={Ok} errorCode={ErrorCode} ms={Ms}",
            hash, call.Name, result.Ok, result.ErrorCode, watch.ElapsedMilliseconds);

        if (result.ErrorCode == "Unauthorized")
        {
            await sink.SendAsync("error", new { code = "TokenExpired" });
            return null;
        }
        await sink.SendAsync("tool_end", new { tool = call.Name, ok = result.Ok, errorCode = result.ErrorCode, ms = watch.ElapsedMilliseconds });
        foreach (var card in result.Cards ?? [])
            await sink.SendAsync(card["kind"]?.GetValue<string>() == "confirm" ? "confirm_required" : "card", card);
        foreach (var action in result.UiActions ?? [])
            await sink.SendAsync("ui_action", action);
        return result;
    }

    /// <summary>What the model sees of a tool result: data or the error, and which panel elements the user already sees.</summary>
    internal static JsonElement ModelResult(ToolResult result)
    {
        var o = new JsonObject { ["ok"] = result.Ok };
        if (result.Ok)
            o["data"] = result.Data?.DeepClone();
        else
        {
            o["errorCode"] = result.ErrorCode;
            if (result.Message is not null)
                o["message"] = result.Message;
        }
        if (result.Cards is { Count: > 0 } cards)
            o["shownToUser"] = new JsonArray(cards.Select(c => (JsonNode?)c["kind"]?.DeepClone()).ToArray());
        return JsonSerializer.SerializeToElement(o);
    }

    private IList<AITool> ToolDeclarations(IReadOnlyCollection<string> roles)
    {
        var tools = registry.ForRoles(roles)
            .Select(t => (AITool)AIFunctionFactory.CreateDeclaration(t.Name, t.Description,
                JsonSerializer.SerializeToElement(t.InputSchema)))
            .ToList();
        tools.Add(AIFunctionFactory.CreateDeclaration(SystemPrompt.CrisisToolName,
            "Show the fixed safety message when the user expresses a personal crisis. Write nothing else.",
            JsonSerializer.SerializeToElement(new { type = "object", properties = new { }, additionalProperties = false })));
        return tools; // deterministic order (registry sorts by name): keeps the cached prefix stable
    }

    private async Task<List<AiChatMessage>> LoadHistoryAsync(Guid conversationId, int max, CancellationToken ct)
    {
        var rows = await db.Messages.Where(m => m.ConversationId == conversationId)
            .OrderByDescending(m => m.Id).Take(max).ToListAsync(ct);
        rows.Reverse();
        var history = rows.Select(r => new AiChatMessage(new AiChatRole(r.Role),
            JsonSerializer.Deserialize<List<AIContent>>(r.ContentJson, Json) ?? [])).ToList();
        // Never start with a tool result or a reply whose call was cut off: begin at a plain user message.
        var start = history.FindIndex(m => m.Role == AiChatRole.User && m.Contents.OfType<TextContent>().Any());
        return start < 0 ? [] : history[start..];
    }

    private async Task<long?> SaveTurnAsync(Conversation conversation,
        List<(AiChatMessage Message, TurnUsage Usage)> turn, TurnUsage total, string modelId)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        conversation.UpdatedAt = now;

        var rows = turn.Select(t => new DbMessage
        {
            ConversationId = conversation.Id,
            Role = t.Message.Role.Value,
            ContentJson = JsonSerializer.Serialize(t.Message.Contents, Json),
            Model = t.Message.Role == AiChatRole.Assistant ? modelId : null,
            InputTokens = (int)(t.Usage.InputTokens + t.Usage.CacheWriteTokens),
            CacheReadTokens = (int)t.Usage.CacheReadTokens,
            OutputTokens = (int)t.Usage.OutputTokens,
            CostUsd = t.Usage.CostUsd,
            CreatedAt = now,
        }).ToList();
        db.Messages.AddRange(rows);

        var day = DateOnly.FromDateTime(now);
        var usageDay = await db.Usage.AsTracking().FirstOrDefaultAsync(u => u.UserSub == conversation.UserSub && u.Day == day, CancellationToken.None);
        if (usageDay is null)
        {
            usageDay = new UsageDay { UserSub = conversation.UserSub, Day = day };
            db.Usage.Add(usageDay);
        }
        usageDay.Messages++;
        usageDay.InputTokens += (int)(total.InputTokens + total.CacheWriteTokens + total.CacheReadTokens);
        usageDay.OutputTokens += (int)total.OutputTokens;
        usageDay.CostUsd += total.CostUsd;

        // The turn is stored even if the client went away mid-stream: the spend happened.
        await db.SaveChangesAsync(CancellationToken.None);
        return rows.LastOrDefault(r => r.Role == AiChatRole.Assistant.Value)?.Id;
    }

    /// <summary>Usage split the way Anthropic bills it. The bridge reports cache writes in <c>AdditionalCounts</c>.</summary>
    internal static TurnUsage ReadUsage(UsageDetails? usage, AssistantOptions o)
    {
        if (usage is null)
            return default;
        var cacheRead = usage.CachedInputTokenCount ?? 0;
        var cacheWrite = usage.AdditionalCounts?
            .Where(kv => kv.Key.Contains("cache", StringComparison.OrdinalIgnoreCase)
                && (kv.Key.Contains("creat", StringComparison.OrdinalIgnoreCase) || kv.Key.Contains("write", StringComparison.OrdinalIgnoreCase)))
            .Sum(kv => kv.Value) ?? 0;
        var input = Math.Max(0, (usage.InputTokenCount ?? 0) - cacheRead - cacheWrite);
        var output = usage.OutputTokenCount ?? 0;
        var price = o.InputUsdPerMTok / 1_000_000m;
        var writeMultiplier = o.CacheTtl == "1h" ? CacheWrite1hMultiplier : CacheWrite5mMultiplier;
        var cost = input * price + cacheWrite * price * writeMultiplier + cacheRead * price * CacheReadMultiplier
            + output * (o.OutputUsdPerMTok / 1_000_000m);
        return new TurnUsage(input, cacheWrite, cacheRead, output, Math.Round(cost, 6));
    }

    private static ReasoningEffort? Effort(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "low" => ReasoningEffort.Low,
        "medium" => ReasoningEffort.Medium,
        "high" => ReasoningEffort.High,
        _ => null,
    };

    private static string PrimaryRole(IReadOnlyCollection<string> roles) =>
        new[] { "Student", "Parent", "Teacher", "Admin" }.FirstOrDefault(roles.Contains) ?? roles.FirstOrDefault() ?? "";

    private static string Hash(string text) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)))[..12].ToLowerInvariant();
}
