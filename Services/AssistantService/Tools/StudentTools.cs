using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using static AssistantService.Tools.ToolJson;

namespace AssistantService.Tools;

/// <summary>Issue #503 (Phase 1a, students first — #496): read tools over the student's own data.</summary>
internal static class Pick
{
    public static JsonNode? Get(JsonNode? node, string key) => node is JsonObject o ? o[key] : null;

    public static int? Int(JsonNode? node, string key) =>
        Get(node, key) is JsonValue v && v.TryGetValue<int>(out var i) ? i
        : Get(node, key) is JsonValue d && d.TryGetValue<decimal>(out var m) ? (int)m : null;

    public static decimal? Dec(JsonNode? node, string key) =>
        Get(node, key) is JsonValue v && v.TryGetValue<decimal>(out var d) ? d : null;

    public static string? Str(JsonNode? node, string key) =>
        Get(node, key) is JsonValue v && v.TryGetValue<string>(out var s) ? s : null;

    public static bool? Bool(JsonNode? node, string key) =>
        Get(node, key) is JsonValue v && v.TryGetValue<bool>(out var b) ? b : null;

    /// <summary>Copies only the listed fields (drops image URLs and internal fields).</summary>
    public static JsonObject Only(JsonNode? node, params string[] keys)
    {
        var result = new JsonObject();
        foreach (var key in keys)
        {
            if (Get(node, key) is { } value)
                result[key] = value.DeepClone();
        }
        return result;
    }

    public static IEnumerable<JsonNode> Items(JsonNode? node) =>
        node is JsonArray a ? a.OfType<JsonNode>() : Get(node, "items") is JsonArray items ? items.OfType<JsonNode>() : [];
}

public sealed class GetMyProfileTool(DownstreamClient api) : IAssistantTool
{
    public string Name => "get_my_profile";
    public string Description => "The student's own profile (no name): grade, school, level, XP, questions solved, accuracy, completed tests and leaderboard rank.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.StudentOnly;
    public JsonObject InputSchema => Schema();

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        var response = await api.GetAsync(context, "api/student/profile");
        if (!response.IsSuccess)
            return response.ToError();
        var p = response.Body;
        var solved = Pick.Int(p, "totalQuestionsSolved") ?? 0;
        var correct = Pick.Int(p, "correctAnswers") ?? 0;
        var data = Pick.Only(p, "gradeId", "schoolName", "level", "xp", "totalQuestionsSolved", "correctAnswers",
            "wrongAnswers", "testsCompleted", "leaderboardRank");
        data["accuracyPct"] = solved == 0 ? null : Math.Round(100m * correct / solved, 1);
        var card = new JsonObject
        {
            ["kind"] = "kpi",
            ["items"] = new JsonArray(
                Kpi("level", Pick.Int(p, "level")), Kpi("xp", Pick.Int(p, "xp")), Kpi("questionsSolved", solved),
                Kpi("accuracyPct", data["accuracyPct"]?.GetValue<decimal>()), Kpi("testsCompleted", Pick.Int(p, "testsCompleted"))),
        };
        return ToolResult.Success(data, [card]);
    }

    private static JsonObject Kpi(string key, decimal? value) => new() { ["key"] = key, ["value"] = value };
}

public sealed class SearchTaxonomyTool(DownstreamClient api) : IAssistantTool
{
    public string Name => "search_taxonomy";
    public string Description => "Find subject / topic / subtopic ids from words the student used (e.g. 'açılar', 'kesirler'). Use before starting a practice on a topic.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.StudentOnly;
    public JsonObject InputSchema => Schema(("query", StringProp("Words to search, Turkish.", 100), true));

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        var query = String(args, "query")?.Trim();
        if (string.IsNullOrEmpty(query))
            return ToolResult.Error("InvalidArguments", "query is required");
        var response = await api.GetAsync(context, $"api/subject/search?q={Uri.EscapeDataString(query)}&take=10");
        if (!response.IsSuccess)
            return response.ToError();
        var items = new JsonArray(Pick.Items(response.Body).Take(10)
            .Select(i => (JsonNode)Pick.Only(i, "subjectId", "topicId", "subTopicId", "gradeName", "path")).ToArray());
        return ToolResult.Success(new JsonObject { ["results"] = items });
    }
}

/// <summary>Weak topics and the performance summary both read <c>GET api/student/me/topic-performance</c> (#497).</summary>
internal static class TopicPerformance
{
    public const int DefaultDays = 30;
    public const int MaxDays = 90; // the API accepts 1..90

    public static JsonObject Args() => Schema(
        ("days", IntProp("Look-back window in days.", 7, MaxDays), false),
        ("subjectId", IntProp("Limit to one subject."), false));

