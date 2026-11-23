using System.Text.Json;
using AssistantService.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;

namespace AssistantService.Endpoints;

/// <summary>
/// Issue #505: the user's own conversation history. Owner only: someone else's conversation is 404, never 403 (no
/// existence leak). No admin read of conversations exists; admins get aggregate numbers only.
/// </summary>
public static class ConversationEndpoints
{
    public const int MaxListed = 50;

    public static WebApplication MapConversationEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(AssistantGate.PathPrefix + "/conversations").RequireAuthorization();

        group.MapGet("", async (HttpContext http, AssistantDbContext db) =>
        {
            var sub = ToolEndpoints.ContextFrom(http).UserSub;
            var items = await db.Conversations.AsNoTracking()
                .Where(c => c.UserSub == sub)
                .OrderByDescending(c => c.UpdatedAt).ThenByDescending(c => c.Id)
                .Take(MaxListed)
                .Select(c => new { id = c.Id, title = c.Title, createdAt = c.CreatedAt, updatedAt = c.UpdatedAt })
                .ToListAsync(http.RequestAborted);
            return Results.Ok(new { items });
        });

        group.MapGet("/{id:guid}", async (Guid id, HttpContext http, AssistantDbContext db) =>
        {
            var sub = ToolEndpoints.ContextFrom(http).UserSub;
            var conversation = await db.Conversations.AsNoTracking()
                .FirstOrDefaultAsync(c => c.Id == id && c.UserSub == sub, http.RequestAborted);
            if (conversation is null)
                return Results.NotFound(new { code = "ConversationNotFound" });

            // Only what the user saw as chat: their messages and the assistant's text. Tool calls / results stay server side.
            var rows = await db.Messages.AsNoTracking()
                .Where(m => m.ConversationId == id && (m.Role == "user" || m.Role == "assistant"))
                .OrderBy(m => m.Id)
                .Select(m => new { m.Id, m.Role, m.ContentJson, m.CreatedAt })
                .ToListAsync(http.RequestAborted);
            var messages = rows
                .Select(m => new { id = m.Id, role = m.Role, text = TextOf(m.ContentJson), createdAt = m.CreatedAt })
                .Where(m => m.text.Length > 0)
                .ToList();
            return Results.Ok(new
            {
                id = conversation.Id,
                title = conversation.Title,
                createdAt = conversation.CreatedAt,
                updatedAt = conversation.UpdatedAt,
                messages,
            });
        });

        group.MapDelete("/{id:guid}", async (Guid id, HttpContext http, AssistantDbContext db) =>
        {
            var sub = ToolEndpoints.ContextFrom(http).UserSub;
            var deleted = await db.Conversations.Where(c => c.Id == id && c.UserSub == sub).ExecuteDeleteAsync(http.RequestAborted);
            return deleted == 0 ? Results.NotFound(new { code = "ConversationNotFound" }) : Results.NoContent();
        });

        // "Clear my history": every conversation of the caller (messages, pending actions, feedback cascade).
        group.MapDelete("", async (HttpContext http, AssistantDbContext db) =>
        {
            var sub = ToolEndpoints.ContextFrom(http).UserSub;
            var deleted = await db.Conversations.Where(c => c.UserSub == sub).ExecuteDeleteAsync(http.RequestAborted);
            return Results.Ok(new { deleted });
        });

        return app;
    }

    internal static string TextOf(string contentJson)
    {
        try
        {
            var contents = JsonSerializer.Deserialize<List<AIContent>>(contentJson, AIJsonUtilities.DefaultOptions) ?? [];
            return string.Concat(contents.OfType<TextContent>().Select(t => t.Text)).Trim();
        }
        catch (JsonException)
        {
            return "";
        }
    }
}
