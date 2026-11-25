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

/// <summary>
/// Issue #527: questions where the student lost time (<c>slow</c>: at least twice the other students' average, right or wrong)
/// or answered wrong in a rush (<c>rushed</c>). The model sees ids, topics and the two times; the panel shows the images with a
/// "solve these" button (start_hard_question_practice accepts these ids). No correct choice.
/// </summary>
public sealed class GetTimeStruggleQuestionsTool(DownstreamClient api) : IAssistantTool
{
    public const int DefaultTake = 5;
    public const int MaxTake = 10;
    private const int MaxCardText = 300;
    private static readonly string[] Kinds = ["slow", "rushed"];

    public string Name => "get_time_struggle_questions";
    public string Description =>
        "kind=slow (default): questions the student spent much longer on than other students (right or wrong; slowest relative " +
        "time first). kind=rushed: questions answered wrong much faster than average (likely carelessness or guessing). Gives the " +
        "student's time and the average in seconds. Optionally one subject / topic / subtopic (ids from search_taxonomy). Shows " +
        "the question images; then start_hard_question_practice with the ids to solve them.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.StudentOnly;
    public JsonObject InputSchema => Schema(
        ("kind", StringProp("slow: lost time; rushed: wrong in a rush.", values: Kinds), false),
        ("subjectId", IntProp("Subject id."), false),
        ("topicId", IntProp("Topic id."), false),
        ("subTopicId", IntProp("Subtopic id."), false),
        ("take", IntProp("How many questions.", 1, MaxTake), false));

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        var take = Math.Clamp(Int(args, "take") ?? DefaultTake, 1, MaxTake);
        var kind = String(args, "kind") == "rushed" ? "rushed" : "slow";
        var path = $"api/student/me/time-struggles?kind={kind}&days=90&take={take}";
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
            ["kind"] = kind,
            ["totalCount"] = Pick.Int(response.Body, "totalCount"),
            ["questions"] = new JsonArray(items.Select(i =>
            {
                var row = Pick.Only(i, "questionId", "subjectName", "topicName", "subTopicNames", "timeTakenSeconds", "averageSeconds", "correct", "answeredAt");
                // Ready to say: the model got "3,5 dakika" for 200 s live.
                row["timeTaken"] = Duration(Pick.Int(i, "timeTakenSeconds"));
                row["average"] = Duration(Pick.Int(i, "averageSeconds"));
                return (JsonNode)row;
            }).ToArray()),
        };
        if (items.Count == 0)
            return ToolResult.Success(data);

        var card = new JsonObject
        {
            ["kind"] = "questions",
            ["items"] = new JsonArray(items.Select(i =>
            {
                var row = Pick.Only(i, "questionId", "subjectName", "topicName", "imageUrl");
                if (Pick.Str(i, "text") is { Length: > 0 } text)
                    row["text"] = text.Length <= MaxCardText ? text : text[..MaxCardText] + "…";
                return (JsonNode)row;
            }).ToArray()),
        };
        return ToolResult.Success(data, [card]);
    }

    /// <summary>Turkish "3 dk 20 sn" / "45 sn".</summary>
    internal static string? Duration(int? seconds) => seconds switch
    {
        null => null,
        < 60 => $"{seconds} sn",
        _ when seconds % 60 == 0 => $"{seconds / 60} dk",
        _ => $"{seconds / 60} dk {seconds % 60} sn",
    };
}

/// <summary>Issue #526: a practice session on exactly these hard questions, in this order. Needs the student's confirmation.</summary>
public sealed class StartHardQuestionPracticeTool(DownstreamClient api) : IAssistantWriteTool
{
    public const int MaxQuestions = 30;

    public string Name => "start_hard_question_practice";
    public string Description =>
        "Solve again the questions get_hard_question_examples or get_time_struggle_questions returned (pass their questionIds). " +
        "Needs the student's confirmation; then the practice screen opens. Refused for ids not in those lists.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.StudentOnly;
    public JsonObject InputSchema => Schema(("questionIds", IntArrayProp("Question ids from get_hard_question_examples or get_time_struggle_questions.", MaxQuestions), true));

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

/// <summary>
/// Issue #528: subtopics where the student keeps picking the same popular distractor (the wrong choice most students pick).
/// The model sees the subtopics, counts and the student's own choices (label and text, wrapped as untrusted); never the correct
/// choice: it explains through explain_question (#510). The panel shows the example questions as a questions card.
/// </summary>
public sealed class GetRepeatedTrapAnswersTool(DownstreamClient api) : IAssistantTool
{
    public const int DefaultTake = 3;
    public const int MaxTake = 5;
    private const int MaxCardText = 300;

