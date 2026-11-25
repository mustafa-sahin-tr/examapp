using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using static AssistantService.Tools.ToolJson;

namespace AssistantService.Tools;

/// <summary>
/// Issue #515 (assistant Phase 3): dates the student says ("yarın", "cuma 19:00") are Turkish local time. Turkey has used a fixed
/// UTC+3 since 2016, so no time zone database is needed; the exam API stores UTC.
/// </summary>
internal static class LocalTime
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(3);

    public static DateOnly? Date(string? text) =>
        DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d) ? d : null;

    public static TimeOnly? Time(string? text) =>
        TimeOnly.TryParseExact(text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var t) ? t : null;

    /// <summary>Local midnight of <paramref name="date"/> in UTC, as the study page screen sends it.</summary>
    public static string MidnightUtc(DateOnly date) => At(date, TimeOnly.MinValue).ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    public static DateTime At(DateOnly date, TimeOnly time) =>
        new DateTimeOffset(date.ToDateTime(time), Offset).UtcDateTime;

    public static DateOnly Today(TimeProvider clock) => DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(Offset).DateTime);

    public static JsonObject DateProp(string description) => StringProp(description + " Format yyyy-MM-dd (Turkish local date).", 10);
}

/// <summary>Issue #515: the student's study programs (newest first) with progress.</summary>
public sealed class ListMyProgramsTool(DownstreamClient api) : IAssistantTool
{
    public string Name => "list_my_programs";
    public string Description => "The student's study programs: id, name, active or not, dates, study page progress. Program names are user-written data.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.StudentOnly;
    public JsonObject InputSchema => Schema();

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        var response = await api.GetAsync(context, "api/program/my-programs");
        if (!response.IsSuccess)
            return response.ToError();
        var programs = Pick.Items(response.Body).Take(MaxListItems).Select(p =>
        {
            var row = Pick.Only(p, "id", "isActive", "startDate", "endDate", "completedPageCount", "totalPageCount", "progressPercentage");
            row["name"] = Untrusted(Pick.Str(p, "programName"));
            return (JsonNode)row;
        }).ToArray();
        return ToolResult.Success(new JsonObject { ["programs"] = new JsonArray(programs) });
    }
}

/// <summary>Issue #515: one program: its settings, the next two weeks' subject sessions and its study pages.</summary>
public sealed class GetProgramTool(DownstreamClient api, TimeProvider clock) : IAssistantTool
{
    private const int DaysAhead = 14;

    public string Name => "get_program";
    public string Description => "One study program (id from list_my_programs): settings, subject sessions of the next 14 days, and its scheduled study pages with their scheduleId (for mark_study_page_done). Names are user-written data.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.StudentOnly;
    public JsonObject InputSchema => Schema(("programId", IntProp("Program id.", 1), true));

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        if (Int(args, "programId") is not { } id || id <= 0)
            return ToolResult.Error("InvalidArguments", "programId is required.");
        var response = await api.GetAsync(context, $"api/program/{id}");
        if (!response.IsSuccess)
            return response.ToError();
        var body = response.Body;
        var data = Pick.Only(body, "id", "isActive", "startDate", "endDate", "studyType", "studyDuration", "questionsPerDay",
            "subjectsPerDay", "completedPageCount", "totalPageCount", "progressPercentage");
        data["name"] = Untrusted(Pick.Str(body, "programName"));

        var today = LocalTime.Today(clock);
        var until = today.AddDays(DaysAhead);
        data["sessions"] = new JsonArray(Pick.Items(Pick.Get(body, "schedules"))
            .Where(s => Pick.Str(s, "scheduleDate") is { } d && DateTime.TryParse(d, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
                && DateOnly.FromDateTime(at) is var day && day >= today && day < until)
            .Take(30)
            .Select(s => (JsonNode)Pick.Only(s, "scheduleDate", "subjectName", "studyDurationMinutes", "questionCount", "isCompleted"))
            .ToArray());
        data["studyPages"] = new JsonArray(Pick.Items(Pick.Get(body, "studyItemSchedules")).Take(30).Select(s =>
        {
            var row = Pick.Only(s, "startDate", "endDate", "isCompleted");
            row["scheduleId"] = Pick.Int(s, "id");
            row["title"] = Untrusted(Pick.Str(s, "studyItemTitle"));
            return (JsonNode)row;
        }).ToArray());
        return ToolResult.Success(data);
    }
}

