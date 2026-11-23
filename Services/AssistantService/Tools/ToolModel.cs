using System.Text.Json;
using System.Text.Json.Nodes;

namespace AssistantService.Tools;

/// <summary>
/// Issue #503: who is calling a tool. Tools call the existing APIs with the user's own token (never a service token:
/// the service principal would get Id=0, parent endpoints would 404 and audits would name the wrong person).
/// </summary>
public sealed record ToolContext(
    string UserSub,
    IReadOnlyCollection<string> Roles,
    string BearerToken,
    string? AcceptLanguage,
    Guid? ConversationId = null,
    CancellationToken CancellationToken = default);

/// <summary>
/// A tool result. <see cref="Data"/> goes to the model; <see cref="Cards"/> and <see cref="UiActions"/> go to the panel and
/// are built from server data, never from numbers the model wrote.
/// </summary>
public sealed record ToolResult(
    JsonNode? Data,
    string? ErrorCode = null,
    string? Message = null,
    IReadOnlyList<JsonObject>? Cards = null,
    IReadOnlyList<JsonObject>? UiActions = null)
{
    public bool Ok => ErrorCode is null;

    public static ToolResult Success(JsonNode? data, IReadOnlyList<JsonObject>? cards = null, IReadOnlyList<JsonObject>? uiActions = null) =>
        new(data, Cards: cards, UiActions: uiActions);

    public static ToolResult Error(string code, string? message = null) => new(null, code, message);
}

public interface IAssistantTool
{
    /// <summary>snake_case, unique.</summary>
    string Name { get; }

    /// <summary>Shown to the model; English, short, says when to use it.</summary>
    string Description { get; }

    /// <summary>Realm roles that may see and call the tool (checked again on every call).</summary>
    IReadOnlyCollection<string> Roles { get; }

    /// <summary>JSON Schema of the arguments (type object).</summary>
    JsonObject InputSchema { get; }

    Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args);
}

/// <summary>
/// A tool that changes something. The model's call only previews (<see cref="IAssistantTool.ExecuteAsync"/> has no side
/// effects); <see cref="CommitAsync"/> runs when the user confirms the card (<c>POST /api/assistant/actions/{id}/confirm</c>).
/// </summary>
public interface IAssistantWriteTool : IAssistantTool
{
    Task<ToolResult> CommitAsync(ToolContext context, JsonElement args);
}

public static class Roles
{
    public const string Student = "Student";
    public static readonly IReadOnlyCollection<string> StudentOnly = [Student];
}

/// <summary>Helpers for tool argument parsing and result shaping.</summary>
public static class ToolJson
{
    public const int MaxListItems = 20;

    /// <summary>User-written text (test names, comments) is marked so the model treats it as data, not instructions.</summary>
    public static string? Untrusted(string? text) =>
        string.IsNullOrEmpty(text) ? text : $"<untrusted>{text.Replace("<", "‹").Replace(">", "›")}</untrusted>";

    public static int? Int(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number
            && v.TryGetInt32(out var i) ? i : null;

    public static bool? Bool(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? v.GetBoolean() : null;

    public static string? String(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    public static List<int>? IntList(JsonElement args, string name, int max = 50)
    {
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.Array)
            return null;
        return v.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Number && e.TryGetInt32(out _))
            .Select(e => e.GetInt32()).Distinct().Take(max).ToList();
    }

    public static JsonObject Schema(params (string Name, JsonObject Schema, bool Required)[] properties)
    {
        var props = new JsonObject();
        var required = new JsonArray();
        foreach (var (name, schema, isRequired) in properties)
        {
            props[name] = schema;
            if (isRequired)
                required.Add(name);
        }
        var result = new JsonObject { ["type"] = "object", ["properties"] = props, ["additionalProperties"] = false };
        if (required.Count > 0)
            result["required"] = required;
        return result;
    }

    public static JsonObject IntProp(string description, int? min = null, int? max = null)
    {
        var o = new JsonObject { ["type"] = "integer", ["description"] = description };
        if (min is not null) o["minimum"] = min;
        if (max is not null) o["maximum"] = max;
        return o;
    }

    public static JsonObject IntArrayProp(string description, int maxItems = 50) => new()
    {
        ["type"] = "array", ["description"] = description, ["items"] = new JsonObject { ["type"] = "integer" }, ["maxItems"] = maxItems,
    };

    public static JsonObject BoolProp(string description) => new() { ["type"] = "boolean", ["description"] = description };

    public static JsonObject StringProp(string description, int? maxLength = null, IEnumerable<string>? values = null)
    {
        var o = new JsonObject { ["type"] = "string", ["description"] = description };
        if (maxLength is not null) o["maxLength"] = maxLength;
        if (values is not null) o["enum"] = new JsonArray(values.Select(v => (JsonNode)JsonValue.Create(v)!).ToArray());
        return o;
    }
}
