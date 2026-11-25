using System.Text.Json;
using System.Text.Json.Nodes;
using AssistantService.Help;
using static AssistantService.Tools.ToolJson;

namespace AssistantService.Tools;

/// <summary>
/// Issue #506: searches the in-app user guide (<see cref="HelpIndex"/>). Only articles of the caller's role come back;
/// the model answers how-to questions from this text instead of guessing screens.
/// </summary>
public sealed class SearchHelpTool(HelpIndex index) : IAssistantTool
{
    public const int MaxQueryLength = 200;

    public string Name => "search_help";
    public string Description =>
        "Search the app's user guide for how to do something in the app (e.g. adding a parent, changing the password, " +
        "where homework is). Answer from the returned text; if nothing comes back, say you don't know.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.Everyone;
    public JsonObject InputSchema => Schema(("query", StringProp("The user's question or its key words, Turkish.", MaxQueryLength), true));

    public Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        var query = String(args, "query")?.Trim();
        if (string.IsNullOrEmpty(query) || query.Length > MaxQueryLength)
            return Task.FromResult(ToolResult.Error("InvalidArguments", $"query is required, at most {MaxQueryLength} characters"));
        var results = new JsonArray(index.Search(query, context.Roles)
            .Select(h => (JsonNode)new JsonObject { ["slug"] = h.Article.Slug, ["title"] = h.Article.Title, ["text"] = h.Article.Body })
            .ToArray());
        return Task.FromResult(ToolResult.Success(new JsonObject { ["results"] = results }));
    }
}