/// <summary>
/// Issue #515: a new study program from the same choices as the program wizard. Creating one deactivates the current program
/// (the API's rule), so the card says so. Only after the student confirms the card.
/// </summary>
public sealed class CreateStudyProgramTool(DownstreamClient api, TimeProvider clock) : IAssistantWriteTool
{
    private const int MaxDays = 180;
    private static readonly string[] Durations = ["25-5", "30-10", "40-10", "50-10"];
    private static readonly int[] QuestionCounts = [8, 12, 16];

    public string Name => "create_study_program";
    public string Description => "Create a study program like the program wizard: studyType time (sessions of studyDuration) or question (questionsPerDay per subject), subjectsPerDay, rest days (1=Monday … 7=Sunday), difficult subjects (ids from the preview's subjects list), dates. Replaces the active program. Needs the student's confirmation.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.StudentOnly;
    public JsonObject InputSchema => Schema(
        ("name", StringProp("Program name.", 60), true),
        ("studyType", StringProp("time or question.", values: ["time", "question"]), true),
        ("studyDuration", StringProp("For time: work-break minutes.", values: Durations), false),
        ("questionsPerDay", IntProp("For question: questions per subject per day (8, 12 or 16).", 8, 16), false),
        ("subjectsPerDay", IntProp("Different subjects per day.", 1, 3), true),
        ("restDays", IntArrayProp("Days without study, 1=Monday … 7=Sunday.", 6), false),
        ("difficultSubjectIds", IntArrayProp("Subjects the student finds hard (ids from the preview's subjects).", 10), false),
        ("startDate", LocalTime.DateProp("First day (today's date for 'from today')."), true),
        ("endDate", LocalTime.DateProp("Last day; default 30 days after the start."), false));

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        var (request, error) = Build(args, commit: false);
        if (error is not null)
            return error;
        var subjects = await api.GetAsync(context, "api/program/subjects");
        if (!subjects.IsSuccess)
            return subjects.ToError();
        var known = Pick.Items(subjects.Body).ToDictionary(s => Pick.Int(s, "id") ?? 0, s => Pick.Str(s, "name"));
        var difficult = request!.DifficultSubjectIds;
        if (difficult.Any(id => !known.ContainsKey(id)))
            return ToolResult.Error("InvalidArguments", "difficultSubjectIds must be among: "
                + string.Join(", ", known.Select(k => $"{k.Key}={k.Value}")));

