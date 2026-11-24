using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using static AssistantService.Tools.ToolJson;

namespace AssistantService.Tools;

/// <summary>
/// Issue #1336 (Phase 1b, epic #495): read tools over the parent dashboard endpoints. The parent–child link, the child's plan
/// (403 when a section is not in it), rate limits and <c>ParentAccessAudit</c> stay in the exam API: every call carries the
/// parent's own token, so another parent's child or a Pending link answers 404 there. Only the child's first name reaches
/// the model (no surname, school or teacher names); user-written titles are wrapped as untrusted data.
/// </summary>
internal static class ParentApi
{
    public static ToolResult? StudentId(JsonElement args, out int studentId)
    {
        studentId = Int(args, "studentId") ?? 0;
        return studentId > 0 ? null : ToolResult.Error("InvalidArguments", "studentId is required (from list_my_children)");
    }

    public static (string Name, JsonObject Schema, bool Required) StudentIdArg =>
        ("studentId", IntProp("The child's studentId from list_my_children."), true);

    public static string Path(int studentId, string rest) =>
        string.Create(CultureInfo.InvariantCulture, $"api/parent/children/{studentId}/{rest}");

    public static decimal? Pct(int part, int whole) => whole == 0 ? null : Math.Round(100m * part / whole, 1);

    /// <summary>The exam API's calendar (<c>Dashboard:TimeZone</c>): the parent reads times in Istanbul time.</summary>
    public static readonly TimeZoneInfo TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Istanbul");

    /// <summary>
    /// Rewrites the UTC instants <paramref name="keys"/> of <paramref name="row"/> as local "yyyy-MM-dd HH:mm" (the model turned
    /// 12:00Z into "12:00" instead of 15:00). A value that is not an instant is left as is.
    /// </summary>
    public static JsonObject LocalTimes(JsonObject row, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (row[key] is JsonValue v && v.TryGetValue<string>(out var text)
                && DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var at))
                row[key] = TimeZoneInfo.ConvertTime(at, TimeZone).ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
        }
        return row;
    }
}

public sealed class ListMyChildrenTool(DownstreamClient api) : IAssistantTool
{
    public string Name => "list_my_children";
    public string Description => "The parent's linked children (studentId, first name, grade). Call first; the other child tools need a studentId. Pending links show no data.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.ParentOnly;
    public JsonObject InputSchema => Schema();

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        var response = await api.GetAsync(context, "api/parent-links/my-children");
        if (!response.IsSuccess)
            return response.ToError();
        var all = Pick.Items(response.Body).ToList();
        var active = all.Where(c => Pick.Str(c, "status") == "Active" && Pick.Int(c, "studentId") is > 0).ToList();
        var children = active.Select(c => (JsonNode)new JsonObject
        {
            ["studentId"] = Pick.Int(c, "studentId"),
            ["firstName"] = Untrusted(FirstName(Pick.Str(c, "studentName"))),
            ["gradeName"] = Pick.Str(c, "gradeName"),
        }).ToArray();
        return ToolResult.Success(new JsonObject
        {
            ["children"] = new JsonArray(children),
            ["pendingLinks"] = all.Count - active.Count,
        });
    }

    /// <summary>Data minimisation: the model gets the first given name only.</summary>
    internal static string? FirstName(string? fullName) =>
        fullName?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();
}

