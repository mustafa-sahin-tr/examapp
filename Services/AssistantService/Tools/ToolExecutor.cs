using System.Text.Json;
using System.Text.Json.Nodes;
using AssistantService.Data;
using Microsoft.EntityFrameworkCore;

namespace AssistantService.Tools;

/// <summary>Issue #503: every tool, defined once; the chat loop (#504) and later /mcp (#518) read them from here.</summary>
public sealed class ToolRegistry(IEnumerable<IAssistantTool> tools)
{
    private readonly Dictionary<string, IAssistantTool> _byName = tools.ToDictionary(t => t.Name, StringComparer.Ordinal);

    public IAssistantTool? Get(string name) => _byName.GetValueOrDefault(name);

    /// <summary>Role filter: the model only sees the caller's tools.</summary>
    public IReadOnlyList<IAssistantTool> ForRoles(IReadOnlyCollection<string> roles) =>
        _byName.Values.Where(t => t.Roles.Any(roles.Contains)).OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
}

/// <summary>Outcome of a model's tool call: a result, or (write tools) a pending action waiting for the user.</summary>
public sealed record ToolInvocation(ToolResult Result, Guid? PendingActionId = null);

public enum ActionDecisionError
{
    None,
    NotFound,
    Expired,
    AlreadyDecided,
}

public sealed class ToolExecutor(ToolRegistry registry, AssistantDbContext db, TimeProvider clock, ILogger<ToolExecutor> logger)
{
    public static readonly TimeSpan PendingActionLifetime = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Runs a tool call. The role is checked again here (never trust the model's tool list). A write tool only previews and
    /// stores a <see cref="PendingAction"/> owned by the caller; nothing changes until <see cref="ConfirmAsync"/>.
    /// </summary>
    public async Task<ToolInvocation> InvokeAsync(ToolContext context, string name, JsonElement args)
    {
        var tool = registry.Get(name);
        if (tool is null || !tool.Roles.Any(context.Roles.Contains))
            return new(ToolResult.Error("ToolNotAllowed", $"Unknown tool or not allowed: {name}"));

        ToolResult preview;
        try
        {
            preview = await tool.ExecuteAsync(context, args);
        }
        catch (HttpRequestException ex)
        {
            logger.LogWarning(ex, "[Assistant] tool {Tool} downstream call failed", name);
            return new(ToolResult.Error("DownstreamUnavailable"));
        }
        if (tool is not IAssistantWriteTool || !preview.Ok)
            return new(preview);

        var now = clock.GetUtcNow().UtcDateTime;
        var action = new PendingAction
        {
            Id = Guid.NewGuid(),
            ConversationId = context.ConversationId ?? throw new InvalidOperationException("Write tools need a conversation."),
            UserSub = context.UserSub,
            Tool = name,
            ArgumentsJson = args.ValueKind == JsonValueKind.Undefined ? "{}" : args.GetRawText(),
            PreviewJson = preview.Data?.ToJsonString(),
            CreatedAt = now,
            ExpiresAt = now + PendingActionLifetime,
        };
        db.PendingActions.Add(action);
        await db.SaveChangesAsync(context.CancellationToken);

        var card = new JsonObject
        {
            ["kind"] = "confirm",
            ["pendingActionId"] = action.Id.ToString(),
            ["tool"] = name,
            ["preview"] = preview.Data?.DeepClone(),
            ["expiresAt"] = action.ExpiresAt,
        };
        var data = new JsonObject
        {
            ["requiresConfirmation"] = true,
            ["pendingActionId"] = action.Id.ToString(),
            ["preview"] = preview.Data?.DeepClone(),
        };
        return new(ToolResult.Success(data, [card]), action.Id);
    }

    /// <summary>
    /// The user tapped "confirm" on the card. Only the owner's own pending, unexpired action runs, once; another user's id
    /// is NotFound (no hint it exists).
    /// </summary>
    public async Task<(ActionDecisionError Error, ToolResult? Result)> ConfirmAsync(ToolContext context, Guid id)
    {
        var (error, action) = await DecideAsync(context, id, PendingActionStatus.Confirmed);
        if (error != ActionDecisionError.None)
            return (error, null);

        var tool = registry.Get(action!.Tool) as IAssistantWriteTool;
        ToolResult result;
        if (tool is null || !tool.Roles.Any(context.Roles.Contains))
            result = ToolResult.Error("ToolNotAllowed");
        else
        {
            using var args = JsonDocument.Parse(action.ArgumentsJson);
            try
            {
                result = await tool.CommitAsync(context, args.RootElement);
            }
            catch (HttpRequestException ex)
            {
                logger.LogWarning(ex, "[Assistant] confirm {Tool} downstream call failed", action.Tool);
                result = ToolResult.Error("DownstreamUnavailable");
            }
        }
        if (!result.Ok)
        {
            action.Status = PendingActionStatus.Failed;
            await db.SaveChangesAsync(CancellationToken.None);
        }
        return (ActionDecisionError.None, result);
    }

    public async Task<ActionDecisionError> RejectAsync(ToolContext context, Guid id)
    {
        var (error, _) = await DecideAsync(context, id, PendingActionStatus.Rejected);
        return error;
    }

    /// <summary>
    /// Claims the action with one conditional UPDATE (owner, still pending, not expired), so a double tap or two parallel
    /// requests cannot both run it.
    /// </summary>
    private async Task<(ActionDecisionError, PendingAction?)> DecideAsync(ToolContext context, Guid id, PendingActionStatus target)
    {
        var now = clock.GetUtcNow().UtcDateTime;
        var claimed = await db.PendingActions
            .Where(a => a.Id == id && a.UserSub == context.UserSub && a.Status == PendingActionStatus.Pending && a.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, target).SetProperty(a => a.DecidedAt, now), context.CancellationToken);
        var action = await db.PendingActions.AsTracking()
            .FirstOrDefaultAsync(a => a.Id == id && a.UserSub == context.UserSub, context.CancellationToken);
        if (action is null)
            return (ActionDecisionError.NotFound, null);
        if (claimed == 1)
            return (ActionDecisionError.None, action);
        if (action.Status is PendingActionStatus.Pending or PendingActionStatus.Expired)
        {
            if (action.Status == PendingActionStatus.Pending)
            {
                action.Status = PendingActionStatus.Expired;
                await db.SaveChangesAsync(context.CancellationToken);
            }
            return (ActionDecisionError.Expired, null);
        }
        return (ActionDecisionError.AlreadyDecided, null);
    }
}