        var active = await api.GetAsync(context, "api/program/my-programs");
        var replaces = active.IsSuccess && Pick.Items(active.Body).Any(p => Pick.Bool(p, "isActive") == true);
        return ToolResult.Success(new JsonObject
        {
            ["action"] = "createStudyProgram",
            ["name"] = Untrusted(request.Name),
            ["studyType"] = request.StudyType,
            ["studyDuration"] = request.StudyDuration,
            ["questionsPerDay"] = request.QuestionsPerDay,
            ["subjectsPerDay"] = request.SubjectsPerDay,
            ["restDays"] = new JsonArray(request.RestDays.Select(d => (JsonNode)d).ToArray()),
            ["difficultSubjects"] = new JsonArray(difficult.Select(id => (JsonNode)known[id]!).ToArray()),
            ["startDate"] = request.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["endDate"] = request.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["replacesActiveProgram"] = replaces,
            ["subjects"] = SubjectList(known),
        });
    }

    public async Task<ToolResult> CommitAsync(ToolContext context, JsonElement args)
    {
        var (request, error) = Build(args, commit: true);
        if (error is not null)
            return error;
        var selections = new JsonArray
        {
            Selection(1, request!.StudyType),
            request.StudyType == "time" ? Selection(2, request.StudyDuration!) : Selection(3, request.QuestionsPerDay!.Value.ToString(CultureInfo.InvariantCulture)),
            Selection(5, request.SubjectsPerDay.ToString(CultureInfo.InvariantCulture)),
            Selection(6, request.RestDays.Count == 0 ? ["8"] : request.RestDays.Select(d => d.ToString(CultureInfo.InvariantCulture)).ToArray()),
        };
        var response = await api.PostAsync(context, "api/program/create", new JsonObject
        {
            ["programName"] = request.Name,
            ["description"] = string.Empty,
            ["startDate"] = request.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["endDate"] = request.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["userSelections"] = selections,
            ["difficultSubjectIds"] = new JsonArray(request.DifficultSubjectIds.Select(id => (JsonNode)id).ToArray()),
        });
        if (!response.IsSuccess)
            return response.ToError();
        var id = Pick.Int(response.Body, "id");
        return ToolResult.Success(new JsonObject { ["programId"] = id, ["totalSessions"] = Pick.Items(Pick.Get(response.Body, "schedules")).Count() },
            uiActions: id is { } programId ? [UiScreens.Navigate("program", programId)] : null);
    }

    private static JsonObject Selection(int stepId, params string[] values) => new()
    {
        ["stepId"] = stepId,
        ["selectedValues"] = new JsonArray(values.Select(v => (JsonNode)v).ToArray()),
    };

    private static JsonArray SubjectList(Dictionary<int, string?> known) =>
        new(known.Select(k => (JsonNode)new JsonObject { ["id"] = k.Key, ["name"] = k.Value }).ToArray());

    private sealed record Request(string Name, string StudyType, string? StudyDuration, int? QuestionsPerDay, int SubjectsPerDay,
        List<int> RestDays, List<int> DifficultSubjectIds, DateOnly Start, DateOnly End);

    /// <summary>
    /// The stored args are parsed again on commit; dates come only from them (startDate is required), so the card and the commit
    /// agree. A card confirmed just after midnight keeps its start day (one day of grace on commit).
    /// </summary>
    private (Request? Request, ToolResult? Error) Build(JsonElement args, bool commit)
    {
        static (Request?, ToolResult?) Invalid(string message) => (null, ToolResult.Error("InvalidArguments", message));

        var name = String(args, "name")?.Trim();
        if (string.IsNullOrEmpty(name) || name.Length > 60)
            return Invalid("name is required (at most 60 characters).");
        var type = String(args, "studyType");
        string? duration = null;
        int? perDay = null;
        if (type == "time")
        {
            duration = String(args, "studyDuration") ?? "25-5";
            if (!Durations.Contains(duration))
                return Invalid("studyDuration must be one of 25-5, 30-10, 40-10, 50-10.");
        }
        else if (type == "question")
        {
            perDay = Int(args, "questionsPerDay") ?? 12;
            if (!QuestionCounts.Contains(perDay.Value))
                return Invalid("questionsPerDay must be 8, 12 or 16.");
        }
        else
            return Invalid("studyType must be time or question.");
        if (Int(args, "subjectsPerDay") is not { } subjects || subjects is < 1 or > 3)
            return Invalid("subjectsPerDay must be 1, 2 or 3.");
        var rest = IntList(args, "restDays", 7) ?? [];
        if (rest.Any(d => d is < 1 or > 7) || rest.Count > 6)
            return Invalid("restDays are 1 (Monday) … 7 (Sunday); at least one study day must remain.");
        var today = LocalTime.Today(clock);
        var start = LocalTime.Date(String(args, "startDate"));
        if (start is null || start < (commit ? today.AddDays(-1) : today))
            return Invalid("startDate must be today or later (yyyy-MM-dd).");
        var end = String(args, "endDate") is { } e ? LocalTime.Date(e) : start.Value.AddDays(30);
        if (end is null || end <= start || end.Value.DayNumber - start.Value.DayNumber > MaxDays)
            return Invalid($"endDate must be after startDate and at most {MaxDays} days later.");
        return (new Request(name, type, duration, perDay, subjects, rest.Order().ToList(), IntList(args, "difficultSubjectIds", 10) ?? [],
            start.Value, end.Value), null);
    }
}