public sealed class GetChildWeeklySummaryTool(DownstreamClient api) : IAssistantTool
{
    public string Name => "get_child_weekly_summary";
    public string Description => "This week (Monday–Sunday) for one child: questions solved, correct, accuracy, active days, homework completed / overdue / pending, last activity and the weakest topic of the last 30 days. Use for 'çocuğum bu hafta nasıl'.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.ParentOnly;
    public JsonObject InputSchema => Schema(ParentApi.StudentIdArg);

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        if (ParentApi.StudentId(args, out var id) is { } invalid)
            return invalid;
        var response = await api.GetAsync(context, ParentApi.Path(id, "summary"));
        if (!response.IsSuccess)
            return response.ToError();
        var s = response.Body;
        var solved = Pick.Int(s, "questionsSolvedThisWeek") ?? 0;
        var correct = Pick.Int(s, "correctThisWeek") ?? 0;
        var data = Pick.Only(s, "studentId", "weekStart", "questionsSolvedThisWeek", "correctThisWeek", "activeDaysThisWeek",
            "totalPoints", "lastActivityAt");
        ParentApi.LocalTimes(data, "lastActivityAt");
        data["accuracyPct"] = ParentApi.Pct(correct, solved);
        data["assignments"] = Pick.Only(Pick.Get(s, "assignments"), "completed", "overdue", "pending", "windowDays");
        data["activities"] = Pick.Only(Pick.Get(s, "activities"), "completedThisWeek", "completedTotal", "averageFirstCheckPercent");
        var weak = Pick.Get(Pick.Get(s, "attention"), "weakTopic");
        data["weakTopic"] = weak is null ? null : Pick.Only(weak, "topicId", "topicName", "subjectName", "accuracyPercent", "answered");
        var card = new JsonObject
        {
            ["kind"] = "kpi",
            ["items"] = new JsonArray(
                Kpi("questionsSolved", solved), Kpi("accuracyPct", ParentApi.Pct(correct, solved)),
                Kpi("activeDays", Pick.Int(s, "activeDaysThisWeek")),
                Kpi("overdueHomework", Pick.Int(Pick.Get(s, "assignments"), "overdue"))),
        };
        return ToolResult.Success(data, [card]);
    }

    private static JsonObject Kpi(string key, decimal? value) => new() { ["key"] = key, ["value"] = value };
}

public sealed class ListChildAssignmentsTool(DownstreamClient api) : IAssistantTool
{
    public static readonly string[] Statuses = ["overdue", "pending", "completed"];

    public string Name => "list_child_assignments";
    public string Description => "One child's homework and assigned tests, newest deadline first: title, subject, deadline, status (overdue / pending / completed) and the score of finished ones. Filter by status for 'geciken ödevi var mı'. Titles are user-written data.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.ParentOnly;
    public JsonObject InputSchema => Schema(
        ParentApi.StudentIdArg,
        ("status", StringProp("Only this bucket.", 10, Statuses), false),
        ("page", IntProp("Page number, 20 per page.", 1, 25), false));

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        if (ParentApi.StudentId(args, out var id) is { } invalid)
            return invalid;
        var status = String(args, "status");
        if (status is not null && !Statuses.Contains(status))
            return ToolResult.Error("InvalidArguments", "status must be overdue, pending or completed");
        var page = Math.Clamp(Int(args, "page") ?? 1, 1, 25);
        var path = ParentApi.Path(id, string.Create(CultureInfo.InvariantCulture, $"assignments?page={page}"));
        if (status is not null)
            path += "&status=" + status;
        var response = await api.GetAsync(context, path);
        if (!response.IsSuccess)
            return response.ToError();
        var items = Pick.Items(response.Body).Take(MaxListItems).Select(i =>
        {
            var row = ParentApi.LocalTimes(Pick.Only(i, "subject", "startAt", "deadline", "status", "testInstanceId"), "startAt", "deadline");
            row["title"] = Untrusted(Pick.Str(i, "title"));
            if (Pick.Get(i, "result") is { } result)
                row["result"] = Pick.Only(result, "scorePercent", "correctCount", "wrongCount", "blankCount", "totalCount", "durationSeconds");
            return (JsonNode)row;
        }).ToArray();
        return ToolResult.Success(new JsonObject
        {
            ["page"] = page,
            ["totalCount"] = Pick.Int(response.Body, "totalCount"),
            ["counts"] = Pick.Only(Pick.Get(response.Body, "counts"), "completed", "overdue", "pending", "windowDays"),
            ["assignments"] = new JsonArray(items),
        });
    }
}

