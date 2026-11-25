using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using static AssistantService.Tools.ToolJson;

namespace AssistantService.Tools;

/// <summary>
/// Issue #512 (Phase 2, slice 1): teacher read tools over the teacher dashboard endpoints (#53–#56). Scope stays in the exam API:
/// every call carries the teacher's own token, the endpoints read the caller's own worksheets inside the school scope (#190), and an
/// unapproved teacher gets 403 <c>TeacherNotApproved</c> (#287), which reaches the model as the error code. Student and test names
/// are user-written and go to the model as untrusted data.
/// </summary>
internal static class TeacherApi
{
    public const int DefaultDays = 7;
    public const int MaxDays = 90;

    public static decimal? Pct(int part, int whole) => whole == 0 ? null : Math.Round(100m * part / whole, 1);

    public static decimal? Round(decimal? value) => value is { } v ? Math.Round(v, 1) : null;

    /// <summary>Case and dotted/dotless-i insensitive "contains" for a name filter.</summary>
    public static bool NameMatches(string? name, string? query) =>
        string.IsNullOrWhiteSpace(query)
        || (name is not null && Fold(name).Contains(Fold(query.Trim()), StringComparison.Ordinal));

    private static string Fold(string s) =>
        s.ToLower(CultureInfo.GetCultureInfo("tr-TR")).Replace('ı', 'i').Replace('İ', 'i');
}

public sealed class GetTeacherOverviewTool(DownstreamClient api) : IAssistantTool
{
    public string Name => "get_teacher_overview";
    public string Description => "The teacher's overview for the last `days` days (default 7, 1–90): own tests and distinct students assigned to them, then the questions those students solved in the teacher's assigned tests (not elsewhere on the platform), accuracy, minutes and the most active students. Use for 'öğrencilerim bu hafta nasıl', 'genel durum'.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.TeacherOnly;
    public JsonObject InputSchema => Schema(("days", IntProp("Window in days (1–90). Default 7.", 1, TeacherApi.MaxDays), false));

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        var days = Int(args, "days") ?? TeacherApi.DefaultDays;
        if (days is < 1 or > TeacherApi.MaxDays)
            return ToolResult.Error("InvalidArguments", "days must be 1–90");

        var summary = await api.GetAsync(context, "api/teacher/dashboard-summary");
        if (!summary.IsSuccess)
            return summary.ToError();
        var activity = await api.GetAsync(context, string.Create(CultureInfo.InvariantCulture, $"api/teacher/students-activity-summary?days={days}"));
        if (!activity.IsSuccess)
            return activity.ToError();

        var a = activity.Body;
        var solved = Pick.Int(a, "totalQuestionsSolved") ?? 0;
        var correct = Pick.Int(a, "totalCorrectCount") ?? 0;
        var minutes = (Pick.Int(a, "totalTimeSeconds") ?? 0) / 60;
        var top = Pick.Items(Pick.Get(a, "topStudents")).Take(5).Select(s =>
        {
            var n = Pick.Int(s, "questionsSolved") ?? 0;
            return (JsonNode)new JsonObject
            {
                ["studentId"] = Pick.Int(s, "studentId"),
                ["name"] = Untrusted(Pick.Str(s, "studentName")),
                ["questionsSolved"] = n,
                ["accuracyPct"] = TeacherApi.Pct(Pick.Int(s, "correctCount") ?? 0, n),
            };
        }).ToArray();

        var worksheets = Pick.Int(summary.Body, "totalWorksheets");
        var students = Pick.Int(summary.Body, "totalUniqueStudents");
        var data = new JsonObject
        {
            ["days"] = days,
            ["worksheets"] = worksheets,
            ["students"] = students,
            ["questionsSolved"] = solved,
            ["accuracyPct"] = TeacherApi.Pct(correct, solved),
            ["studyMinutes"] = minutes,
            ["mostActiveStudents"] = new JsonArray(top),
        };
        var card = new JsonObject
        {
            ["kind"] = "kpi",
            ["items"] = new JsonArray(Kpi("worksheets", worksheets), Kpi("students", students), Kpi("questionsSolved", solved),
                Kpi("accuracyPct", TeacherApi.Pct(correct, solved))),
        };
        return ToolResult.Success(data, [card]);
    }

    private static JsonObject Kpi(string key, decimal? value) => new() { ["key"] = key, ["value"] = value };
}