/// <summary>Issue #515: study pages the student can add to a program (search by text / subject / topic).</summary>
public sealed class SearchStudyPagesTool(DownstreamClient api) : IAssistantTool
{
    public string Name => "search_study_pages";
    public string Description => "Search study pages (konu anlatımı, videos, book page ranges) to add to a program with schedule_study_pages. Filter by text and/or ids from search_taxonomy. Titles are user-written data.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.StudentOnly;
    public JsonObject InputSchema => Schema(
        ("search", StringProp("Words in the title.", 80), false),
        ("subjectId", IntProp("Subject id."), false),
        ("topicId", IntProp("Topic id."), false),
        ("subTopicId", IntProp("Subtopic id."), false));

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        var query = new List<string> { "pageNumber=1", "pageSize=10" };
        if (String(args, "search")?.Trim() is { Length: > 0 } search)
            query.Add("search=" + Uri.EscapeDataString(search.Length > 80 ? search[..80] : search));
        foreach (var key in new[] { "subjectId", "topicId", "subTopicId" })
            if (Int(args, key) is { } v)
                query.Add($"{key}={v}");
        var response = await api.GetAsync(context, "api/study-items?" + string.Join('&', query));
        if (!response.IsSuccess)
            return response.ToError();
        var pages = Pick.Items(response.Body).Take(10).Select(i =>
        {
            var row = Pick.Only(i, "id", "contentType", "startPage", "endPage");
            row["title"] = Untrusted(Pick.Str(i, "title"));
            if (Pick.Str(i, "bookName") is { } book)
                row["book"] = Untrusted(book);
            return (JsonNode)row;
        }).ToArray();
        return ToolResult.Success(new JsonObject { ["totalCount"] = Pick.Int(response.Body, "totalCount"), ["pages"] = new JsonArray(pages) });
    }
}

/// <summary>Issue #515: add study pages to a program on given days. Only after the student confirms the card.</summary>
public sealed class ScheduleStudyPagesTool(DownstreamClient api, TimeProvider clock) : IAssistantWriteTool
{
    private const int MaxItems = 10;