    public string Name => "get_repeated_trap_answers";
    public string Description =>
        "Subtopics where the student keeps falling for the same trap: in most of their wrong answers there they picked the choice " +
        "most students wrongly pick. Gives counts and example questions with the student's chosen choice (no correct answer). " +
        "Optionally one subject / topic (ids from search_taxonomy). Shows the example questions to the student.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.StudentOnly;
    public JsonObject InputSchema => Schema(
        ("subjectId", IntProp("Subject id."), false),
        ("topicId", IntProp("Topic id."), false),
        ("take", IntProp("How many subtopics.", 1, MaxTake), false));

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        var take = Math.Clamp(Int(args, "take") ?? DefaultTake, 1, MaxTake);
        var path = $"api/student/me/repeated-traps?days=90&take={take}";
        foreach (var name in new[] { "subjectId", "topicId" })
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
            ["minMistakes"] = Pick.Int(response.Body, "minMistakes"),
            ["totalCount"] = Pick.Int(response.Body, "totalCount"),
            ["patterns"] = new JsonArray(items.Select(i =>
            {
                var row = Pick.Only(i, "subjectName", "topicId", "topicName", "subTopicId", "subTopicName", "mistakeCount", "trapCount");
                // No percent: the model read it as accuracy; trapCount of mistakeCount says it plainly.
                row["examples"] = new JsonArray(Pick.Items(Pick.Get(i, "examples")).Select(e => (JsonNode)new JsonObject
                {
                    ["questionId"] = Pick.Int(e, "questionId"),
                    ["chosenLabel"] = Pick.Str(e, "chosenLabel"),
                    ["chosenText"] = Untrusted(Pick.Str(e, "chosenText")),
                }).ToArray());
                return (JsonNode)row;
            }).ToArray()),
        };
        var examples = items.SelectMany(i => Pick.Items(Pick.Get(i, "examples")).Select(e => (Pattern: i, Example: e))).ToList();
        if (examples.Count == 0)
            return ToolResult.Success(data);

        var card = new JsonObject
        {
            ["kind"] = "questions",
            // The next step is a practice on the subtopic (offered in the reply), not these questions again.
            ["solvable"] = false,
            ["items"] = new JsonArray(examples.Select(x =>
            {
                var row = new JsonObject
                {
                    ["questionId"] = Pick.Int(x.Example, "questionId"),
                    ["subjectName"] = Pick.Str(x.Pattern, "subjectName"),
                    ["topicName"] = Pick.Str(x.Pattern, "subTopicName") ?? Pick.Str(x.Pattern, "topicName"),
                    ["imageUrl"] = Pick.Str(x.Example, "imageUrl"),
                };
                if (Pick.Str(x.Example, "text") is { Length: > 0 } text)
                    row["text"] = text.Length <= MaxCardText ? text : text[..MaxCardText] + "…";
                return (JsonNode)row;
            }).ToArray()),
        };
        return ToolResult.Success(data, [card]);
    }
}

/// <summary>
/// Issue #533: the student's accuracy per topic next to their grade's average and median (all students of the same grade, any
/// school; nightly stats, null below 20 students). Aggregates only: no rank, no other student. No card: the reply compares.
/// </summary>
public sealed class CompareMyTopicsWithPeersTool(DownstreamClient api) : IAssistantTool
{
    public const int MaxTopics = 15;

    public string Name => "compare_my_topics_with_peers";
    public string Description =>
        "The student's accuracy per topic (last 90 days, topics with at least 5 answers) next to the average and median accuracy " +
        "of students in the same grade. Topics furthest behind first; peer values are null when too few students worked on the " +
        "topic. Optionally one subject (id from search_taxonomy).";
    public IReadOnlyCollection<string> Roles => Tools.Roles.StudentOnly;
    public JsonObject InputSchema => Schema(("subjectId", IntProp("Subject id."), false));

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        var path = "api/student/me/topic-comparison";
        if (Int(args, "subjectId") is { } subjectId)
            path += $"?subjectId={subjectId}";
        var response = await api.GetAsync(context, path);
        if (!response.IsSuccess)
            return response.ToError();

        var items = Pick.Items(response.Body).ToList();
        return ToolResult.Success(new JsonObject
        {
            ["days"] = Pick.Int(response.Body, "days"),
            ["minAnswers"] = Pick.Int(response.Body, "minAnswers"),
            ["topicCount"] = items.Count,
            ["withoutPeerData"] = items.Count(i => Pick.Int(i, "peerAccuracyPct") is null),
            ["topics"] = new JsonArray(items.Take(MaxTopics).Select(i => (JsonNode)Pick.Only(i, "subjectName", "topicId", "topicName",
                "myAnswered", "myAccuracyPct", "peerAccuracyPct", "peerMedianPct")).ToArray()),
        });
    }
}