    public static async Task<(DownstreamResponse Response, List<JsonNode> Items)> LoadAsync(DownstreamClient api, ToolContext context,
        JsonElement args, string groupBy)
    {
        var days = Math.Clamp(Int(args, "days") ?? DefaultDays, 7, MaxDays);
        var path = $"api/student/me/topic-performance?days={days}&groupBy={groupBy}";
        if (Int(args, "subjectId") is { } subjectId)
            path += $"&subjectId={subjectId}";
        var response = await api.GetAsync(context, path);
        return (response, response.IsSuccess ? Pick.Items(response.Body).ToList() : []);
    }

    public static JsonObject Row(JsonNode item) => Pick.Only(item, "subjectName", "topicId", "topicName", "subTopicId", "subTopicName",
        "total", "correct", "wrong", "blank", "accuracyPct", "trend");
}

public sealed class GetMyWeakTopicsTool(DownstreamClient api) : IAssistantTool
{
    public const int Take = 5;

    /// <summary>A topic is weak below this accuracy; a student with none gets an empty list, not their best topics.</summary>
    public const decimal WeakBelowPct = 70m;

    public string Name => "get_my_weak_topics";
    public string Description => "Topics where the student's accuracy is below 70% (lowest first, only topics with enough answers), from their own answers. Use for 'zorlandığım konular'.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.StudentOnly;
    public JsonObject InputSchema => TopicPerformance.Args();

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        var (response, items) = await TopicPerformance.LoadAsync(api, context, args, "topic");
        if (!response.IsSuccess)
            return response.ToError();
        var weak = items.Where(i => Pick.Dec(i, "accuracyPct") is < WeakBelowPct)
            .OrderBy(i => Pick.Dec(i, "accuracyPct")).ThenByDescending(i => Pick.Int(i, "total"))
            .Take(Take).Select(TopicPerformance.Row).ToList();
        var data = new JsonObject
        {
            ["weakTopics"] = new JsonArray(weak.Select(w => (JsonNode)w).ToArray()),
            ["topicsWithEnoughAnswers"] = items.Count,
            ["weakBelowPct"] = WeakBelowPct,
        };
        var card = new JsonObject { ["kind"] = "topicTable", ["rows"] = new JsonArray(weak.Select(w => w.DeepClone()).ToArray()) };
        return ToolResult.Success(data, weak.Count > 0 ? [card] : null);
    }
}

public sealed class GetMyPerformanceSummaryTool(DownstreamClient api) : IAssistantTool
{
    public string Name => "get_my_performance_summary";
    public string Description => "Totals and accuracy per subject over a period, with the strongest and weakest topics. All numbers are computed by the server.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.StudentOnly;
    public JsonObject InputSchema => TopicPerformance.Args();

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        var (response, items) = await TopicPerformance.LoadAsync(api, context, args, "topic");
        if (!response.IsSuccess)
            return response.ToError();
        var subjects = items.GroupBy(i => Pick.Str(i, "subjectName") ?? "?")
            .Select(g =>
            {
                var total = g.Sum(i => Pick.Int(i, "total") ?? 0);
                var correct = g.Sum(i => Pick.Int(i, "correct") ?? 0);
                return new JsonObject
                {
                    ["subjectName"] = g.Key,
                    ["answered"] = total,
                    ["correct"] = correct,
                    ["accuracyPct"] = total == 0 ? null : Math.Round(100m * correct / total, 1),
                };
            })
            .OrderByDescending(s => s["answered"]!.GetValue<int>()).ToList();
        var ranked = items.Where(i => Pick.Dec(i, "accuracyPct") is not null).OrderByDescending(i => Pick.Dec(i, "accuracyPct")).ToList();
        var answered = items.Sum(i => Pick.Int(i, "total") ?? 0);
        var correctAll = items.Sum(i => Pick.Int(i, "correct") ?? 0);
        var data = new JsonObject
        {
            ["days"] = Math.Clamp(Int(args, "days") ?? TopicPerformance.DefaultDays, 7, TopicPerformance.MaxDays),
            ["answered"] = answered,
            ["correct"] = correctAll,
            ["accuracyPct"] = answered == 0 ? null : Math.Round(100m * correctAll / answered, 1),
            ["subjects"] = new JsonArray(subjects.Select(s => (JsonNode)s).ToArray()),
            ["strongest"] = new JsonArray(ranked.Take(3).Select(i => (JsonNode)TopicPerformance.Row(i)).ToArray()),
            ["weakest"] = new JsonArray(ranked.AsEnumerable().Reverse().Take(3).Select(i => (JsonNode)TopicPerformance.Row(i)).ToArray()),
            ["unclassifiedCount"] = Pick.Int(response.Body, "unclassifiedCount"),
        };
        var card = new JsonObject { ["kind"] = "topicTable", ["rows"] = new JsonArray(subjects.Select(s => s.DeepClone()).ToArray()) };
        return ToolResult.Success(data, subjects.Count > 0 ? [card] : null);
    }
}