    public string Name => "schedule_study_pages";
    public string Description => "Add study pages (ids from search_study_pages) to a program (id from list_my_programs), each from startDate to endDate. Needs the student's confirmation.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.StudentOnly;
    public JsonObject InputSchema => Schema(
        ("programId", IntProp("Program id.", 1), true),
        ("items", new JsonObject
        {
            ["type"] = "array",
            ["maxItems"] = MaxItems,
            ["description"] = "Pages and their days.",
            ["items"] = Schema(
                ("studyItemId", IntProp("Study page id.", 1), true),
                ("startDate", LocalTime.DateProp("First day."), true),
                ("endDate", LocalTime.DateProp("Last day; default the start day."), false)),
        }, true));

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        var (programId, items, error) = Parse(args, commit: false);
        if (error is not null)
            return error;
        // Each page through the student's scoped single-item read: an id out of the plan / not visible is refused here.
        var titles = new Dictionary<int, string?>();
        foreach (var item in items)
        {
            var page = await api.GetAsync(context, $"api/study-items/{item.Id}");
            if (!page.IsSuccess)
                return ToolResult.Error("StudyPageNotAvailable", $"Study page {item.Id} is not available; use ids from search_study_pages.");
            titles[item.Id] = Pick.Str(page.Body, "title");
        }
        var program = await api.GetAsync(context, $"api/program/{programId}");
        if (!program.IsSuccess)
            return program.ToError();
        return ToolResult.Success(new JsonObject
        {
            ["action"] = "scheduleStudyPages",
            ["programId"] = programId,
            ["programName"] = Untrusted(Pick.Str(program.Body, "programName")),
            ["items"] = new JsonArray(items.Select(i => (JsonNode)new JsonObject
            {
                ["studyItemId"] = i.Id,
                ["title"] = Untrusted(titles[i.Id]),
                ["startDate"] = i.Start.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                ["endDate"] = i.End.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            }).ToArray()),
        });
    }

    public async Task<ToolResult> CommitAsync(ToolContext context, JsonElement args)
    {
        var (programId, items, error) = Parse(args, commit: true);
        if (error is not null)
            return error;
        var response = await api.PostAsync(context, $"api/program/{programId}/study-pages", new JsonObject
        {
            ["items"] = new JsonArray(items.Select(i => (JsonNode)new JsonObject
            {
                ["studyItemId"] = i.Id,
                ["startDate"] = LocalTime.MidnightUtc(i.Start),
                ["endDate"] = LocalTime.MidnightUtc(i.End),
            }).ToArray()),
        });
        if (!response.IsSuccess)
            return response.ToError();
        return ToolResult.Success(new JsonObject { ["programId"] = programId, ["added"] = items.Count },
            uiActions: [UiScreens.Navigate("program", programId)]);
    }

    private sealed record Item(int Id, DateOnly Start, DateOnly End);

    private (int ProgramId, List<Item> Items, ToolResult? Error) Parse(JsonElement args, bool commit)
    {
        static (int, List<Item>, ToolResult?) Invalid(string message) => (0, [], ToolResult.Error("InvalidArguments", message));

        if (Int(args, "programId") is not { } programId || programId <= 0)
            return Invalid("programId is required.");
        if (args.ValueKind != JsonValueKind.Object || !args.TryGetProperty("items", out var list) || list.ValueKind != JsonValueKind.Array)
            return Invalid("items are required.");
        var today = LocalTime.Today(clock).AddDays(commit ? -1 : 0);
        var items = new List<Item>();
        foreach (var e in list.EnumerateArray().Take(MaxItems))
        {
            var start = LocalTime.Date(String(e, "startDate"));
            var end = String(e, "endDate") is { } t ? LocalTime.Date(t) : start;
            if (Int(e, "studyItemId") is not { } id || id <= 0 || start is null || end is null || start < today || end < start)
                return Invalid("Each item needs studyItemId and startDate (today or later); endDate not before startDate.");
            if (items.All(i => i.Id != id))
                items.Add(new Item(id, start.Value, end.Value));
        }
        return items.Count == 0 ? Invalid("items are required.") : (programId, items, null);
    }
}

/// <summary>Issue #515: mark a scheduled study page done (or not done) in a program. Only after the student confirms.</summary>
public sealed class MarkStudyPageDoneTool(DownstreamClient api) : IAssistantWriteTool
{
    public string Name => "mark_study_page_done";
    public string Description => "Mark a program's study page done, or not done with done=false (programId, scheduleId from get_program). Needs the student's confirmation.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.StudentOnly;
    public JsonObject InputSchema => Schema(
        ("programId", IntProp("Program id.", 1), true),
        ("scheduleId", IntProp("The study page's scheduleId from get_program.", 1), true),
        ("done", BoolProp("false to undo; default true."), false));

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        if (Int(args, "programId") is not { } programId || programId <= 0 || Int(args, "scheduleId") is not { } scheduleId || scheduleId <= 0)
            return ToolResult.Error("InvalidArguments", "programId and scheduleId are required.");
        var program = await api.GetAsync(context, $"api/program/{programId}");
        if (!program.IsSuccess)
            return program.ToError();
        var page = Pick.Items(Pick.Get(program.Body, "studyItemSchedules")).FirstOrDefault(s => Pick.Int(s, "id") == scheduleId);
        if (page is null)
            return ToolResult.Error("NotFound", "No such study page in this program; call get_program.");
        return ToolResult.Success(new JsonObject
        {
            ["action"] = "markStudyPageDone",
            ["programId"] = programId,
            ["scheduleId"] = scheduleId,
            ["title"] = Untrusted(Pick.Str(page, "studyItemTitle")),
            ["done"] = Bool(args, "done") ?? true,
            ["alreadyDone"] = Pick.Bool(page, "isCompleted"),
        });
    }

    public async Task<ToolResult> CommitAsync(ToolContext context, JsonElement args)
    {
        if (Int(args, "programId") is not { } programId || programId <= 0 || Int(args, "scheduleId") is not { } scheduleId || scheduleId <= 0)
            return ToolResult.Error("InvalidArguments", "programId and scheduleId are required.");
        var path = $"api/program/{programId}/study-pages/{scheduleId}/complete";
        var done = Bool(args, "done") ?? true;
        var response = done ? await api.PutAsync(context, path, null) : await api.DeleteAsync(context, path);
        return response.IsSuccess
            ? ToolResult.Success(new JsonObject { ["programId"] = programId, ["scheduleId"] = scheduleId, ["done"] = done })
            : response.ToError();
    }
}