public sealed class GetChildTestResultTool(DownstreamClient api) : IAssistantTool
{
    public string Name => "get_child_test_result";
    public string Description => "Result of one finished test of a child (testInstanceId from list_child_assignments): score, correct / wrong / blank, duration and per-topic counts. No questions or answers.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.ParentOnly;
    public JsonObject InputSchema => Schema(ParentApi.StudentIdArg, ("testInstanceId", IntProp("Test instance id."), true));

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        if (ParentApi.StudentId(args, out var id) is { } invalid)
            return invalid;
        if (Int(args, "testInstanceId") is not ({ } instanceId and > 0))
            return ToolResult.Error("InvalidArguments", "testInstanceId is required");
        var response = await api.GetAsync(context, ParentApi.Path(id, string.Create(CultureInfo.InvariantCulture, $"test-results/{instanceId}")));
        if (!response.IsSuccess)
            return response.ToError();
        var r = response.Body;
        // Questions (#1310: answers and drawings) stay on the screen; the model gets counts only.
        var data = ParentApi.LocalTimes(Pick.Only(r, "testInstanceId", "subject", "outcome", "startedAt", "finishedAt"), "startedAt", "finishedAt");
        data["title"] = Untrusted(Pick.Str(r, "title"));
        data["score"] = Pick.Only(Pick.Get(r, "score"), "scorePercent", "correctCount", "wrongCount", "blankCount", "totalCount", "durationSeconds");
        data["topics"] = new JsonArray(Pick.Items(Pick.Get(r, "topics")).Take(MaxListItems)
            .Select(t => (JsonNode)Pick.Only(t, "name", "correctCount", "wrongCount", "blankCount", "totalCount")).ToArray());
        return ToolResult.Success(data);
    }
}

public sealed class GetChildWeakTopicsTool(DownstreamClient api) : IAssistantTool
{
    public string Name => "get_child_weak_topics";
    public string Description => "Topics where one child's accuracy is below 70% (lowest first, topics with at least 5 answers). Use for 'nerede zorlanıyor'. Forbidden when the child's plan has no analysis.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.ParentOnly;
    public JsonObject InputSchema => Schema(
        ParentApi.StudentIdArg,
        ("days", IntProp("Look-back window in days.", 7, TopicPerformance.MaxDays), false),
        ("subjectId", IntProp("Limit to one subject."), false));

    /// <summary>The parent endpoint refuses fewer than 5 answers per topic (#767).</summary>
    public const int MinQuestions = 5;

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        if (ParentApi.StudentId(args, out var id) is { } invalid)
            return invalid;
        var days = Math.Clamp(Int(args, "days") ?? TopicPerformance.DefaultDays, 7, TopicPerformance.MaxDays);
        var query = string.Create(CultureInfo.InvariantCulture, $"topic-performance?days={days}&groupBy=topic&minQuestions={MinQuestions}");
        if (Int(args, "subjectId") is { } subjectId)
            query += string.Create(CultureInfo.InvariantCulture, $"&subjectId={subjectId}");
        var response = await api.GetAsync(context, ParentApi.Path(id, query));
        if (!response.IsSuccess)
            return response.ToError();
        var items = Pick.Items(response.Body).ToList();
        var weak = items.Where(i => Pick.Dec(i, "accuracyPct") is < GetMyWeakTopicsTool.WeakBelowPct)
            .OrderBy(i => Pick.Dec(i, "accuracyPct")).ThenByDescending(i => Pick.Int(i, "total"))
            .Take(GetMyWeakTopicsTool.Take).Select(TopicPerformance.Row).ToList();
        var data = new JsonObject
        {
            ["days"] = days,
            ["weakTopics"] = new JsonArray(weak.Select(w => (JsonNode)w).ToArray()),
            ["topicsWithEnoughAnswers"] = items.Count,
            ["weakBelowPct"] = GetMyWeakTopicsTool.WeakBelowPct,
        };
        var card = new JsonObject { ["kind"] = "topicTable", ["rows"] = new JsonArray(weak.Select(w => w.DeepClone()).ToArray()) };
        return ToolResult.Success(data, weak.Count > 0 ? [card] : null);
    }
}

