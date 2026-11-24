using System.Text.Json;
using System.Text.Json.Nodes;
using static AssistantService.Tools.ToolJson;

namespace AssistantService.Tools;

/// <summary>
/// Issue #526 (Phase 2): the questions the student struggled with most (<c>mine</c>: wrong or blank answers in finished tests
/// and practice) or that most students got wrong (<c>global</c>: nightly stats, an aggregate percent only). A question of a running
/// or unfinished assigned test never shows. The model sees ids, topics and counts; the panel shows the question images as a card
/// with a "solve these" button. No correct choice: explain_question (#510) explains one.
/// </summary>
public sealed class GetHardQuestionExamplesTool(DownstreamClient api) : IAssistantTool
{
    public const int DefaultTake = 5;
    public const int MaxTake = 10;
    private const int MaxCardText = 300;
    private static readonly string[] Modes = ["mine", "global"];

    public string Name => "get_hard_question_examples";
    public string Description =>
        "mode=mine (default): questions the student struggled with most (most wrong / blank first, then ones missed again after a " +
        "correct answer). mode=global: questions most students got wrong (lowest correctPercent first; ones the student already " +
        "solved are left out). Optionally on one subject / topic / subtopic (ids from search_taxonomy). Shows the question images " +
        "to the student. Then start_hard_question_practice with the ids to solve them.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.StudentOnly;
    public JsonObject InputSchema => Schema(
        ("mode", StringProp("mine: the student's own; global: hard for most students.", values: Modes), false),
        ("subjectId", IntProp("Subject id."), false),
        ("topicId", IntProp("Topic id."), false),
        ("subTopicId", IntProp("Subtopic id."), false),
        ("days", IntProp("mine only: look-back window in days.", 1, 90), false),
        ("take", IntProp("How many questions.", 1, MaxTake), false));

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        var take = Math.Clamp(Int(args, "take") ?? DefaultTake, 1, MaxTake);
        var global = String(args, "mode") == "global";
        var path = global
            ? $"api/student/me/hard-questions?mode=global&take={take}"
            : $"api/student/me/hard-questions?mode=mine&days={Math.Clamp(Int(args, "days") ?? 90, 1, 90)}&take={take}";
        foreach (var name in new[] { "subjectId", "topicId", "subTopicId" })
        {
            if (Int(args, name) is { } id)
                path += $"&{name}={id}";
        }
        var response = await api.GetAsync(context, path);
        if (!response.IsSuccess)
            return response.ToError();

        var items = Pick.Items(response.Body).Take(take).ToList();
        var data = new JsonObject
        {
            ["mode"] = global ? "global" : "mine",
            ["totalCount"] = Pick.Int(response.Body, "totalCount"),
            ["questions"] = new JsonArray(items.Select(i => (JsonNode)Pick.Only(i, "questionId", "subjectName", "topicName",
                "subTopicNames", "attempts", "missCount", "missedAfterCorrect", "lastAttemptCorrect", "lastAttemptAt", "correctPercent")).ToArray()),
        };
        if (items.Count == 0)
            return ToolResult.Success(data);

        var card = new JsonObject
        {
            ["kind"] = "questions",
            ["items"] = new JsonArray(items.Select(i =>
            {
                var row = Pick.Only(i, "questionId", "subjectName", "topicName", "missCount", "correctPercent", "imageUrl");
                if (Pick.Str(i, "text") is { Length: > 0 } text)
                    row["text"] = text.Length <= MaxCardText ? text : text[..MaxCardText] + "…";
                return (JsonNode)row;
            }).ToArray()),
        };
        return ToolResult.Success(data, [card]);
    }
}

/// <summary>Issue #526: a practice session on exactly these hard questions, in this order. Needs the student's confirmation.</summary>
public sealed class StartHardQuestionPracticeTool(DownstreamClient api) : IAssistantWriteTool
{
    public const int MaxQuestions = 30;

    public string Name => "start_hard_question_practice";
    public string Description =>
        "Solve again the questions get_hard_question_examples returned (pass their questionIds). Needs the student's confirmation; " +
        "then the practice screen opens. Refused for ids not in the student's hard questions.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.StudentOnly;
    public JsonObject InputSchema => Schema(("questionIds", IntArrayProp("Question ids from get_hard_question_examples.", MaxQuestions), true));

    public Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        if (Ids(args) is not { Count: > 0 } ids)
            return Task.FromResult(ToolResult.Error("InvalidArguments", "questionIds is required."));
        return Task.FromResult(ToolResult.Success(new JsonObject
        {
            ["action"] = "startHardQuestionPractice",
            ["targetCount"] = ids.Count,
        }));
    }

    public async Task<ToolResult> CommitAsync(ToolContext context, JsonElement args)
    {
        if (Ids(args) is not { Count: > 0 } ids)
            return ToolResult.Error("InvalidArguments", "questionIds is required.");
        var response = await api.PostAsync(context, "api/practice/sessions", new
        {
            questionIds = ids,
            startedVia = StartedViaAssistant.Value,
        });
        if (!response.IsSuccess)
            return response.ToError();
        var id = Pick.Int(response.Body, "id");
        return ToolResult.Success(new JsonObject { ["sessionId"] = id, ["questionCount"] = ids.Count },
            uiActions: id is { } sessionId ? [UiScreens.Navigate("practiceSession", sessionId)] : null);
    }

    private static List<int>? Ids(JsonElement args) => IntList(args, "questionIds", MaxQuestions)?.Distinct().ToList();
}