/// <summary>Issue #515: tests the student can solve (text, subject, status).</summary>
public sealed class SearchWorksheetsTool(DownstreamClient api) : IAssistantTool
{
    public string Name => "search_worksheets";
    public string Description => "Search the tests open to the student (all statuses unless filtered): by words in the name, subject ids (search_taxonomy) and status (notStarted, inProgress, completed). Each row has status; a completed test can be solved again. Gives worksheetId for start_test / set_worksheet_reminder. Names are user-written data.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.StudentOnly;
    public JsonObject InputSchema => Schema(
        ("search", StringProp("Words in the test name.", 80), false),
        ("subjectIds", IntArrayProp("Subject ids.", 5), false),
        ("status", StringProp("Status filter.", values: ["notStarted", "inProgress", "completed"]), false));

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        var query = new List<string> { "pageNumber=1", "pageSize=10", "kind=0" };
        if (String(args, "search")?.Trim() is { Length: > 0 } search)
            query.Add("search=" + Uri.EscapeDataString(search.Length > 80 ? search[..80] : search));
        foreach (var s in IntList(args, "subjectIds", 5) ?? [])
            query.Add($"subjectIds={s}");
        if (String(args, "status") switch { "notStarted" => "-1", "inProgress" => "0", "completed" => "1", _ => null } is { } status)
            query.Add("statuses=" + status);
        var response = await api.GetAsync(context, "api/worksheet/list?" + string.Join('&', query));
        if (!response.IsSuccess)
            return response.ToError();
        var tests = Pick.Items(response.Body).Take(10).Select(w =>
        {
            var row = Pick.Only(w, "questionCount", "isAssigned");
            row["worksheetId"] = Pick.Int(w, "id");
            row["name"] = Untrusted(Pick.Str(w, "name"));
            if (Pick.Int(w, "maxDurationSeconds") is { } seconds && seconds > 0)
                row["durationMinutes"] = (seconds + 59) / 60;
            if (Pick.Get(w, "instance") is JsonObject instance)
            {
                var completed = Pick.Str(instance, "completedDate") is { } at && !at.StartsWith("0001", StringComparison.Ordinal);
                row["status"] = completed ? "completed" : "inProgress";
                if (completed)
                    row["lastResult"] = Pick.Only(instance, "score", "correctAnswers", "totalQuestions", "completedDate");
            }
            else
                row["status"] = "notStarted";
            return (JsonNode)row;
        }).ToArray();
        return ToolResult.Success(new JsonObject { ["totalCount"] = Pick.Int(response.Body, "totalCount"), ["tests"] = new JsonArray(tests) });
    }
}