public sealed class GetChildProgressTool(DownstreamClient api) : IAssistantTool
{
    public string Name => "get_child_progress";
    public string Description => "One child's points, level, points earned this week, earned badges (name, date) and their own rank (platform and school). No other students.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.ParentOnly;
    public JsonObject InputSchema => Schema(ParentApi.StudentIdArg);

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        if (ParentApi.StudentId(args, out var id) is { } invalid)
            return invalid;
        var response = await api.GetAsync(context, ParentApi.Path(id, "progress"));
        if (!response.IsSuccess)
            return response.ToError();
        var p = response.Body;
        var data = Pick.Only(p, "totalXp", "level", "weekStart", "weeklyXp");
        var badges = Pick.Items(Pick.Get(p, "badges")).ToList();
        data["badgeCount"] = badges.Count;
        data["recentBadges"] = new JsonArray(badges.OrderByDescending(b => Pick.Str(b, "earnedOn")).Take(10)
            .Select(b => (JsonNode)Pick.Only(b, "name", "earnedOn")).ToArray());
        data["ranks"] = new JsonArray(Pick.Items(Pick.Get(p, "ranks")).Select(r => (JsonNode)Pick.Only(r, "scope", "rank", "totalCount")).ToArray());
        return ToolResult.Success(data);
    }
}

public sealed class GetChildScheduleTool(DownstreamClient api) : IAssistantTool
{
    public string Name => "get_child_schedule";
    public string Description => "One child's plan items (title, subject, planned day) and private lessons (day, start, end in local time, pending / approved) in a date range, this week when no range. Dates yyyy-MM-dd, at most 31 days.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.ParentOnly;
    public JsonObject InputSchema => Schema(
        ParentApi.StudentIdArg,
        ("from", StringProp("First day yyyy-MM-dd (with to).", 10), false),
        ("to", StringProp("Last day yyyy-MM-dd, inclusive.", 10), false));

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        if (ParentApi.StudentId(args, out var id) is { } invalid)
            return invalid;
        var from = String(args, "from");
        var to = String(args, "to");
        if ((from is null) != (to is null) || from is not null && (!IsDate(from) || !IsDate(to!)))
            return ToolResult.Error("InvalidArguments", "give both from and to as yyyy-MM-dd, or neither");
        var path = ParentApi.Path(id, "schedule");
        if (from is not null)
            path += $"?from={from}&to={to}";
        var response = await api.GetAsync(context, path);
        if (!response.IsSuccess)
            return response.ToError();
        var s = response.Body;
        var data = Pick.Only(s, "from", "to");
        data["plans"] = new JsonArray(Pick.Items(Pick.Get(s, "plans")).Take(MaxListItems).Select(p =>
        {
            var row = Pick.Only(p, "subject", "plannedOn");
            row["title"] = Untrusted(Pick.Str(p, "title"));
            return (JsonNode)row;
        }).ToArray());
        // Teacher names stay on the screen (data minimisation).
        data["lessons"] = new JsonArray(Pick.Items(Pick.Get(s, "lessons")).Take(MaxListItems)
            .Select(l => (JsonNode)ParentApi.LocalTimes(Pick.Only(l, "startsOn", "startAt", "endAt", "status"), "startAt", "endAt")).ToArray());
        return ToolResult.Success(data);
    }

    private static bool IsDate(string value) =>
        DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);
}

/// <summary>Issue #511 (Phase 2): one child's day-by-day activity with the window before ("geçen haftaya göre").</summary>
public sealed class GetChildActivityTrendTool(DownstreamClient api) : IAssistantTool
{
    public const int DefaultDays = 7;
    public const int MinDays = 7;
    public const int MaxDays = 28;

