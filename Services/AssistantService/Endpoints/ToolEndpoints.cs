using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Nodes;
using AssistantService.Data;
using AssistantService.Security;
using AssistantService.Tools;

namespace AssistantService.Endpoints;

public static class ToolEndpoints
{
    public static ToolContext ContextFrom(HttpContext http, Guid? conversationId = null)
    {
        var user = http.User;
        var sub = user.FindFirst("sub")?.Value ?? user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "";
        var roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).Distinct().ToArray();
        var header = http.Request.Headers.Authorization.ToString();
        var token = header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) ? header[7..].Trim() : "";
        return new ToolContext(sub, roles, token, http.Request.Headers.AcceptLanguage.ToString(), conversationId, http.RequestAborted);
    }

    public static object ToJson(ToolResult r) => new
    {
        ok = r.Ok,
        data = r.Data,
        errorCode = r.ErrorCode,
        message = r.Message,
        cards = r.Cards,
        uiActions = r.UiActions,
    };

    public static WebApplication MapToolEndpoints(this WebApplication app)
    {
        var group = app.MapGroup(AssistantGate.PathPrefix).RequireAuthorization();

        group.MapGet("/tools", (HttpContext http, ToolRegistry registry) =>
        {
            var context = ContextFrom(http);
            return Results.Ok(registry.ForRoles(context.Roles).Select(t => new
            {
                name = t.Name,
                description = t.Description,
                requiresConfirmation = t is IAssistantWriteTool,
                inputSchema = t.InputSchema,
            }));
        });

        // #503: the only way a write tool's change happens. 404 = not yours / unknown, 410 = expired, 409 = already decided.
        group.MapPost("/actions/{id:guid}/confirm", async (Guid id, HttpContext http, ToolExecutor executor, ParentControl parentControl) =>
        {
            // #523: a parent who switched the assistant off also stops an action proposed before that.
            if (await parentControl.RejectAsync(ContextFrom(http)) is { } refused)
                return refused;
            var (error, result) = await executor.ConfirmAsync(ContextFrom(http), id);
            return error switch
            {
                ActionDecisionError.None => Results.Ok(ToJson(result!)),
                _ => DecisionProblem(error),
            };
        });

        group.MapPost("/actions/{id:guid}/reject", async (Guid id, HttpContext http, ToolExecutor executor) =>
        {
            var error = await executor.RejectAsync(ContextFrom(http), id);
            return error == ActionDecisionError.None ? Results.NoContent() : DecisionProblem(error);
        });

        // Development only: call a tool directly (the chat loop in #504 is the real caller).
        if (app.Environment.IsDevelopment())
        {
            group.MapPost("/tools/{name}/invoke", async (string name, JsonElement? args, HttpContext http, ToolExecutor executor,
                AssistantDbContext db, TimeProvider clock, ParentControl parentControl) =>
            {
                var context = ContextFrom(http);
                if (await parentControl.RejectAsync(context) is { } refused)
                    return refused;
                var now = clock.GetUtcNow().UtcDateTime;
                var conversation = new Conversation
                {
                    Id = Guid.NewGuid(), UserSub = context.UserSub, Role = context.Roles.FirstOrDefault() ?? "", Title = "dev invoke",
                    CreatedAt = now, UpdatedAt = now,
                };
                db.Conversations.Add(conversation);
                await db.SaveChangesAsync(http.RequestAborted);
                var invocation = await executor.InvokeAsync(context with { ConversationId = conversation.Id }, name,
                    args ?? JsonDocument.Parse("{}").RootElement);
                return Results.Ok(new { invocation.PendingActionId, result = ToJson(invocation.Result) });
            });
        }

        return app;
    }

    private static IResult DecisionProblem(ActionDecisionError error) => error switch
    {
        ActionDecisionError.NotFound => Results.NotFound(new { code = "PendingActionNotFound" }),
        ActionDecisionError.Expired => Results.Json(new { code = "PendingActionExpired" }, statusCode: StatusCodes.Status410Gone),
        _ => Results.Conflict(new { code = "PendingActionAlreadyDecided" }),
    };
}