/// <summary>
/// Issue #515: start a test. After the confirmation the test screen opens; the assistant is closed there (the layout suppresses
/// it during a test, and the chat refuses AssistantClosedDuringTest).
/// </summary>
public sealed class StartTestTool(DownstreamClient api) : IAssistantWriteTool
{
    public string Name => "start_test";
    public string Description => "Start a test (worksheetId from search_worksheets or the screen context). The test screen opens and the assistant closes during the test. Needs the student's confirmation.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.StudentOnly;
    public JsonObject InputSchema => Schema(("worksheetId", IntProp("Worksheet id.", 1), true));

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        if (Int(args, "worksheetId") is not { } id || id <= 0)
            return ToolResult.Error("InvalidArguments", "worksheetId is required.");
        // The scoped student list, not GET worksheet/{id} (which names any worksheet): only tests open to the student.
        var listed = await api.GetAsync(context, $"api/worksheet/list?id={id}&pageNumber=1&pageSize=1");
        if (!listed.IsSuccess)
            return listed.ToError();
        var worksheet = Pick.Items(listed.Body).FirstOrDefault(w => Pick.Int(w, "id") == id);
        if (worksheet is null)
            return ToolResult.Error("NotFound", "No such test for this student; call search_worksheets.");
        if (Pick.Int(worksheet, "kind") is 1)
            return ToolResult.Error("NotATest", "This is an activity; open it with ui_open_screen worksheet.");
        var preview = new JsonObject
        {
            ["action"] = "startTest",
            ["worksheetId"] = id,
            ["name"] = Untrusted(Pick.Str(worksheet, "name")),
            ["questionCount"] = Pick.Int(worksheet, "questionCount"),
        };
        if (Pick.Int(worksheet, "maxDurationSeconds") is { } seconds && seconds > 0)
            preview["durationMinutes"] = (seconds + 59) / 60;
        return ToolResult.Success(preview);
    }

    public async Task<ToolResult> CommitAsync(ToolContext context, JsonElement args)
    {
        if (Int(args, "worksheetId") is not { } id || id <= 0)
            return ToolResult.Error("InvalidArguments", "worksheetId is required.");
        var response = await api.PostAsync(context, $"api/worksheet/start-test/{id}", null);
        if (!response.IsSuccess)
            return response.ToError();
        var instanceId = Pick.Int(response.Body, "instanceId");
        return ToolResult.Success(new JsonObject { ["instanceId"] = instanceId },
            uiActions: instanceId is > 0 ? [UiScreens.NavigateToTest(instanceId.Value)] : null);
    }
}

/// <summary>Issue #515: remind the student before a test they plan to solve ("cuma 19:00'da bu testi çözeceğim, hatırlat").</summary>
public sealed class SetWorksheetReminderTool(DownstreamClient api, TimeProvider clock) : IAssistantWriteTool
{
    public string Name => "set_worksheet_reminder";
    public string Description => "Plan an assigned test (isAssigned in search_worksheets) for a local date and time and get a notification remindBeforeMinutes earlier (default 60, at most 1440). Replaces the test's existing reminder. Not for unassigned tests (NotAssigned). Needs the student's confirmation.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.StudentOnly;
    public JsonObject InputSchema => Schema(
        ("worksheetId", IntProp("Worksheet id.", 1), true),
        ("date", LocalTime.DateProp("Day of the plan."), true),
        ("time", StringProp("Local time HH:mm.", 5), true),
        ("remindBeforeMinutes", IntProp("Minutes before.", 0, 1440), false));

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        var (id, at, before, error) = Parse(args);
        if (error is not null)
            return error;
        // The exam API keeps reminders for tests a teacher assigned; only the list answers isAssigned.
        var listed = await api.GetAsync(context, $"api/worksheet/list?id={id}&pageNumber=1&pageSize=1");
        if (!listed.IsSuccess)
            return listed.ToError();
        var worksheet = Pick.Items(listed.Body).FirstOrDefault(w => Pick.Int(w, "id") == id);
        if (worksheet is null)
            return ToolResult.Error("NotFound", "No such test for this student; call search_worksheets.");
        if (Pick.Bool(worksheet, "isAssigned") != true)
            return ToolResult.Error("NotAssigned", "Reminders are only for tests a teacher assigned to the student.");
        var existing = await api.GetAsync(context, $"api/worksheet/{id}/reminder");
        return ToolResult.Success(new JsonObject
        {
            ["action"] = "setWorksheetReminder",
            ["worksheetId"] = id,
            ["name"] = Untrusted(Pick.Str(worksheet, "name")),
            ["date"] = String(args, "date"),
            ["time"] = String(args, "time"),
            ["remindBeforeMinutes"] = before,
            ["replacesReminder"] = existing.IsSuccess && Pick.Str(existing.Body, "status") == "Pending",
        });
    }

    public async Task<ToolResult> CommitAsync(ToolContext context, JsonElement args)
    {
        var (id, at, before, error) = Parse(args);
        if (error is not null)
            return error;
        var response = await api.PutAsync(context, $"api/worksheet/{id}/reminder", new JsonObject
        {
            ["scheduledFor"] = at.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture),
            ["remindBeforeMinutes"] = before,
        });
        return response.IsSuccess
            ? ToolResult.Success(new JsonObject { ["worksheetId"] = id, ["date"] = String(args, "date"), ["time"] = String(args, "time"), ["remindBeforeMinutes"] = before })
            : response.ToError();
    }

    private (int Id, DateTime At, int Before, ToolResult? Error) Parse(JsonElement args)
    {
        static (int, DateTime, int, ToolResult?) Invalid(string message) => (0, default, 0, ToolResult.Error("InvalidArguments", message));

        if (Int(args, "worksheetId") is not { } id || id <= 0)
            return Invalid("worksheetId is required.");
        if (LocalTime.Date(String(args, "date")) is not { } date || LocalTime.Time(String(args, "time")) is not { } time)
            return Invalid("date (yyyy-MM-dd) and time (HH:mm) are required.");
        var at = LocalTime.At(date, time);
        if (at <= clock.GetUtcNow().UtcDateTime)
            return Invalid("The time must be in the future.");
        var before = Int(args, "remindBeforeMinutes") ?? 60;
        if (before is < 0 or > 1440)
            return Invalid("remindBeforeMinutes must be 0 … 1440.");
        return (id, at, before, null);
    }
}