    public string Name => "get_child_activity_trend";
    public string Description => "One child's last N days (today included, default 7) day by day — questions answered, correct, points — and the same totals for the N days before. Use for 'geçen haftaya göre nasıl', 'hangi günler çalıştı', 'azaldı mı'.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.ParentOnly;
    public JsonObject InputSchema => Schema(ParentApi.StudentIdArg, ("days", IntProp("Window length in days.", MinDays, MaxDays), false));

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        if (ParentApi.StudentId(args, out var id) is { } invalid)
            return invalid;
        var days = Math.Clamp(Int(args, "days") ?? DefaultDays, MinDays, MaxDays);
        var response = await api.GetAsync(context, ParentApi.Path(id, string.Create(CultureInfo.InvariantCulture, $"activity?days={days}")));
        if (!response.IsSuccess)
            return response.ToError();
        var body = response.Body;
        var current = Totals(Pick.Get(body, "current"));
        var previous = Totals(Pick.Get(body, "previous"));
        var data = Pick.Only(body, "days", "from", "to");
        data["current"] = current;
        data["previous"] = previous;
        data["answeredChangePct"] = Change(Pick.Int(current, "answered") ?? 0, Pick.Int(previous, "answered") ?? 0);
        data["daily"] = new JsonArray(Pick.Items(Pick.Get(body, "daily"))
            .Select(d => (JsonNode)Pick.Only(d, "day", "answered", "correct", "points")).ToArray());
        var card = new JsonObject
        {
            ["kind"] = "kpi",
            ["items"] = new JsonArray(
                Kpi("questionsSolved", Pick.Int(current, "answered")), Kpi("previousQuestionsSolved", Pick.Int(previous, "answered")),
                Kpi("accuracyPct", Pick.Dec(current, "accuracyPct")), Kpi("activeDays", Pick.Int(current, "activeDays")),
                Kpi("xp", Pick.Int(current, "points"))),
        };
        return ToolResult.Success(data, [card]);
    }

    internal static JsonObject Totals(JsonNode? totals)
    {
        var row = Pick.Only(totals, "answered", "correct", "activeDays", "points");
        row["accuracyPct"] = ParentApi.Pct(Pick.Int(totals, "correct") ?? 0, Pick.Int(totals, "answered") ?? 0);
        return row;
    }

    /// <summary>Percent change of answered questions; null when the window before had none.</summary>
    internal static decimal? Change(int current, int previous) =>
        previous == 0 ? null : Math.Round(100m * (current - previous) / previous, 0);

    private static JsonObject Kpi(string key, decimal? value) => new() { ["key"] = key, ["value"] = value };
}

/// <summary>
/// Issue #511: the parent's own children side by side over the same window (activity totals only). Only when the parent asks:
/// the prompt keeps it neutral, no ranking of siblings.
/// </summary>
public sealed class CompareChildrenTool(DownstreamClient api) : IAssistantTool
{
    public const int MaxChildren = 6;

    public string Name => "compare_children";
    public string Description => "The parent's linked children side by side for the last N days (default 7): questions answered, accuracy, active days and points, with the N days before. Only when the parent asks to compare their children.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.ParentOnly;
    public JsonObject InputSchema => Schema(("days", IntProp("Window length in days.", GetChildActivityTrendTool.MinDays, GetChildActivityTrendTool.MaxDays), false));

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        var days = Math.Clamp(Int(args, "days") ?? GetChildActivityTrendTool.DefaultDays, GetChildActivityTrendTool.MinDays, GetChildActivityTrendTool.MaxDays);
        var links = await api.GetAsync(context, "api/parent-links/my-children");
        if (!links.IsSuccess)
            return links.ToError();
        var children = Pick.Items(links.Body)
            .Where(c => Pick.Str(c, "status") == "Active" && Pick.Int(c, "studentId") is > 0)
            .Take(MaxChildren)
            .ToList();

        var rows = new List<JsonNode>();
        foreach (var child in children)
        {
            var studentId = Pick.Int(child, "studentId")!.Value;
            var row = new JsonObject
            {
                ["studentId"] = studentId,
                ["firstName"] = Untrusted(ListMyChildrenTool.FirstName(Pick.Str(child, "studentName"))),
            };
            var response = await api.GetAsync(context, ParentApi.Path(studentId, string.Create(CultureInfo.InvariantCulture, $"activity?days={days}")));
            if (response.IsSuccess)
            {
                row["current"] = GetChildActivityTrendTool.Totals(Pick.Get(response.Body, "current"));
                row["previous"] = GetChildActivityTrendTool.Totals(Pick.Get(response.Body, "previous"));
            }
            else
            {
                // One child's failure (e.g. a link removed meanwhile) does not hide the others.
                row["error"] = response.ToError().ErrorCode;
            }
            rows.Add(row);
        }
        return ToolResult.Success(new JsonObject { ["days"] = days, ["children"] = new JsonArray(rows.ToArray()) });
    }
}