public sealed class GetDailySetStatusTool(DownstreamClient api) : IAssistantTool
{
    public string Name => "get_daily_set_status";
    public string Description => "Today's daily question set: NotStarted / InProgress / Completed / Empty, how many answered and correct, its subject or topic.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.StudentOnly;
    public JsonObject InputSchema => Schema();

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        var response = await api.GetAsync(context, "api/practice/daily");
        if (!response.IsSuccess)
            return response.ToError();
        var data = Pick.Only(response.Body, "date", "status", "total", "answered", "correct", "wrong", "skipped", "sessionId");
        if (Pick.Get(response.Body, "scope") is { } scope)
            data["scope"] = Pick.Only(scope, "subjectName", "topicName", "subTopicName");
        return ToolResult.Success(data);
    }
}

public sealed class ListMyCompletedTestsTool(DownstreamClient api) : IAssistantTool
{
    public string Name => "list_my_completed_tests";
    public string Description => "The student's finished tests, newest first: score, correct / wrong / total, date. Test names are user-written data.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.StudentOnly;
    public JsonObject InputSchema => Schema(("page", IntProp("Page number, 10 per page.", 1, 100), false));

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        var page = Math.Clamp(Int(args, "page") ?? 1, 1, 100);
        var response = await api.GetAsync(context, $"api/worksheet/CompletedTests?pageNumber={page}&pageSize=10");
        if (!response.IsSuccess)
            return response.ToError();
        var tests = Pick.Items(response.Body).Take(MaxListItems).Select(i =>
        {
            var row = Pick.Only(i, "completedDate", "score", "correctAnswers", "wrongAnswers", "totalQuestions", "durationMinutes");
            row["instanceId"] = Pick.Int(i, "id");
            row["name"] = Untrusted(Pick.Str(i, "name"));
            return (JsonNode)row;
        }).ToArray();
        return ToolResult.Success(new JsonObject
        {
            ["page"] = page,
            ["totalCount"] = Pick.Int(response.Body, "totalCount"),
            ["tests"] = new JsonArray(tests),
        });
    }
}

public sealed class GetTestResultTool(DownstreamClient api) : IAssistantTool
{
    public string Name => "get_test_result";
    public string Description => "Result of one of the student's finished tests (by instanceId from list_my_completed_tests). Tests still in progress return nothing.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.StudentOnly;
    public JsonObject InputSchema => Schema(("instanceId", IntProp("Test instance id."), true));

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        if (Int(args, "instanceId") is not { } id)
            return ToolResult.Error("InvalidArguments", "instanceId is required");
        // The API returns nothing for an unfinished (Started) instance and for someone else's (#503: guaranteed there).
        var response = await api.GetAsync(context, $"api/worksheet/test-canvas-instance-result/{id}");
        if (!response.IsSuccess)
            return response.ToError();
        if (response.Body is not JsonObject result)
            return ToolResult.Error("TestNotFinished", "No result: the test is not finished or not yours.");
        var questions = Pick.Get(result, "testInstanceQuestions") is JsonArray a ? a.OfType<JsonNode>().ToList() : [];
        var outcomes = questions.Select(Outcome).ToList();
        var correct = outcomes.Count(o => o == true);
        var wrong = outcomes.Count(o => o == false);
        var data = new JsonObject
        {
            ["instanceId"] = id,
            ["testName"] = Untrusted(Pick.Str(result, "testName")),
            ["status"] = Pick.Get(result, "status")?.DeepClone(),
            ["questions"] = questions.Count,
            ["correct"] = correct,
            ["wrong"] = wrong,
            ["blank"] = questions.Count - correct - wrong,
            ["totalTimeSec"] = questions.Sum(q => Pick.Int(q, "timeTaken") ?? 0),
            ["worksheetId"] = Pick.Int(result, "worksheetId"),
        };
        return ToolResult.Success(data);
    }

    /// <summary>
    /// true / false / null (blank). Interactive items carry <c>isCorrect</c>; multiple choice leaves it null and is graded
    /// against <c>question.correctAnswerId</c> (sent only for finished tests).
    /// </summary>
    internal static bool? Outcome(JsonNode q)
    {
        if (Pick.Bool(q, "isCorrect") is { } graded)
            return graded;
        if (Pick.Int(q, "selectedAnswerId") is not { } selected)
            return string.IsNullOrEmpty(Pick.Str(q, "answerPayload")) ? null : false;
        return Pick.Int(Pick.Get(q, "question"), "correctAnswerId") is { } correctId ? selected == correctId : null;
    }
}