public sealed class ListMyWorksheetsTool(DownstreamClient api) : IAssistantTool
{
    public const int MaxItems = 20;

    public string Name => "list_my_worksheets";
    public string Description => "The teacher's own tests with how many students they are assigned to and the completion percentage, lowest completion first. Optional `query` filters by test name. Use for 'testlerim', 'hangi testi kaç kişi bitirdi'. Names are user-written data.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.TeacherOnly;
    public JsonObject InputSchema => Schema(("query", StringProp("Words from the test name (optional).", 80), false));

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        var query = String(args, "query");
        var response = await api.GetAsync(context, "api/teacher/worksheets-overview");
        if (!response.IsSuccess)
            return response.ToError();

        var all = Pick.Items(response.Body).Where(w => TeacherApi.NameMatches(Pick.Str(w, "name"), query)).ToList();
        var assigned = all.Count(w => Pick.Int(w, "assignedStudentCount") is > 0);
        var items = all
            .OrderBy(w => Pick.Int(w, "assignedStudentCount") is > 0 ? 0 : 1)
            .ThenBy(w => Pick.Dec(w, "completionPercentage") ?? 0)
            .Take(MaxItems)
            .Select(w => (JsonNode)new JsonObject
            {
                ["worksheetId"] = Pick.Int(w, "worksheetId"),
                ["name"] = Untrusted(Pick.Str(w, "name")),
                ["assignedStudents"] = Pick.Int(w, "assignedStudentCount"),
                ["completionPct"] = TeacherApi.Round(Pick.Dec(w, "completionPercentage")),
            }).ToArray();
        return ToolResult.Success(new JsonObject
        {
            ["total"] = all.Count,
            ["assignedTotal"] = assigned,
            ["items"] = new JsonArray(items),
        });
    }
}

public sealed class ListLaggingStudentsTool(DownstreamClient api) : IAssistantTool
{
    public const int MaxItems = 20;

    public string Name => "list_lagging_students";
    public string Description => "Students who have not finished one of the teacher's assigned tests in its assignment window, one row per student and test; overdue = the due date already passed. Overdue rows come first. Optional gradeId (the student's grade) and worksheetId (one test from list_my_worksheets). Use for 'kim geride', 'testi bitirmeyenler'. Names are user-written data.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.TeacherOnly;
    public JsonObject InputSchema => Schema(
        ("gradeId", IntProp("Grade id to filter by (optional).", 1), false),
        ("worksheetId", IntProp("One test (worksheetId from list_my_worksheets), optional.", 1), false));

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        var gradeId = Int(args, "gradeId");
        var worksheetId = Int(args, "worksheetId");
        var path = gradeId is { } g
            ? string.Create(CultureInfo.InvariantCulture, $"api/teacher/lagging-students?gradeId={g}")
            : "api/teacher/lagging-students";
        var response = await api.GetAsync(context, path);
        if (!response.IsSuccess)
            return response.ToError();

        var all = Pick.Items(response.Body)
            .Where(r => worksheetId is null || Pick.Int(r, "worksheetId") == worksheetId)
            .ToList();
        var items = all
            .OrderByDescending(r => Pick.Bool(r, "isExpired") == true)
            .Take(MaxItems)
            .Select(r => (JsonNode)new JsonObject
            {
                ["studentId"] = Pick.Int(r, "studentId"),
                ["name"] = Untrusted(Pick.Str(r, "studentName")),
                ["gradeName"] = Pick.Str(r, "gradeName"),
                ["worksheetId"] = Pick.Int(r, "worksheetId"),
                ["worksheetName"] = Untrusted(Pick.Str(r, "worksheetName")),
                ["overdue"] = Pick.Bool(r, "isExpired") == true,
            }).ToArray();
        return ToolResult.Success(new JsonObject
        {
            ["total"] = all.Count,
            ["overdueTotal"] = all.Count(r => Pick.Bool(r, "isExpired") == true),
            ["distinctStudents"] = all.Select(r => Pick.Int(r, "studentId")).Distinct().Count(),
            ["items"] = new JsonArray(items),
        });
    }
}