/// <summary>Issue #511: the weekly report of a finished week (#1112), Monday–Sunday. Forbidden when not in the child's plan.</summary>
public sealed class GetChildWeeklyReportTool(DownstreamClient api) : IAssistantTool
{
    public const int MaxWeeksBack = 12;

    public string Name => "get_child_weekly_report";
    public string Description => "The weekly report of one child's last finished week (or weeksAgo weeks before it): answers, accuracy, study minutes, points, accuracy per subject, topics that dropped, homework, mock exams, badges and a suggestion. Use for 'geçen hafta raporu', 'haftalık rapor'. Forbidden when the report is not in the child's plan.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.ParentOnly;
    public JsonObject InputSchema => Schema(
        ParentApi.StudentIdArg,
        ("weeksAgo", IntProp("0 = the last finished week, 1 = the week before…", 0, MaxWeeksBack - 1), false));

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        if (ParentApi.StudentId(args, out var id) is { } invalid)
            return invalid;
        var path = ParentApi.Path(id, "weekly-report");
        var response = await api.GetAsync(context, path);
        // An older week counts back from the API's last finished week (its calendar, not this service's clock).
        if (response.IsSuccess && Int(args, "weeksAgo") is { } weeksAgo and > 0
            && DateOnly.TryParseExact(Pick.Str(response.Body, "weekStart"), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var last))
        {
            var week = last.AddDays(-7 * Math.Min(weeksAgo, MaxWeeksBack - 1));
            response = await api.GetAsync(context, path + "?weekStart=" + week.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }
        if (!response.IsSuccess)
            return response.ToError();
        var r = response.Body;
        var solved = Pick.Int(r, "questionsSolved") ?? 0;
        var correct = Pick.Int(r, "correct") ?? 0;
        var data = Pick.Only(r, "weekStart", "questionsSolved", "correct", "activeDays", "studyMinutes", "points",
            "assignmentsCompleted", "assignmentsOverdue", "suggestion", "suggestionTopic");
        data["accuracyPct"] = ParentApi.Pct(correct, solved);
        data["subjects"] = new JsonArray(Pick.Items(Pick.Get(r, "subjects")).Take(MaxListItems)
            .Select(s => (JsonNode)Pick.Only(s, "subjectName", "answered", "correct", "accuracyPercent")).ToArray());
        data["droppedTopics"] = new JsonArray(Pick.Items(Pick.Get(r, "droppedTopics"))
            .Select(t => (JsonNode)Pick.Only(t, "topicName", "subjectName", "accuracyPercent", "previousAccuracyPercent")).ToArray());
        data["mockExams"] = new JsonArray(Pick.Items(Pick.Get(r, "mockExams")).Take(MaxListItems).Select(m =>
        {
            var row = Pick.Only(m, "net", "rank", "takers", "finishedAtUtc");
            row["name"] = Untrusted(Pick.Str(m, "name"));
            return (JsonNode)row;
        }).ToArray());
        data["badges"] = new JsonArray(Pick.Items(Pick.Get(r, "badges")).Take(MaxListItems)
            .Select(b => (JsonNode)Pick.Only(b, "name")).ToArray());
        var card = new JsonObject
        {
            ["kind"] = "kpi",
            ["items"] = new JsonArray(
                Kpi("questionsSolved", solved), Kpi("accuracyPct", ParentApi.Pct(correct, solved)),
                Kpi("activeDays", Pick.Int(r, "activeDays")), Kpi("studyMinutes", Pick.Int(r, "studyMinutes")),
                Kpi("overdueHomework", Pick.Int(r, "assignmentsOverdue"))),
        };
        return ToolResult.Success(data, [card]);
    }

    private static JsonObject Kpi(string key, decimal? value) => new() { ["key"] = key, ["value"] = value };
}
