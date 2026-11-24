using System.Text.Json;
using System.Text.Json.Nodes;
using static AssistantService.Tools.ToolJson;

namespace AssistantService.Tools;

/// <summary>
/// Issue #503: opens today's daily set, optionally scoped to a subject / topic / subtopic ("açılar setini aç"). The model's
/// call previews; the set opens only when the student confirms the card.
/// </summary>
/// <summary>Issue #524: the exam API records sessions the assistant opened (the epic's success metric).</summary>
internal static class StartedViaAssistant
{
    public const string Value = "Assistant";
}

public sealed class StartDailyPracticeTool(DownstreamClient api) : IAssistantWriteTool
{
    private const string StartedVia = StartedViaAssistant.Value;

    public string Name => "start_daily_practice";
    public string Description => "Open today's daily question set, optionally on one subject / topic / subtopic (ids from search_taxonomy). Needs the student's confirmation; then the practice screen opens.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.StudentOnly;
    public JsonObject InputSchema => Schema(
        ("subjectId", IntProp("Subject id."), false),
        ("topicId", IntProp("Topic id."), false),
        ("subTopicId", IntProp("Subtopic id."), false));

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        var status = await api.GetAsync(context, "api/practice/daily");
        if (!status.IsSuccess)
            return status.ToError();
        var current = Pick.Str(status.Body, "status");
        var preview = new JsonObject
        {
            ["action"] = "startDailyPractice",
            ["currentStatus"] = current,
            // An already started set keeps its scope (the API ignores a new one).
            ["scopeApplies"] = current is "NotStarted" or "Empty" or null,
            ["subjectId"] = Int(args, "subjectId"),
            ["topicId"] = Int(args, "topicId"),
            ["subTopicId"] = Int(args, "subTopicId"),
        };
        return ToolResult.Success(preview);
    }

    public async Task<ToolResult> CommitAsync(ToolContext context, JsonElement args)
    {
        var response = await api.PostAsync(context, "api/practice/daily/start", new
        {
            startedVia = StartedVia,
            subjectId = Int(args, "subjectId"),
            topicId = Int(args, "topicId"),
            subTopicId = Int(args, "subTopicId"),
        });
        if (!response.IsSuccess)
            return response.ToError();
        var sessionId = Pick.Int(response.Body, "sessionId");
        var data = Pick.Only(response.Body, "sessionId", "status", "total");
        if (Pick.Get(response.Body, "scope") is { } scope)
            data["scope"] = Pick.Only(scope, "subjectName", "topicName", "subTopicName", "filledFromGeneral");
        return ToolResult.Success(data, uiActions: sessionId is { } id ? [UiScreens.Navigate("practiceSession", id)] : null);
    }
}

/// <summary>Issue #503: a free practice session on chosen topics, mistakes or due reviews.</summary>
public sealed class StartPracticeSessionTool(DownstreamClient api) : IAssistantWriteTool
{
    public string Name => "start_practice_session";
    public string Description => "Start a practice session on given subjects / topics / subtopics (ids from search_taxonomy), or on the student's mistakes or due reviews. Needs the student's confirmation.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.StudentOnly;
    public JsonObject InputSchema => Schema(
        ("subjectIds", IntArrayProp("Subject ids.", 10), false),
        ("topicIds", IntArrayProp("Topic ids.", 10), false),
        ("subTopicIds", IntArrayProp("Subtopic ids.", 10), false),
        ("targetCount", IntProp("Number of questions.", 1, 50), false),
        ("mistakes", BoolProp("Practice questions the student got wrong."), false),
        ("dueReview", BoolProp("Practice questions due for review today."), false));

    public Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        var body = Body(args);
        if (body.Count == 0)
            return Task.FromResult(ToolResult.Error("InvalidArguments", "Give topics / subjects, or mistakes / dueReview."));
        body["action"] = "startPracticeSession";
        return Task.FromResult(ToolResult.Success(body));
    }

    public async Task<ToolResult> CommitAsync(ToolContext context, JsonElement args)
    {
        var body = Body(args);
        body["startedVia"] = StartedViaAssistant.Value;
        var response = await api.PostAsync(context, "api/practice/sessions", body);
        if (!response.IsSuccess)
            return response.ToError();
        var id = Pick.Int(response.Body, "id");
        var data = new JsonObject { ["sessionId"] = id };
        return ToolResult.Success(data, uiActions: id is { } sessionId ? [UiScreens.Navigate("practiceSession", sessionId)] : null);
    }

    private static JsonObject Body(JsonElement args)
    {
        var body = new JsonObject();
        void Ids(string name)
        {
            if (IntList(args, name, 10) is { Count: > 0 } ids)
                body[name] = new JsonArray(ids.Select(i => (JsonNode)i).ToArray());
        }
        Ids("subjectIds");
        Ids("topicIds");
        Ids("subTopicIds");
        if (Bool(args, "mistakes") == true) body["mistakes"] = true;
        if (Bool(args, "dueReview") == true) body["dueReview"] = true;
        if (body.Count > 0 && Int(args, "targetCount") is { } count)
            body["targetCount"] = Math.Clamp(count, 1, 50);
        return body;
    }
}