/// <summary>Issue #515: cancel a test's reminder. Only after the student confirms.</summary>
public sealed class DeleteWorksheetReminderTool(DownstreamClient api) : IAssistantWriteTool
{
    public string Name => "delete_worksheet_reminder";
    public string Description => "Cancel the reminder of a test. Needs the student's confirmation.";
    public IReadOnlyCollection<string> Roles => Tools.Roles.StudentOnly;
    public JsonObject InputSchema => Schema(("worksheetId", IntProp("Worksheet id.", 1), true));

    public async Task<ToolResult> ExecuteAsync(ToolContext context, JsonElement args)
    {
        if (Int(args, "worksheetId") is not { } id || id <= 0)
            return ToolResult.Error("InvalidArguments", "worksheetId is required.");
        var existing = await api.GetAsync(context, $"api/worksheet/{id}/reminder");
        if (!existing.IsSuccess)
            return existing.ToError();
        if (Pick.Str(existing.Body, "status") != "Pending")
            return ToolResult.Error("NoReminder", "This test has no pending reminder.");
        var listed = await api.GetAsync(context, $"api/worksheet/list?id={id}&pageNumber=1&pageSize=1");
        var worksheet = listed.IsSuccess ? Pick.Items(listed.Body).FirstOrDefault(w => Pick.Int(w, "id") == id) : null;
        return ToolResult.Success(new JsonObject
        {
            ["action"] = "deleteWorksheetReminder",
            ["worksheetId"] = id,
            ["name"] = Untrusted(Pick.Str(worksheet, "name")),
            ["scheduledFor"] = Pick.Str(existing.Body, "scheduledFor"),
        });
    }

    public async Task<ToolResult> CommitAsync(ToolContext context, JsonElement args)
    {
        if (Int(args, "worksheetId") is not { } id || id <= 0)
            return ToolResult.Error("InvalidArguments", "worksheetId is required.");
        var response = await api.DeleteAsync(context, $"api/worksheet/{id}/reminder");
        return response.IsSuccess ? ToolResult.Success(new JsonObject { ["worksheetId"] = id, ["deleted"] = true }) : response.ToError();
    }
}
